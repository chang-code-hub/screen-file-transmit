using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Windows;

namespace screen_file_receiver
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ExtractUnmanagedDlls();
        }

        private static void ExtractUnmanagedDlls()
        {
            var dllNames = new[] { "OpenCvSharpExtern.dll", "opencv_videoio_ffmpeg4130_64.dll" };
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
