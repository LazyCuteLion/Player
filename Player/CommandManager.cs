using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Player
{
    public class CommandManager
    {
        private static System.Threading.CancellationTokenSource cancellationToken;
        private static System.IO.Ports.SerialPort com;

        public static void Start(string name, TimeSpan time)
        {
            Stop();
            var n = name.LastIndexOf(".");
            name = name.Substring(0, n) + "_cmd.ini";
            if (File.Exists(name))
            {
                var lines = File.ReadAllLines(name);
                if (lines.Length > 0)
                {
                    cancellationToken = new System.Threading.CancellationTokenSource();

                    foreach (var item in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(item))
                        {
                            var temp = item.Split('-');
                            if (TimeSpan.TryParse(temp[0], out TimeSpan t) && t > time)
                            {
                                Task.Delay(t - time, cancellationToken.Token).ContinueWith((task, p) =>
                                  {
                                      if (!task.IsCanceled)
                                      {
                                          Send(p.ToString());
                                      }
                                  }, temp[1]).ConfigureAwait(false);
                            }
                            else if (temp[0].Equals("start", StringComparison.OrdinalIgnoreCase) && time < TimeSpan.FromMilliseconds(100))
                            {
                                Send(temp[1]);
                            }
                        }
                    }
                }
            }
        }

        public static void Stop(string name = "")
        {
            try
            {
                cancellationToken?.Cancel();
                cancellationToken?.Dispose();
                cancellationToken = null;

                if (!string.IsNullOrEmpty(name))
                {
                    var n = name.LastIndexOf(".");
                    name = name.Substring(0, n) + "_cmd.ini";
                    if (File.Exists(name))
                    {
                        var lines = File.ReadAllLines(name).Where(l => l.StartsWith("stop", StringComparison.OrdinalIgnoreCase)).ToArray();
                        if (lines.Length > 0)
                        {
                            Task.Run(() =>
                            {
                                foreach (var item in lines)
                                {
                                    Send(item.Substring(5));
                                    Task.Delay(100).Wait();
                                }
                            });
                        }
                    }
                }
            }
            catch { }
        }

        public static void Send(string cmd)
        {
            if (string.IsNullOrWhiteSpace(System.Configuration.ConfigurationManager.AppSettings["Com"]))
                return;
            if (com == null)
                com = new System.IO.Ports.SerialPort(System.Configuration.ConfigurationManager.AppSettings["Com"]);
            try
            {
                if (!com.IsOpen)
                    com.Open();
                var s = cmd;
                if (s.StartsWith("0x"))
                {
                    s = s.Substring(2);
                    var d = new byte[s.Length / 2];
                    for (int i = 0; i < d.Length; i++)
                    {
                        d[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
                    }
                    com.Write(d, 0, d.Length);
                }
                else
                {
                    com.Write(s);
                }
            }
            catch { }
        }
    }
}
