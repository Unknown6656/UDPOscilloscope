using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UDPOscilloscope
{
    public static class Program
    {
        private const double BORDER = .05;
        private const int PORT = 31488;
        private const int SP_PER_PCK = 0;
        private static readonly Queue<byte[]> queue = new Queue<byte[]>();

        [MTAThread]
        public static int Main(string[] argv)
        {
            bool done = false;

            new Task(() =>
            {
                Display d = null;

                d = new Display(() =>
                {
                    if (queue.Count > 0)
                    {
                        byte[] frame = queue.Dequeue();
                        double[] dY = new double[frame.Length / 2];
                        double[] dX = new double[dY.Length];

                        Parallel.For(0, dY.Length, i => (dY[i], dX[i]) = ((frame[i * 2] << 8) | frame[i * 2 + 1], i));
                        StringBuilder sb = new StringBuilder();

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

                        foreach (var entry in most_occuring)
                            sb.AppendLine($"{entry.Key:x4}h ({entry.Key,6})  ---->  {entry.Rel * 100d:N3}%   ({entry.Num})");

                        d.textBox1.Text = sb.ToString();
                        d.scottPlotUC1.Xs = dX;
                        d.scottPlotUC1.Ys = dY;
                        d.scottPlotUC1.SP.AxisSet(-dX.Length * BORDER, dX.Length * (1 + BORDER), -BORDER * ushort.MaxValue, (1 + BORDER) * ushort.MaxValue);
                        d.scottPlotUC1.UpdateGraph();

                        Application.DoEvents();

                        d.Update();
                    }
                });
                d.FormClosing += (o, a) => done = true;
                d.ShowDialog();
                d.Dispose();

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
            {
                while (!done)
                    Application.DoEvents();
            }
            while (Console.ReadKey(true).Key != ConsoleKey.Escape);
            
            return 0;
        }
    }
}