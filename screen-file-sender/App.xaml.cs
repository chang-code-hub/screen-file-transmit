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
    }
}