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
            this.StateChanged += MainWindow_StateChanged;
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
            }
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsPreviewMode))
            {
                if (viewModel.IsPreviewMode)
                {
                    this.ResizeMode = ResizeMode.CanResize;
                    this.SizeToContent = SizeToContent.WidthAndHeight;
                    this.Topmost = true;
                    this.PreviewView.Visibility = Visibility.Visible;
                    this.SettingView.Visibility = Visibility.Collapsed;
                }
                else
                {
                    this.ResizeMode = ResizeMode.NoResize;
                    this.SizeToContent = SizeToContent.Height;
                    this.Width = 680;
                    this.Topmost = false;
                    this.SettingView.Visibility = Visibility.Visible;
                    this.PreviewView.Visibility = Visibility.Collapsed;
                }
                SetWindowPosition();
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.PreviewImageSource))
            {
                if (viewModel.IsPreviewMode && viewModel.PreviewImageSource != null)
                {
                    this.Width = viewModel.PreviewImageSource.PixelWidth;
                    this.Height = viewModel.PreviewImageSource.PixelHeight;
                    SetWindowPosition();
                }
                else
                {
                    this.ResizeMode = ResizeMode.NoResize;
                    this.SizeToContent = SizeToContent.Height;
                    this.Width = 680;
                    SetWindowPosition();
                }
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
                if (System.Windows.MessageBox.Show(Properties.Resources.ResourceManager.GetString("MsgBox_ConfirmExitDuringConvert"), Properties.Resources.ResourceManager.GetString("MsgBox_Title_Confirm"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }
            else if (viewModel.IsPreviewMode)
            {
                if (System.Windows.MessageBox.Show(Properties.Resources.ResourceManager.GetString("MsgBox_ConfirmExitDuringPreview"), Properties.Resources.ResourceManager.GetString("MsgBox_Title_Confirm"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }
            else
            {
                if (System.Windows.MessageBox.Show(Properties.Resources.ResourceManager.GetString("MsgBox_ConfirmExit"), Properties.Resources.ResourceManager.GetString("MsgBox_Title_Confirm"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
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
         

        private void ScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();

        }
    }
}