using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace screen_file_transmit
{
    /// <summary>
    /// 拾取点击位置时的全屏半透明蒙版。
    /// 用来挡住下方所有界面元素，避免拾取用的那一次点击传递到被截取的应用；
    /// 只把这一次点击作为拾取结果回调给调用方。
    /// </summary>
    public partial class PickPointOverlay : Window
    {
        private readonly Action<int, int> _onPicked;
        private readonly Action _onCancelled;

        public PickPointOverlay(Action<int, int> onPicked, Action onCancelled)
        {
            _onPicked = onPicked;
            _onCancelled = onCancelled;
            InitializeComponent();
            Loaded += (s, e) => CaptureMouse();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // 与其它遮罩窗口一致：加 WS_EX_TOOLWINDOW，避免出现在任务栏/Alt-Tab
            var helper = new WindowInteropHelper(this);
            IntPtr hWnd = helper.Handle;
            int exStyle = (int)NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE, (IntPtr)(exStyle | NativeMethods.WS_EX_TOOLWINDOW));
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 这一次点击即为拾取点：取光标物理坐标并回调，随后关闭蒙版。
            // 事件在蒙版窗口内被消费，不会冒泡/传递到下方界面。
            e.Handled = true;

            int x = 0, y = 0;
            if (NativeMethods.GetCursorPos(out var pt))
            {
                x = pt.X;
                y = pt.Y;
            }

            Finish();
            _onPicked?.Invoke(x, y);
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            // 仅供捕获期间跟随；蒙版本身不改变外观
            e.Handled = true;
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Finish();
                _onCancelled?.Invoke();
            }
        }

        private void Finish()
        {
            ReleaseMouseCapture();
            Close();
        }
    }
}
