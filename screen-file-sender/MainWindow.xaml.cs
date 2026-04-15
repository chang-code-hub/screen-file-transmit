using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;

namespace screen_file_transmit
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainWindowViewModel viewModel = new MainWindowViewModel();

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = viewModel;
            this.Loaded += MainWindow_Loaded;
            this.LocationChanged += MainWindow_LocationChanged;
        }

        private void MainWindow_LocationChanged(object sender, EventArgs e)
        {
            GetScreenResolution();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            GetScreenResolution();
        }

        private void GetScreenResolution()
        {
            // 获取窗口所在的屏幕
            var screen = Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            viewModel.ScreenSize = screen.Bounds;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            viewModel.Password = PasswordBox.Password;
        }
    }
}