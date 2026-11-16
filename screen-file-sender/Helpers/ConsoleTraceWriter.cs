using System.Diagnostics;
using System.IO;
using System.Text;

namespace screen_file_transmit
{
    /// <summary>
    /// 把 Console 输出转写到 Trace。
    /// screen-file-receiver 是 WinExe，没有控制台，Console.WriteLine 原本看不到任何输出；
    /// 调试 UI 时把它重定向到 Trace，附加调试器后即可在 VS 输出窗口观察 ImageDecoder 等处的日志。
    /// 仅在 DEBUG 构建由 App.OnStartup 启用。
    /// </summary>
    internal sealed class ConsoleTraceWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            Trace.Write(value.ToString());
        }

        public override void Write(string value)
        {
            if (!string.IsNullOrEmpty(value))
                Trace.Write(value);
        }

        public override void WriteLine(string value)
        {
            Trace.WriteLine(value ?? string.Empty);
        }
    }
}
