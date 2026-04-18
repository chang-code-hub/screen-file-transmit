using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace screen_file_transmit
{
    public partial class RegionSelectOverlay : Window
    {
        private Point _startPoint;
        private bool _isDragging;
        private IntPtr _keyboardHook;
        private NativeMethods.LowLevelKeyboardProc _keyboardProc;

        public Rect SelectedRegion { get; private set; }

        public RegionSelectOverlay()
        {
            InitializeComponent();
            Loaded += RegionSelectOverlay_Loaded;
            Closed += RegionSelectOverlay_Closed;
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
        private void RegionSelectOverlay_Loaded(object sender, RoutedEventArgs e)
        {
            _keyboardProc = KeyboardHookCallback;
            _keyboardHook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_KEYBOARD_LL,
                _keyboardProc,
                IntPtr.Zero,
                0);
        }

        private void RegionSelectOverlay_Closed(object sender, EventArgs e)
        {
            if (_keyboardHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = IntPtr.Zero;
            }
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0
                && (wParam == (IntPtr)NativeMethods.WM_KEYDOWN || wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN))
            {
                var kbd = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                if (kbd.vkCode == NativeMethods.VK_ESCAPE)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (IsVisible)
                        {
                            _isDragging = false;
                            ReleaseMouseCapture();
                            SelectedRegion = Rect.Empty;
                            Close();
                        }
                    }));
                }
            }
            return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _startPoint = e.GetPosition(this);
            SelectionRect.Visibility = Visibility.Visible;
            SelectionRect.Width = 0;
            SelectionRect.Height = 0;
            Canvas.SetLeft(SelectionRect, _startPoint.X);
            Canvas.SetTop(SelectionRect, _startPoint.Y);
            CaptureMouse();
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging)
                return;

            var current = e.GetPosition(this);
            double x = Math.Min(_startPoint.X, current.X);
            double y = Math.Min(_startPoint.Y, current.Y);
            double w = Math.Abs(current.X - _startPoint.X);
            double h = Math.Abs(current.Y - _startPoint.Y);

            Canvas.SetLeft(SelectionRect, x);
            Canvas.SetTop(SelectionRect, y);
            SelectionRect.Width = w;
            SelectionRect.Height = h;
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging)
                return;

            _isDragging = false;
            ReleaseMouseCapture();

            var current = e.GetPosition(this);
            double x = Math.Min(_startPoint.X, current.X);
            double y = Math.Min(_startPoint.Y, current.Y);
            double w = Math.Abs(current.X - _startPoint.X);
            double h = Math.Abs(current.Y - _startPoint.Y);

            SelectedRegion = new Rect(Left + x, Top + y, w, h);
            Close();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                _isDragging = false;
                ReleaseMouseCapture();
                SelectedRegion = Rect.Empty;
                Close();
                e.Handled = true;
            }
        }
    }
}
