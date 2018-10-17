using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using static System.Configuration.ConfigurationManager;

namespace Player
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        protected async override void OnStartup(StartupEventArgs e)
        {
            var cmd = AppSettings["OnStarted"];
            if (!string.IsNullOrEmpty(cmd))
            {
                foreach (var item in cmd.Split(','))
                {
                    CommandManager.Send(item);
                    await Task.Delay(100).ConfigureAwait(false);
                }
            }
            base.OnStartup(e);
        }

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            if (e.ReasonSessionEnding == ReasonSessionEnding.Shutdown)
            {
                var cmd = AppSettings["OnShutdown"];
                if (!string.IsNullOrEmpty(cmd))
                {
                    foreach (var item in cmd.Split(','))
                    {
                        CommandManager.Send(item);
                        Task.Delay(100).Wait();
                    }
                    Task.Delay(1000);
                }
            }
            base.OnSessionEnding(e);
        }
    }
}
