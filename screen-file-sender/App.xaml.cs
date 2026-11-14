using System;
using System.Globalization;
using System.Threading;
using System.Windows;

namespace screen_file_transmit
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            Thread.CurrentThread.CurrentUICulture = CultureInfo.CurrentUICulture;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // Handle `-v` / `--version` before the main window is shown.
            if (VersionFlag.Handle(e.Args))
                return; // VersionFlag exits the process itself

            base.OnStartup(e);
        }
    }
}