using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace screen_file_receiver
{
    public partial class WindowSelectOverlay : Window
    {
        private IntPtr _hoverHwnd;
        private IntPtr _selfHwnd;

        public IntPtr SelectedHwnd { get; private set; }

        public WindowSelectOverlay()
        {
            InitializeComponent();
            Loaded += WindowSelectOverlay_Loaded;
        }

        private void WindowSelectOverlay_Loaded(object sender, RoutedEventArgs e)
        {
            _selfHwnd = new WindowInteropHelper(this).Handle;
            CaptureMouse();
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (!NativeMethods.GetCursorPos(out var pt))
            {
                HighlightBorder.Visibility = Visibility.Collapsed;
                _hoverHwnd = IntPtr.Zero;
                return;
            }

            IntPtr hwnd = NativeMethods.WindowFromPoint(pt);

            if (hwnd == IntPtr.Zero || hwnd == _selfHwnd || !NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd))
            {
                HighlightBorder.Visibility = Visibility.Collapsed;
                _hoverHwnd = IntPtr.Zero;
                return;
            }

            NativeMethods.GetWindowRect(hwnd, out var rc);
            // 优先使用 DWM 扩展帧边界（不含阴影），失败则回退 GetWindowRect
            if (NativeMethods.TryGetExtendedFrameBounds(hwnd, out var dwmRc))
            {
                rc = dwmRc;
            }

            int width = rc.Right - rc.Left;
            int height = rc.Bottom - rc.Top;
            if (width <= 0 || height <= 0)
            {
                HighlightBorder.Visibility = Visibility.Collapsed;
                _hoverHwnd = IntPtr.Zero;
                return;
            }

            // 子窗口/控件过滤：高或宽小于 40 不选
            IntPtr rootHwnd = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            if (rootHwnd != hwnd && (width < 40 || height < 40))
            {
                HighlightBorder.Visibility = Visibility.Collapsed;
                _hoverHwnd = IntPtr.Zero;
                return;
            }

            const int borderThickness = 2;
            int left = rc.Left - borderThickness;
            int top = rc.Top - borderThickness;
            int right = rc.Right + borderThickness;
            int bottom = rc.Bottom + borderThickness;

            var topLeft = ScreenCaptureHelper.PhysicalToLogical(this, new Point(left, top));
            var bottomRight = ScreenCaptureHelper.PhysicalToLogical(this, new Point(right, bottom));

            HighlightBorder.Width = Math.Max(0, bottomRight.X - topLeft.X);
            HighlightBorder.Height = Math.Max(0, bottomRight.Y - topLeft.Y);
            Canvas.SetLeft(HighlightBorder, topLeft.X);
            Canvas.SetTop(HighlightBorder, topLeft.Y);
            HighlightBorder.Visibility = Visibility.Visible;

            _hoverHwnd = hwnd;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            SelectedHwnd = _hoverHwnd;
            ReleaseMouseCapture();
            Close();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                SelectedHwnd = IntPtr.Zero;
                ReleaseMouseCapture();
                Close();
                e.Handled = true;
            }
        }
    }
}
