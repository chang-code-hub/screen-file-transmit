using System;
using System.Runtime.InteropServices;

namespace screen_file_transmit
{
    /// <summary>
    /// 系统层鼠标阻断器：用低级鼠标钩子（WH_MOUSE_LL）在输入分发到任何应用之前吞掉鼠标点击。
    /// 与 WPF 的 e.Handled 不同，本类作用于系统层，能阻止 VdSession.exe 等全局监听工具
    /// 及下层应用收到选区/拾取时的点击。拖拽/移动仍放行，只吞按下/抬起/双击。
    /// 用法：using (new MouseClickBlocker()) { ... }，或 Start/Stop 配对调用。
    /// </summary>
    internal sealed class MouseClickBlocker : IDisposable
    {
        private IntPtr _hook;
        private NativeMethods.LowLevelMouseProc _proc; // 防止被 GC 回收
        private bool _disposed;

        // 只允许点击穿透到这些窗口（如选区/拾取蒙版自身），其余窗口的点击一律吞掉
        private readonly Func<NativeMethods.POINT, bool> _allowClickAt;

        /// <param name="allowClickAt">
        /// 给定鼠标物理坐标，返回 true 表示该点击允许继续分发（如落在蒙版自身上）。
        /// 传 null 表示吞掉所有点击。
        /// </param>
        public MouseClickBlocker(Func<NativeMethods.POINT, bool> allowClickAt = null)
        {
            _allowClickAt = allowClickAt;
            Start();
        }

        /// <summary>是否已启用系统层阻断</summary>
        public bool IsActive => _hook != IntPtr.Zero;

        private void Start()
        {
            if (_hook != IntPtr.Zero) return;

            _proc = HookCallback;
            _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc, IntPtr.Zero, 0);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var msg = (uint)wParam.ToInt64();
                // 只吞点击相关事件：按下/抬起/双击。移动放行以便拖拽选区。
                if (msg == NativeMethods.WM_LBUTTONDOWN || msg == NativeMethods.WM_LBUTTONUP ||
                    msg == NativeMethods.WM_LBUTTONDBLCLK ||
                    msg == NativeMethods.WM_RBUTTONDOWN || msg == NativeMethods.WM_RBUTTONUP ||
                    msg == NativeMethods.WM_MBUTTONDOWN || msg == NativeMethods.WM_MBUTTONUP)
                {
                    // 命中允许的窗口（蒙版自身）则放行，否则吞掉，防止传递到下层应用/全局监听工具
                    if (_allowClickAt != null)
                    {
                        NativeMethods.POINT pt = default;
                        var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                        pt = info.pt;
                        if (_allowClickAt(pt))
                            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
                    }

                    // 返回非零钩子值，系统不再把该点击分发给任何窗口/全局监听工具
                    return (IntPtr)1;
                }
            }

            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private void Stop()
        {
            if (_hook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }

            _proc = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
