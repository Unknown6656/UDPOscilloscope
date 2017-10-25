using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UDPOscilloscope
{
    public static class Program
    {
        private const int listenPort = 31488;
        private static readonly Queue<byte[]> queue = new Queue<byte[]>();


        public static int Main(string[] argv)
        {
            using (Display disp = new Display())
            using (UdpClient listener = new UdpClient(listenPort))
            {
                IPEndPoint groupEP = new IPEndPoint(IPAddress.Any, listenPort);
                byte[] buffer;
                bool done = false;

                disp.Load += delegate
                {
                    new Task(async () =>
                    {
                        while (!done)
                        {
                            if (queue.Count > 0)
                            {
                                byte[] frame = queue.Dequeue();
                                double[] dY = new double[frame.Length / 2];
                                double[] dX = new double[dY.Length];

                                Parallel.For(0, dY.Length, i => (dY[i], dX[i]) = ((frame[i * 2] << 8) | frame[i * 2 + 1], i));

                                disp.Invoke(new MethodInvoker(() =>
                                {
                                    disp.scottPlotUC1.Xs = dX;
                                    disp.scottPlotUC1.Ys = dY;
                                    disp.scottPlotUC1.UpdateGraph();

                                    Application.DoEvents();

                                    disp.Update();
                                }));
                            }
                            else
                                await Task.Delay(50);

                            Application.DoEvents();
                        }
                    }).Start();
                };
                disp.FormClosing += (s, e) => done = true;
                disp.Show();

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

                
            }

            return 0;
        }
    }
}