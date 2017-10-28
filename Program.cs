// #define USE_RANDOM_TEST_DATA

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Numerics;

using static System.Math;

namespace UDPOscilloscope
{
    public static class Program
    {
        private const double BORDER = .03;
        private const int PORT = 31488;
        private const int SP_PER_PCK = 0;
        private static readonly Queue<byte[]> queue = new Queue<byte[]>();
#if USE_RANDOM_TEST_DATA
        private static readonly Random rand = new Random();
#endif

        [MTAThread]
        public static int Main(string[] argv)
        {
            bool done = false;

            new Task(() =>
            {
                Display d = null;

                d = new Display(() =>
                {
#if !USE_RANDOM_TEST_DATA
                    if (queue.Count > 0)
#endif
                    {
                        StackFrame[] trace = new StackTrace().GetFrames();
                        MethodBase current = trace[0].GetMethod();
                        int recursion_depth = (from cfrm in trace
                                               let meth = cfrm?.GetMethod()
                                               where current.Equals(meth)
                                               select cfrm).Count();

                        // on slow computers, the timer update rate might be higher than the winmsg queue handling task rate
                        // --> application.doevents calls this delegate on UI refreshing because it should be handled
                        // --> endless recursion because this thread is sending UI updates
                        // --> stack overflow
                        // --> quit when recursion depth is > 24 (it can be over 1k without problems, but still...)
                        if (recursion_depth > 24)
                        {
                            Console.WriteLine("Having a hard time keeping up ..... halp!");
#if !USE_RANDOM_TEST_DATA
                            queue.Dequeue(); // Do nothing, just pop the oldest frame and return
#endif
                            return;
                        }

#if USE_RANDOM_TEST_DATA
                        byte[] frame = new byte[1024];

                        Parallel.For(0, frame.Length, i => frame[i] = (byte)(128 + 127 * Sin((1.2 + Sin(DateTime.Now.Millisecond * PI / 1000)) * 0.03 * i)));
#else
                        byte[] frame = queue.Dequeue();
#endif
                        double[] dY = new double[frame.Length / 2];
                        double[] dX = new double[dY.Length];

                        Parallel.For(0, dY.Length, i => (dY[i], dX[i]) = ((frame[i * 2] << 8) | frame[i * 2 + 1], i));
                        StringBuilder sb = new StringBuilder();

                        double[] dYf = FFT(dY).Skip(1).ToArray();
                        double dYfmax = dYf.Max();
                        var most_occuring = from val in dY
                                            let v = (ushort)val
                                            group v by v into vg
                                            let cnt = vg.Count()
                                            orderby cnt descending
                                            select new
                                            {
                                                Key = vg.Key,
                                                Num = cnt,
                                                Rel = cnt / (double)dY.Length
                                            };
                        var peak_frequencies = from val in dYf.Zip(dX, (y, x) => (x: x, y: y))
                                               let corr = new
                                               {
                                                   Freq = val.x,
                                                   Amp = val.y,
                                                   Norm = val.y / dYfmax
                                               }
                                               orderby corr.Amp descending
                                               select corr;

                        foreach (var entry in most_occuring.Take(20))
                            sb.AppendLine($"{entry.Key:x4}h ({entry.Key,5})  ---->  {entry.Rel * 100d:N3}% ({entry.Num})");

                        sb.AppendLine(new string('-', 25));

                        foreach (var entry in peak_frequencies.Take(20))
                            sb.AppendLine($"{entry.Freq,4}Hz  ---->  {entry.Norm:F3}% ({entry.Amp:F2})");

                        d.textBox1.Text = sb.ToString();
                        d.scottPlotUC1.Xs = dX;
                        d.scottPlotUC1.Ys = dY;
                        d.scottPlotUC1.SP.AxisSet(-dX.Length * BORDER, dX.Length * (1 + BORDER), -BORDER * ushort.MaxValue, (1 + BORDER) * ushort.MaxValue);
                        d.scottPlotUC1.UpdateGraph();

                        d.scottPlotUC2.Xs = dX;
                        d.scottPlotUC2.Ys = dYf;
                        d.scottPlotUC2.SP.AxisSet(-dX.Length * BORDER, dX.Length * (1 + BORDER), -BORDER * dYfmax, (1 + BORDER) * dYfmax);
                        d.scottPlotUC2.UpdateGraph();
                    }
                });
                d.FormClosing += (o, a) => done = true;
                d.ShowDialog();
                d.Dispose();

                Console.WriteLine("Press ESC to exit...");

                done = true;
            }).Start();
            new Task(() =>
            {
                using (UdpClient listener = new UdpClient(PORT))
                {
                    IPEndPoint groupEP = new IPEndPoint(IPAddress.Any, PORT);
                    byte[] buffer;

                    try
                    {
                        while (!done)
                        {
                            Console.WriteLine("Waiting for broadcast ...");

                            buffer = listener.Receive(ref groupEP);

                            Console.WriteLine($"Received a broadcast from {groupEP} with a length of {buffer.Length / 1024f:F1}kB");

                            for (int i = 0; i < 1 << SP_PER_PCK; ++i)
                            {
                                byte[] copy = new byte[buffer.Length >> SP_PER_PCK];

                                Parallel.For(0, copy.Length, j => copy[j] = buffer[i * copy.Length + j]);

                                queue.Enqueue(copy);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        StringBuilder sb = new StringBuilder();

                        while (e != null)
                        {
                            sb.Insert(0, e.Message + e.StackTrace);

                            e = e.InnerException;
                        }

                        Console.WriteLine(sb.ToString());
                    }

                    done = true;
                }

                Console.WriteLine("Press ESC to exit ...");
            }).Start();

            do
                while (!done)
                    Thread.Sleep(100);
            while (Console.ReadKey(true).Key != ConsoleKey.Escape);

            return 0;
        }

        public static double[] FFT(double[] data)
        {
            Complex[] fftc = new Complex[data.Length];
            double[] fft = new double[data.Length];

            Parallel.For(0, data.Length, i => fftc[i] = new Complex(data[i], 0));

            FFT(ref fftc);

            Parallel.For(0, data.Length, i => fft[i] = fftc[i].Magnitude);

            return fft;
        }

        public static void FFT(ref Complex[] buffer)
        {
            int bits = (int)Log(buffer.Length, 2);

            for (int j = 1, l = buffer.Length / 2; j < l; j++)
            {
                int swapPos = BitReverse(j, bits);
                Complex temp = buffer[j];

                buffer[j] = buffer[swapPos];
                buffer[swapPos] = temp;
            }

            for (int N = 2; N <= buffer.Length; N <<= 1)
                for (int i = 0; i < buffer.Length; i += N)
                    for (int k = 0; k < N / 2; k++)
                    {
                        int evenIndex = i + k;
                        int oddIndex = i + k + (N / 2);
                        var even = buffer[evenIndex];
                        var odd = buffer[oddIndex];

                        double term = -2 * k * PI / N;
                        Complex exp = new Complex(Cos(term), Sin(term)) * odd;

                        buffer[evenIndex] = even + exp;
                        buffer[oddIndex] = even - exp;
                    }
        }

        public static int BitReverse(int n, int bits)
        {
            int reversedN = n;
            int count = bits - 1;

            n >>= 1;

            while (n > 0)
            {
                reversedN = (reversedN << 1) | (n & 1);
                count--;
                n >>= 1;
            }

            return ((reversedN << count) & ((1 << bits) - 1));
        }
    }
}