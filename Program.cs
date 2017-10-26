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
        private const int PORT = 31488;
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

                        Parallel.For(0, dY.Length, i => (dY[i], dX[i]) = ((double)((frame[i * 2] << 8) | frame[i * 2 + 1]) / ushort.MaxValue, i));

                        d.scottPlotUC1.Xs = dX;
                        d.scottPlotUC1.Ys = dY;
                        d.scottPlotUC1.SP.AxisSet(-10, dX.Length + 10, -.1, 1.1);
                        d.scottPlotUC1.UpdateGraph();

                        Application.DoEvents();

                        d.Update();
                    }
                });
                d.FormClosing += (o, a) => done = true;
                d.ShowDialog();
                d.Dispose();
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

                            queue.Enqueue(buffer);
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