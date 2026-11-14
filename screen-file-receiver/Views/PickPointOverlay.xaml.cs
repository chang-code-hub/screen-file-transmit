using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace screen_file_transmit
{
    /// <summary>
    /// 拾取点击位置时的蒙版窗口。
    /// 只在选区/目标窗口范围内覆盖一层蒙版并高亮，避免拾取的这次点击传递到该区域下方的界面；
    /// 其余屏幕区域不遮挡。只把这一次点击作为拾取结果回调给调用方。
    /// </summary>
    public partial class PickPointOverlay : Window
    {
        private readonly Action<int, int> _onPicked;
        private readonly Action _onCancelled;

        /// <param name="leftPhys">蒙版左边界（真实物理像素屏幕坐标）</param>
        /// <param name="topPhys">蒙版上边界</param>
        /// <param name="widthPhys">蒙版宽度</param>
        /// <param name="heightPhys">蒙版高度</param>
        public PickPointOverlay(double leftPhys, double topPhys, double widthPhys, double heightPhys,
            Action<int, int> onPicked, Action onCancelled)
        {
            _onPicked = onPicked;
            _onCancelled = onCancelled;
            InitializeComponent();
            Loaded += (s, e) =>
            {
                ApplyMaskRect(leftPhys, topPhys, widthPhys, heightPhys);
                CaptureMouse();
            };
        }

        /// <summary>
        /// 把窗口定位到蒙版范围。
        /// 直接用 SetWindowPos 按真实物理像素摆放 Win32 窗口，绕开 WPF 的 DIP 换算，
        /// 这样在 Windows 显示缩放下不会因 DIP/物理坐标基准不一致而偏移。
        /// </summary>
        private void ApplyMaskRect(double leftPhys, double topPhys, double widthPhys, double heightPhys)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            int x = (int)Math.Round(leftPhys);
            int y = (int)Math.Round(topPhys);
            int w = Math.Max(1, (int)Math.Round(widthPhys));
            int h = Math.Max(1, (int)Math.Round(heightPhys));

            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
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
            // 这一次点击即为拾取点：取光标「真实物理」坐标并回调，随后关闭蒙版。
            // 事件在蒙版窗口内被消费，不会冒泡/传递到下方界面。
            e.Handled = true;

            int x = 0, y = 0;
            if (TryGetPhysicalCursorPos(out var pt))
            {
                x = pt.X;
                y = pt.Y;
            }

            Finish();
            _onPicked?.Invoke(x, y);
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

        /// <summary>
        /// 取光标真实物理像素坐标。优先 GetPhysicalCursorPos（不受进程 DPI 虚拟化影响），
        /// 失败则回退 GetCursorPos。与选区/窗口的物理矩形保持同一坐标系，避免缩放下错位。
        /// </summary>
        private static bool TryGetPhysicalCursorPos(out NativeMethods.POINT pt)
        {
            if (NativeMethods.GetPhysicalCursorPos(out pt))
                return true;
            return NativeMethods.GetCursorPos(out pt);
        }

        private void Finish()
        {
            ReleaseMouseCapture();
            Close();
        }
    }
}
