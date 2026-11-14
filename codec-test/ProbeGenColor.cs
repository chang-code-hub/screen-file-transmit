using screen_file_transmit;
using System;
using System.Drawing.Imaging;
using System.IO;

namespace qr_codec_test
{
    /// <summary>
    /// 生成彩色模式测试页，用于核对 VerifyDecodeResult 在 RGB 模式下的表现
    /// </summary>
    internal class ProbeGenColor
    {
        public static void Run(string input, string outDir, bool colorful, int depth, int rs)
        {
            Directory.CreateDirectory(outDir);
            const int ScreenWidth = 1920 * 2;
            const int ScreenHeight = 1080 * 2;
            const int Scale = 3;

            var matrix = ImageEncoder.CalculateScreenDataMatrix(ScreenWidth, ScreenHeight, Scale, rs);
            var pageInfo = ImageEncoder.CalculatePageInfo(matrix, Scale);
            var ts = ImageEncoder.GenerateFileId();

            using (var fs = new FileStream(input, FileMode.Open, FileAccess.Read))
            {
                int totalPages = (int)Math.Ceiling((double)fs.Length / matrix.PageByteCount / (colorful ? 3 : 1));
                if (totalPages == 0) totalPages = 1;
                int pageNumber = 0;
                while (fs.Position < fs.Length)
                {
                    pageNumber++;
                    var bitmap = ImageEncoder.GenerateDataMatrixBitmap(fs, matrix, pageInfo, depth, colorful, Scale,
                        Path.GetFileName(input), true, pageNumber, totalPages, ts, false, rs);
                    if (bitmap == null) break;
                    bitmap.Save(Path.Combine(outDir, $"c_{(colorful ? "color" : "gray")}_p{pageNumber:D4}.png"), ImageFormat.Png);
                    bitmap.Dispose();
                    if (pageNumber >= 3) break;
                }
                Console.WriteLine($"generated colorful={colorful} depth={depth} rs={rs} -> {outDir}");
            }
        }
    }
}
