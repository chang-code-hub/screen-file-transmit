using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;

namespace screen_file_transmit
{
    public partial class App : Application
    {
        public App()
        {
            Thread.CurrentThread.CurrentUICulture = CultureInfo.CurrentUICulture;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // Handle `-v` / `--version` before the main window is shown or
            // native DLLs are extracted.
            if (VersionFlag.Handle(e.Args))
                return; // VersionFlag exits the process itself

#if DEBUG
            // 调试 UI 时：WinExe 没有控制台，把 Console 输出重定向到 Trace，
            // 附加调试器后可在 VS 输出窗口看到 ImageDecoder 等处的 Console.WriteLine。
            // 必须放在 VersionFlag 之后，否则 `-v` 的版本号输出会被 Trace 吞掉。
            Console.SetOut(new ConsoleTraceWriter());
            Console.SetError(new ConsoleTraceWriter());
#endif

            base.OnStartup(e);
            ExtractUnmanagedDlls();
        }

        private static void ExtractUnmanagedDlls()
        {
            var dllNames = new[] { "OpenCvSharpExtern.dll" };
            var assembly = Assembly.GetExecutingAssembly();
            var targetDir = AppDomain.CurrentDomain.BaseDirectory;

            foreach (var dllName in dllNames)
            {
                var targetPath = Path.Combine(targetDir, dllName);
                if (File.Exists(targetPath))
                    continue;

                var baseName = Path.GetFileNameWithoutExtension(dllName);
                var resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(r => r.IndexOf(baseName, StringComparison.OrdinalIgnoreCase) >= 0);

                if (resourceName == null)
                    continue;

                using var resourceStream = assembly.GetManifestResourceStream(resourceName);
                if (resourceStream == null)
                    continue;

                byte[] bytes;
                if (resourceName.EndsWith(".compressed", StringComparison.OrdinalIgnoreCase) ||
                    resourceName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using var decompressed = new MemoryStream();
                    using (var deflate = new DeflateStream(resourceStream, CompressionMode.Decompress))
                    {
                        deflate.CopyTo(decompressed);
                    }
                    bytes = decompressed.ToArray();
                }
                else
                {
                    using var ms = new MemoryStream();
                    resourceStream.CopyTo(ms);
                    bytes = ms.ToArray();
                }

                File.WriteAllBytes(targetPath, bytes);
            }
        }
    }
}
