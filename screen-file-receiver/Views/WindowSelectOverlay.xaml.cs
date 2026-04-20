using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace screen_file_transmit
{
    public partial class WindowSelectOverlay : Window
    {
        private IntPtr _hoverHwnd;
        private IntPtr _selfHwnd;
        private List<WindowInfo> _windowList;
        private IntPtr _winEventHook;
        private NativeMethods.WinEventDelegate _winEventDelegate;
        private DateTime _lastRebuild;
        private readonly TimeSpan _rebuildThrottle = TimeSpan.FromMilliseconds(200);
        private uint _currentPid;
        private IntPtr _keyboardHook;
        private NativeMethods.LowLevelKeyboardProc _keyboardProc;
        private SelectionBorderWindow _borderWindow;

        public IntPtr SelectedHwnd { get; private set; }

        private class WindowInfo
        {
            public IntPtr Hwnd;
            public NativeMethods.RECT Rect;
            public int ZRank;
            public int Level;
            public string Title;
            public string ClassName;

            public uint Pid { get; internal set; }
        }

        public WindowSelectOverlay()
        {
            InitializeComponent();
            Loaded += WindowSelectOverlay_Loaded;
            Closed += WindowSelectOverlay_Closed;
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
        private void WindowSelectOverlay_Loaded(object sender, RoutedEventArgs e)
        {
            _selfHwnd = new WindowInteropHelper(this).Handle;
            _currentPid = (uint)Process.GetCurrentProcess().Id;
            BuildWindowList();
            CaptureMouse();

            _borderWindow = new SelectionBorderWindow();
            _borderWindow.Show();

            _keyboardProc = KeyboardHookCallback;
            _keyboardHook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_KEYBOARD_LL,
                _keyboardProc,
                IntPtr.Zero,
                0);

            _winEventDelegate = OnWinEvent;
            _winEventHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_OBJECT_CREATE,
                NativeMethods.EVENT_OBJECT_REORDER,
                IntPtr.Zero,
                _winEventDelegate,
                0, 0,
                NativeMethods.WINEVENT_OUTOFCONTEXT);
        }

        private void WindowSelectOverlay_Closed(object sender, EventArgs e)
        {
            if (_borderWindow != null)
            {
                _borderWindow.Close();
                _borderWindow = null;
            }

            if (_keyboardHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = IntPtr.Zero;
            }

            if (_winEventHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_winEventHook);
                _winEventHook = IntPtr.Zero;
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
                            SelectedHwnd = IntPtr.Zero;
                            ReleaseMouseCapture();
                            Close();
                        }
                    }));
                }
            }
            return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (hwnd == _selfHwnd || idObject != 0)
                return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (DateTime.Now - _lastRebuild < _rebuildThrottle)
                    return;

                _lastRebuild = DateTime.Now;
                BuildWindowList();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void BuildWindowList()
        {
            _windowList = new List<WindowInfo>();

            // 按 z-order 从高到低枚举顶层可见窗口
            var desktopHwnd = NativeMethods.GetDesktopWindow();
            var hwnd = NativeMethods.GetWindow(desktopHwnd, NativeMethods.GW_CHILD);
             
            int zRank = 0;
            while (hwnd != IntPtr.Zero)
            {
                if (!IsCurrentProcessWindow(hwnd)
                    && NativeMethods.IsWindow(hwnd)
                    && NativeMethods.IsWindowVisible(hwnd)
                    && !IsTransparentWindow(hwnd)
                    && IsAltTabVisibleWindow(hwnd))
                {
                    // 先递归收集该顶层窗口的子窗口和控件（子窗口在上层，排在父窗口前面）
                    EnumerateChildren(hwnd, _windowList, zRank, 1);

                    NativeMethods.RECT rc;
                    if (NativeMethods.TryGetExtendedFrameBounds(hwnd, out var dwmRc))
                        rc = dwmRc;
                    else
                        NativeMethods.GetWindowRect(hwnd, out rc);

                    int width = rc.Right - rc.Left;
                    int height = rc.Bottom - rc.Top;
                    if (width >= 100 && height >= 100)
                    {
                        var info = CreateWindowInfo(hwnd, rc);
                        info.ZRank = zRank;
                        info.Level = 0;
                        _windowList.Add(info);
                    }
                }

                zRank++;
                hwnd = NativeMethods.GetWindow(hwnd, NativeMethods.GW_HWNDNEXT);
            }

            // 窗口列表重建后，强制下次 MouseMove 重新计算高亮位置
            if (!_windowList.Any(c => c.Hwnd == _hoverHwnd))
            {
                _hoverHwnd = IntPtr.Zero;
            }

            WriteWindowLog();
            Trace.WriteLine($"window count {_windowList.Count}");
        }

        /// <summary>
        /// 递归收集 parentHwnd 的所有后代窗口，按 z-order 从高到低加入 list。
        /// 兄弟窗口之间：z-order 高的先加入；父子之间：子窗口先加入（子在上层）。
        /// </summary>
        private void EnumerateChildren(IntPtr parentHwnd, List<WindowInfo> list, int zRank, int level)
        {
            var child = NativeMethods.GetWindow(parentHwnd, NativeMethods.GW_CHILD);
            while (child != IntPtr.Zero)
            {
                if (!IsCurrentProcessWindow(child)
                    && NativeMethods.IsWindow(child)
                    && NativeMethods.IsWindowVisible(child)
                    && !IsTransparentWindow(child))
                {
                    // 递归收集孙窗口，确保更深的、z-order 更高的子窗口排在前面
                    EnumerateChildren(child, list, zRank, level + 1);

                    NativeMethods.RECT rc;
                    if (NativeMethods.TryGetExtendedFrameBounds(child, out var dwmRc))
                        rc = dwmRc;
                    else
                        NativeMethods.GetWindowRect(child, out rc);

                    int width = rc.Right - rc.Left;
                    int height = rc.Bottom - rc.Top;
                    if (width >= 100 && height >= 100)
                    {
                        var info = CreateWindowInfo(child, rc);
                        info.ZRank = zRank;
                        info.Level = level;
                        list.Add(info);
                    }
                }

                child = NativeMethods.GetWindow(child, NativeMethods.GW_HWNDNEXT);
            }
        }

        private static WindowInfo CreateWindowInfo(IntPtr hwnd, NativeMethods.RECT rc)
        {
            var sbTitle = new StringBuilder(256);
            var sbClass = new StringBuilder(256);
            NativeMethods.GetWindowText(hwnd, sbTitle, sbTitle.Capacity);
            NativeMethods.GetClassName(hwnd, sbClass, sbClass.Capacity);

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            return new WindowInfo
            {
                Pid = pid,
                Hwnd = hwnd,
                Rect = rc,
                ZRank = 0,
                Level = 0,
                Title = sbTitle.ToString(),
                ClassName = sbClass.ToString()
            };
        }

        private void WriteWindowLog()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"WindowList built at {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}, count={_windowList.Count}");
                sb.AppendLine(new string('-', 80));

                foreach (var win in _windowList)
                {
                    string indent = new string(' ', win.Level * 4);
                    string kind = win.Level == 0 ? "Top" : "Child";
                    string title = string.IsNullOrEmpty(win.Title) ? "(no title)" : win.Title;
                    if (title.Length > 40)
                        title = title.Substring(0, 37) + "...";

                    sb.AppendLine($"{indent}[Z={win.ZRank:D3} {kind}] HWnd={win.Hwnd:X8} Pid={win.Pid} | {win.Rect.Right - win.Rect.Left}x{win.Rect.Bottom - win.Rect.Top} @ ({win.Rect.Left},{win.Rect.Top}) | Class={win.ClassName} | Title={title}");
                }

                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "window.log");
                File.WriteAllText(logPath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"WriteWindowLog failed: {ex.Message}");
            }
        }

        private bool IsCurrentProcessWindow(IntPtr hwnd)
        {
            if (hwnd == _selfHwnd)
                return true;

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            return pid == _currentPid;
        }

        private static bool IsAltTabVisibleWindow(IntPtr hwnd)
        {
            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);

            // WS_EX_TOOLWINDOW 的窗口通常不在 Alt-Tab/Win+Tab 中显示，
            // 除非同时具有 WS_EX_APPWINDOW
            if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0
                && (exStyle & NativeMethods.WS_EX_APPWINDOW) == 0)
                return false;

            // 具有 Owner 的窗口通常也不在 Alt-Tab/Win+Tab 中显示
            if (NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) != IntPtr.Zero)
                return false;

            return true;
        }

        private static bool IsTransparentWindow(IntPtr hwnd)
        {
            // 检查 DWM cloaked (Windows 8+)
            if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0)
            {
                if (cloaked != 0)
                    return true;
            }

            // 检查 layered window 的透明度
            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            if ((exStyle & NativeMethods.WS_EX_LAYERED) != 0)
            {
                if (NativeMethods.GetLayeredWindowAttributes(hwnd, out uint _, out byte alpha, out uint flags))
                {
                    if ((flags & NativeMethods.LWA_ALPHA) != 0 && alpha < 10)
                        return true;
                }
            }

            return false;
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if(_borderWindow==null) return;
            if (!NativeMethods.GetCursorPos(out var pt))
            {
                HideBorderWindow();
                _hoverHwnd = IntPtr.Zero;
                return;
            }

            // 在预计算的窗口列表中，按 z-order 从高到低查找包含鼠标坐标的窗口
            IntPtr matchedHwnd = IntPtr.Zero;
            NativeMethods.RECT matchedRc = new NativeMethods.RECT();

            foreach (var win in _windowList)
            {
                if (pt.X >= win.Rect.Left && pt.X < win.Rect.Right
                    && pt.Y >= win.Rect.Top && pt.Y < win.Rect.Bottom)
                {
                    matchedHwnd = win.Hwnd;
                    matchedRc = win.Rect;
                    break;
                }
            }

            if (matchedHwnd == IntPtr.Zero)
            {
                HideBorderWindow();
                _hoverHwnd = IntPtr.Zero;
                return;
            }

            // 同一窗口内移动直接跳过
            if (matchedHwnd == _hoverHwnd)
                return;

            const int borderThickness = 2;
            int left = matchedRc.Left - borderThickness;
            int top = matchedRc.Top - borderThickness;
            int width = matchedRc.Right - matchedRc.Left + borderThickness * 2;
            int height = matchedRc.Bottom - matchedRc.Top + borderThickness * 2;

            // 找到被选中窗口的顶层祖先，将其前一个窗口（更高层）作为插入点，
            // 使红框刚好位于被选中窗口之上、更高层窗口之下
            var rootHwnd = NativeMethods.GetAncestor(matchedHwnd, NativeMethods.GA_ROOT);
            var prevHwnd = NativeMethods.GetWindow(rootHwnd, NativeMethods.GW_HWNDPREV);
            var insertAfter = prevHwnd != IntPtr.Zero ? prevHwnd : NativeMethods.HWND_TOP;

            var borderHwnd = new WindowInteropHelper(_borderWindow).Handle;
            NativeMethods.SetWindowPos(borderHwnd, insertAfter, left, top, width, height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

            _hoverHwnd = matchedHwnd;
        }

        private void HideBorderWindow()
        {
            if (_borderWindow != null)
            {
                var borderHwnd = new WindowInteropHelper(_borderWindow).Handle;
                NativeMethods.SetWindowPos(borderHwnd, IntPtr.Zero, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_HIDEWINDOW);
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            SelectedHwnd = _hoverHwnd;
            Trace.WriteLine($"SELECT WINDOW {SelectedHwnd}");
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
