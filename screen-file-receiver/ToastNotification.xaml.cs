using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace screen_file_transmit
{
    public partial class ToastNotification : Window
    {
        private readonly DispatcherTimer _timer;
        private readonly TimeSpan _duration = TimeSpan.FromSeconds(10);
        private DateTime _startTime;
        private static ToastNotification _current;

        public ToastNotification(string title, string message, MessageBoxImage image)
        {
            InitializeComponent();

            Title = title;
            TitleText.Text = title;
            MessageText.Text = message;

            var brush = GetBrush(image);
            IconEllipse.Fill = brush;
            CountdownIndicator.Fill = brush;

            _timer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _timer.Tick += Timer_Tick;
        }

 
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // 获取当前窗口的句柄和扩展样式
            var helper = new WindowInteropHelper(this);
            IntPtr hWnd = helper.Handle;
            int exStyle = (int)NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);

            // 添加 WS_EX_TOOLWINDOW 样式
            NativeMethods.SetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE, (IntPtr)(exStyle | NativeMethods.WS_EX_TOOLWINDOW));
        }
        private static Brush GetBrush(MessageBoxImage image)
        {
            switch (image)
            {
                case MessageBoxImage.Information:
                    return new SolidColorBrush(Color.FromRgb(24, 144, 255)); // #1890FF
                case MessageBoxImage.Warning:
                    return new SolidColorBrush(Color.FromRgb(250, 173, 20)); // #FAAD14
                case MessageBoxImage.Error:
                    return new SolidColorBrush(Color.FromRgb(255, 77, 79)); // #FF4D4F
                case MessageBoxImage.Question:
                    return new SolidColorBrush(Color.FromRgb(19, 194, 194)); // #13C2C2
                default:
                    return new SolidColorBrush(Color.FromRgb(82, 196, 26)); // #52C41A
            }
        }

        public static void Show(string message)
        {
            Show(message, "提示", MessageBoxImage.None);
        }

        public static void Show(string message, string title)
        {
            Show(message, title, MessageBoxImage.None);
        }

        public static void Show(string message, string title, MessageBoxImage image)
        {
            _current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try { _current.Close(); } catch { }
            }));

            _current = new ToastNotification(title, message, image);
            _current.Closed += (s, e) => _current = null;
            _current.Show();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            PositionWindow();
            _startTime = DateTime.Now;
            _timer.Start();
        }

        private void PositionWindow()
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - ActualWidth - 20;
            Top = workArea.Bottom - ActualHeight - 20;
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            var elapsed = DateTime.Now - _startTime;
            var remaining = _duration - elapsed;

            if (remaining <= TimeSpan.Zero)
            {
                Close();
                return;
            }

            double percent = remaining.TotalMilliseconds / _duration.TotalMilliseconds;
            CountdownIndicator.Width = ActualWidth * percent;
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            // 异步关闭，避免打断当前正在处理的输入消息（如按钮点击）
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try { Close(); }
                catch { }
            }), DispatcherPriority.Background);
        }

        private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer?.Stop();
            base.OnClosed(e);
        }
    }
}
