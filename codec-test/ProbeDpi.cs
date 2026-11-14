using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace qr_codec_test
{
    /// <summary>
    /// 实测各坐标 API 在当前进程的返回值，确定基准关系：
    /// GetWindowRect/GetCursorPos vs GetExtendedFrameBounds/GetPhysicalCursorPos vs WPF DIP/TransformToDevice。
    /// 用法：--dpi
    /// </summary>
    internal class ProbeDpi
    {
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern bool GetPhysicalCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern uint GetDpiForSystem();
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, uint a, out RECT r, int s);

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

        [STAThread]
        public static void Run()
        {
            var win = new Window { Width = 300, Height = 200, Left = 100, Top = 100, Title = "dpi-probe" };
            win.Show();
            var hwnd = new WindowInteropHelper(win).Handle;

            GetWindowRect(hwnd, out var wr);
            DwmGetWindowAttribute(hwnd, 9, out var fb, Marshal.SizeOf(typeof(RECT)));
            var m = PresentationSource.FromVisual(win)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;

            Console.WriteLine("=== DPI probe ===");
            Console.WriteLine($"process DPI awareness: (0=unaware)");
            Console.WriteLine($"GetDpiForSystem     = {GetDpiForSystem()}  scale={GetDpiForSystem()/96.0}");
            Console.WriteLine($"GetDpiForWindow     = {GetDpiForWindow(hwnd)}  scale={GetDpiForWindow(hwnd)/96.0}");
            Console.WriteLine($"WPF TransformToDevice M11={m.M11} M22={m.M22}");
            Console.WriteLine($"Window Left/Top(DIP) = {win.Left},{win.Top}  W/H={win.Width}x{win.Height}");
            Console.WriteLine($"GetWindowRect(虚拟)  = ({wr.L},{wr.T})-({wr.R},{wr.B})  size={wr.R-wr.L}x{wr.B-wr.T}");
            Console.WriteLine($"ExtendedFrame(物理)  = ({fb.L},{fb.T})-({fb.R},{fb.B})  size={fb.R-fb.L}x{fb.B-fb.T}");
            Console.WriteLine($"WR vs FB 左上比       = {(fb.L != 0 ? (double)wr.L / fb.L : double.NaN):F4}  (1.0=同基准, 1/scale=虚拟化)");

            if (GetCursorPos(out var cp) && GetPhysicalCursorPos(out var pp))
            {
                Console.WriteLine($"GetCursorPos(虚拟)   = ({cp.X},{cp.Y})");
                Console.WriteLine($"GetPhysicalCursorPos = ({pp.X},{pp.Y})");
                Console.WriteLine($"cursor 虚拟/物理 比  = {(pp.X != 0 ? (double)cp.X / pp.X : double.NaN):F4}");
            }

            Console.WriteLine($"DIP Left*{m.M11:F2} = {win.Left * m.M11:F1}  vs GetWindowRect.L={wr.L}  vs FB.L={fb.L}");
            win.Close();
        }
    }
}
