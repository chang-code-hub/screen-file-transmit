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

#if DEBUG
            // 调试 UI 时：WinExe 没有控制台，把 Console 输出重定向到 Trace，
            // 附加调试器后可在 VS 输出窗口看到日志。必须放在 VersionFlag 之后。
            Console.SetOut(new ConsoleTraceWriter());
            Console.SetError(new ConsoleTraceWriter());
#endif

            base.OnStartup(e);
        }
    }
}