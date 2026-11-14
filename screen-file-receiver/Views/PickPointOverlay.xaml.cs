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

        /// <param name="left">蒙版左边界（isDip=true 时为 WPF DIP，否则为物理像素）</param>
        /// <param name="top">蒙版上边界</param>
        /// <param name="width">蒙版宽度</param>
        /// <param name="height">蒙版高度</param>
        /// <param name="isDip">true 表示传入已是 WPF DIP（选区控件坐标），直接使用不换算</param>
        public PickPointOverlay(double left, double top, double width, double height, bool isDip,
            Action<int, int> onPicked, Action onCancelled)
        {
            _onPicked = onPicked;
            _onCancelled = onCancelled;
            InitializeComponent();
            Loaded += (s, e) =>
            {
                ApplyMaskRect(left, top, width, height, isDip);
                CaptureMouse();
            };
        }

        /// <summary>
        /// 把窗口定位到蒙版范围。isDip=true 时直接用传入的选区 DIP 坐标，否则物理换算。
        /// </summary>
        private void ApplyMaskRect(double left, double top, double width, double height, bool isDip)
        {
            // isDip=true：传入已是选区控件的 WPF DIP 坐标，原样使用，保证与选区同大小同坐标
            if (isDip)
            {
                Left = left;
                Top = top;
                Width = Math.Max(1, width);
                Height = Math.Max(1, height);
                return;
            }

            // 物理像素：按 WPF 设备变换换算成 DIP 后定位
            double scale = 1.0;
            try
            {
                var source = PresentationSource.FromVisual(this);
                var m = source?.CompositionTarget?.TransformToDevice ?? System.Windows.Media.Matrix.Identity;
                if (m.M11 > 0 && m.M22 > 0) scale = m.M11;
            }
            catch
            {
                //ignore
            }

            Left = left / scale;
            Top = top / scale;
            Width = Math.Max(1, width / scale);
            Height = Math.Max(1, height / scale);
        }

        /// <summary>
        /// 取光标真实物理像素坐标（与物理矩形同基准），失败回退 GetCursorPos。
        /// </summary>
        private static bool TryGetPhysicalCursorPos(out NativeMethods.POINT pt)
        {
            if (NativeMethods.GetPhysicalCursorPos(out pt))
                return true;
            return NativeMethods.GetCursorPos(out pt);
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

        private void Finish()
        {
            ReleaseMouseCapture();
            Close();
        }
    }
}
