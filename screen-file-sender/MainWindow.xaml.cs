using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;

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
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsPreviewMode))
            {
                if (viewModel.IsPreviewMode)
                { 
                    this.ResizeMode = ResizeMode.CanResize;
                    this.SizeToContent = SizeToContent.WidthAndHeight;
                }
                else
                {
                    this.ResizeMode = ResizeMode.NoResize;
                    this.SizeToContent = SizeToContent.Height;
                    this.Width = 680;
                }
                SetWindowPosition();
            } 
        }
        private void SetWindowPosition()
        {
            this.Dispatcher.InvokeAsync(() =>
            {
                UpdateLayout();
                var screen = Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
                var ps = PresentationSource.FromVisual(this);
                if (ps != null)
                {
                    var m = ps.CompositionTarget.TransformFromDevice;
                    var workLeft = m.Transform(new Vector(screen.WorkingArea.Left, 0)).X;
                    var workTop = m.Transform(new Vector(0, screen.WorkingArea.Top)).Y;
                    var workWidth = m.Transform(new Vector(screen.WorkingArea.Width, 0)).X;
                    var workHeight = m.Transform(new Vector(0, screen.WorkingArea.Height)).Y;
                    this.Left = workLeft + (workWidth - this.ActualWidth) / 2;
                    this.Top = workTop + (workHeight - this.ActualHeight) / 2;
                }
                else
                {
                    this.Left = screen.WorkingArea.Left + (screen.WorkingArea.Width - this.ActualWidth) / 2;
                    this.Top = screen.WorkingArea.Top + (screen.WorkingArea.Height - this.ActualHeight) / 2;
                }
            });
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

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (viewModel.IsConverting)
            {
                if (System.Windows.MessageBox.Show("转换正在进行中，确定要退出吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }
            else if (viewModel.IsPreviewMode)
            {
                if (System.Windows.MessageBox.Show("预览正在进行中，确定要退出吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }
            else
            {
                if (System.Windows.MessageBox.Show("确定要退出程序吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }
            viewModel.ExitPreview();
        }

        private void Grid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if(e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void ScrollViewer_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }
    }
}