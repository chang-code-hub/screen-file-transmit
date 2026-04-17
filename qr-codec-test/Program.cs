using OpenCvSharp;
using screen_file_receiver;
using screen_file_transmit;
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using ZXing;
using ZXing.Common;

namespace qr_codec_test
{
    internal class Program
    {
        // 屏幕配置参数
        private const int ScreenWidth = 1920 * 2;//- 79;

        private const int ScreenHeight = 1080 * 2;//- 8;
        private const int Scale = 3;
        private const int ColorDepth = 1;
        private const bool Colorful = false;
        private const int NoiseStdDev = 0;   // 高斯噪点标准差，值越大噪点越重
        private const int JpegQuality = 10;  // JPEG 压缩质量，值越小块效应/伪影越重
        private const int ErrorCorrectionPercent = 30;

        private static void Main(string[] args)
        {
            TestDecodeDmPng();
            TestEncodeAndDecode();
            TestNoisyDecode();
            // 运行条码区域检测测试
            //TestBarcodeDetection();
        }

        private static void TestDecodeDmPng()
        {
            Console.WriteLine("=== 解码 data\\dm.png 测试 ===\n");

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string projectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
            string imageFile = Path.Combine(projectRoot, "data", "dm.png");

            if (!File.Exists(imageFile))
            {
                Console.WriteLine($"错误：找不到文件 {imageFile}");
                return;
            }

            Console.WriteLine($"图片: {imageFile}");


            // 2. 直接用 ZXing DataMatrixReader 解码
            using (Mat image = Cv2.ImRead(imageFile, ImreadModes.Color))
            {
                if (image.Empty())
                {
                    Console.WriteLine("错误：OpenCV 加载图片失败");
                    return;
                }

                Console.WriteLine($"图片尺寸: {image.Width}x{image.Height}");

                var reader = new ZXing.Datamatrix.DataMatrixReader();
                var hints = new Dictionary<DecodeHintType, object>
                {
                    { DecodeHintType.CHARACTER_SET, "ISO-8859-1" },
                    { DecodeHintType.TRY_HARDER, true }
                };

                using (var bitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(image))
                {
                    var source = new BitmapLuminanceSource(bitmap);
                    var binarizer = new HybridBinarizer(source);
                    var binaryBitmap = new BinaryBitmap(binarizer);
                    var result = reader.decode(binaryBitmap, hints);

                    if (result != null && result.BarcodeFormat == BarcodeFormat.DATA_MATRIX)
                    {
                        Console.WriteLine($"\nDataMatrix 解码成功！");
                        Console.WriteLine($"文本长度: {result.Text.Length}");
                        Console.WriteLine($"文本: {result.Text}");

                        byte[] bytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(result.Text);
                        Console.WriteLine($"字节: {BitConverter.ToString(bytes.Take(Math.Min(bytes.Length, 64)).ToArray())}");
                        if (bytes.Length > 64)
                            Console.WriteLine($"  ... (还有 {bytes.Length - 64} 字节)");
                    }
                    else
                    {
                        Console.WriteLine("\n错误：无法直接解码 DataMatrix。");

                        // 尝试用 ImageReader 的完整解码流程
                        Console.WriteLine("尝试使用 ImageReader 完整解码流程...");
                        try
                        {
                            using (var ms = new MemoryStream())
                            {
                                bool ok = ImageDecoder.ReadToFile(imageFile, ms, true);
                                if (ok)
                                {
                                    Console.WriteLine($"ImageReader 成功！解码了 {ms.Length} 字节");
                                }
                                else
                                {
                                    Console.WriteLine("ImageReader 返回 false");
                                }
                            }
                        }
                        catch (Exception ex2)
                        {
                            Console.WriteLine($"ImageReader 失败: {ex2.Message}");
                        }
                    }
                }
            }

            Console.WriteLine("\n测试完成。");
        }

        private static void TestBarcodeDetection()
        {
            Console.WriteLine("=== 条码检测测试 ===\n");

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string projectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
            string dataDir = Path.Combine(projectRoot, "data");

            for (int i = 1; i <= 1; i++)
            {
                string imageFile = Path.Combine(dataDir, $"page_{i:D6}.png");
                if (!File.Exists(imageFile))
                {
                    Console.WriteLine($"跳过：找不到文件 {imageFile}");
                    continue;
                }

                Console.WriteLine($"处理中: {Path.GetFileName(imageFile)}");
                var meta = ImageDecoder.ReadMetadata(imageFile);
                Console.WriteLine($"  元数据:       {(meta.Metadata != null ? BitConverter.ToString(meta.Metadata) : "null")}");
                Console.WriteLine($"  最大行数:     {meta.MaxRows}");
                Console.WriteLine($"  最大列数:     {meta.MaxCols}");
                Console.WriteLine($"  彩色模式:     {meta.Colorful}");
                Console.WriteLine($"  色深度:       {meta.ColorDepth}");
                Console.WriteLine($"  当前页:       {meta.CurrentPage}");
                Console.WriteLine($"  总页数:       {meta.TotalPages}");
                Console.WriteLine($"  文件名:       {meta.FileName}");
                Console.WriteLine($"  文件ID:       {meta.FileId}");
            }

            Console.WriteLine("\n检测完成。按任意键退出...");
            Cv2.WaitKey();
            Cv2.DestroyAllWindows();
        }

        private static void TestEncodeAndDecode()
        {
            Console.WriteLine("=== 二维码文件编解码测试 ===\n");

            // 路径配置
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string projectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
            string inputFile = Path.Combine(projectRoot, "data", "sample.zip");
            string outputDir = Path.Combine(projectRoot, "data", "out");
            string decodedFile = Path.Combine(outputDir, "decoded.zip");

            // 确保输出目录存在
            Directory.CreateDirectory(outputDir);

            // 清理旧文件
            foreach (var file in Directory.GetFiles(outputDir, "*.png"))
            {
                File.Delete(file);
            }
            foreach (var file in Directory.GetFiles(outputDir, "*.jpg"))
            {
                File.Delete(file);
            }

            if (File.Exists(decodedFile))
            {
                File.Delete(decodedFile);
            }

            Console.WriteLine($"输入文件: {inputFile}");
            Console.WriteLine($"输出目录: {outputDir}");
            Console.WriteLine();

            // 步骤1: 编码文件为二维码图片
            Console.WriteLine("[步骤1] 将文件编码为 DataMatrix 图片...");
            var encodedImages = EncodeFile(inputFile, outputDir);
            if (encodedImages.Count == 0)
            {
                Console.WriteLine("错误：未生成任何图片！");
                Console.WriteLine("\n按任意键退出...");
                Console.ReadKey();
                return;
            }

            Console.WriteLine($"生成了 {encodedImages.Count} 张图片：");
            foreach (var img in encodedImages)
            {
                Console.WriteLine($"  - {Path.GetFileName(img)}");
            }

            Console.WriteLine();

            // 步骤2: 从二维码图片解码文件
            Console.WriteLine("[步骤2] 从 DataMatrix 图片解码文件...");
            bool decodeSuccess = DecodeImages(encodedImages, decodedFile);
            Console.WriteLine();

            // 步骤3: 验证结果
            Console.WriteLine("[步骤3] 验证解码内容...");
            if (decodeSuccess && File.Exists(decodedFile))
            {
                VerifyFiles(inputFile, decodedFile);
            }
            else
            {
                Console.WriteLine("错误：解码失败！");
            }

            Console.WriteLine("\n测试完成。");
        }

        /// <summary>
        /// 将文件编码为 DataMatrix 图片序列
        /// </summary>
        private static List<string> EncodeFile(string inputFile, string outputDir)
        {
            var generatedImages = new List<string>();

            if (!File.Exists(inputFile))
            {
                Console.WriteLine($"错误：找不到输入文件：{inputFile}");
                return generatedImages;
            }

            // 计算最优 DataMatrix 配置
            var matrix = ImageEncoder.CalculateScreenDataMatrix(ScreenWidth, ScreenHeight, Scale, ErrorCorrectionPercent);
            Console.WriteLine($"矩阵配置：");
            Console.WriteLine($"  版本: {matrix.BestVersion}");
            Console.WriteLine($"  网格: {matrix.MaxRows}x{matrix.MaxCols}");
            Console.WriteLine($"  二维码尺寸: {matrix.CodeSize} 像素");
            Console.WriteLine($"  每个二维码字节数: {matrix.CodeByteCount}");
            Console.WriteLine($"  每页容量: {matrix.PageByteCount} 字节");

            var pageInfo = ImageEncoder.CalculatePageInfo(matrix, Scale);
            Console.WriteLine($"  屏幕尺寸: {ScreenWidth}x{ScreenHeight}");
            Console.WriteLine($"  图片尺寸: {pageInfo.BitmapWidth}x{pageInfo.BitmapHeight}");
            Console.WriteLine();
            var ts = ImageEncoder.GenerateFileId();

            using (var fileStream = new FileStream(inputFile, FileMode.Open, FileAccess.Read))
            {
                string fileName = "测试文件_Sample.zip";
                int totalPages = (int)Math.Ceiling((double)fileStream.Length / matrix.PageByteCount / (Colorful ? 3 : 1));
                if (totalPages == 0) totalPages = 1;

                int pageNumber = 0;
                while (fileStream.Position < fileStream.Length)
                {
                    pageNumber++;
                    long pageOffset = fileStream.Position;

                    var bitmap = ImageEncoder.GenerateDataMatrixBitmap(
                        fileStream,
                        matrix,
                        pageInfo,
                        ColorDepth,
                        Colorful,
                        Scale,
                        fileName,
                        true, // includeFileName
                        pageNumber,
                        totalPages,
                        ts,
                        false,
                        ErrorCorrectionPercent
                    );

                    if (bitmap != null)
                    {
                        string outputPath = Path.Combine(outputDir, $"page_{pageNumber:D6}.png");
                        bitmap.Save(outputPath, ImageFormat.Png);
                        bitmap.Dispose();
                        generatedImages.Add(outputPath);

                        long bytesEncoded = fileStream.Position - pageOffset;
                        Console.WriteLine($"  第 {pageNumber}/{totalPages} 页: 编码了 {bytesEncoded} 字节");
                    }
                    else
                    {
                        break;
                    }
                }
            }

            return generatedImages;
        }

        /// <summary>
        /// 从 DataMatrix 图片序列解码文件
        /// </summary>
        private static bool DecodeImages(List<string> imageFiles, string outputFile, bool debug = false)
        {
            try
            {
                using (var outputStream = new FileStream(outputFile, FileMode.Create, FileAccess.Write))
                {
                    string detectedFileName = null;

                    foreach (var imageFile in imageFiles.OrderBy(f => f))
                    {
                        Console.WriteLine($"  处理中: {Path.GetFileName(imageFile)}");

                        // 解码当前页并直接写入输出流
                        bool ok = ImageDecoder.ReadToFile(imageFile, outputStream, debug);
                        if (!ok)
                        {
                            Console.WriteLine($"    错误：解码 {Path.GetFileName(imageFile)} 失败");
                            continue;
                        }

                        Console.WriteLine($"    成功 - 当前输出大小: {outputStream.Length} 字节");
                    }

                    Console.WriteLine($"\n解码文件: {outputFile}");
                    if (!string.IsNullOrEmpty(detectedFileName))
                    {
                        Console.WriteLine($"原始文件名: {detectedFileName}");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"解码过程中发生错误: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return false;
            }
        }

        /// <summary>
        /// 为编码后的图像添加噪点、水印、压缩后进行解码测试
        /// </summary>
        private static void TestNoisyDecode()
        {
            Console.WriteLine("\n=== 噪声解码测试 ===\n");

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string projectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
            string outputDir = Path.Combine(projectRoot, "data", "out");

            var originalImages = Directory.GetFiles(outputDir, "page_*.png").Where(c => !c.Contains("degraded")).OrderBy(f => f).ToList();
            if (originalImages.Count == 0)
            {
                Console.WriteLine($"跳过：在 {outputDir} 中找不到 page_*.png 文件");
                return;
            }

            Directory.CreateDirectory(outputDir);
            var degradedImages = new List<string>();

            foreach (var originalImage in originalImages)
            {
                string fileName = Path.GetFileNameWithoutExtension(originalImage);
                Console.WriteLine($"处理中: {Path.GetFileName(originalImage)}");

                using (Mat image = Cv2.ImRead(originalImage, ImreadModes.Color))
                {
                    // 1. 添加高斯噪点
                    Mat noise = new Mat(image.Size(), MatType.CV_8UC3);
                    Cv2.Randn(noise, new Scalar(0, 0, 0), new Scalar(NoiseStdDev, NoiseStdDev, NoiseStdDev));
                    Mat noisy = new Mat();
                    Cv2.Add(image, noise, noisy);
                    noise.Dispose();

                    // 2. 添加透明水印（正中 + 十字分4区后每区正中）
                    string watermark = "WATERMARK";
                    double fontScale = 2.0;
                    int thickness = 3;
                    var textSize = Cv2.GetTextSize(watermark, HersheyFonts.HersheySimplex, fontScale, thickness, out int baseline);

                    int w = noisy.Width;
                    int h = noisy.Height;
                    int cx = w / 2;
                    int cy = h / 2;

                    // 5 个水印位置
                    var positions = new[]
                    {
                        (cx, cy),           // 中心
                        (cx / 2, cy / 2),   // 左上区中心
                        (cx * 3 / 2, cy / 2),   // 右上区中心
                        (cx / 2, cy * 3 / 2),   // 左下区中心
                        (cx * 3 / 2, cy * 3 / 2) // 右下区中心
                    };

                    Mat overlay = noisy.Clone();
                    foreach (var (px, py) in positions)
                    {
                        int tx = px - textSize.Width / 2;
                        int ty = py + textSize.Height / 2;
                        Cv2.PutText(overlay, watermark, new OpenCvSharp.Point(tx, ty), HersheyFonts.HersheySimplex, fontScale, new Scalar(255, 255, 255), thickness, LineTypes.AntiAlias);
                    }
                    // 透明混合（alpha = 0.4）
                    Cv2.AddWeighted(overlay, 0.4, noisy, 0.6, 0, noisy);
                    overlay.Dispose();

                    // 3. 模拟 H.264/JPEG 块效应：低质量 JPEG 重编码
                    string degradedPath = Path.Combine(outputDir, $"{fileName}_degraded.jpg");
                    Cv2.ImWrite(degradedPath, noisy, new int[] { (int)ImwriteFlags.JpegQuality, JpegQuality });

                    degradedImages.Add(degradedPath);

                    noisy.Dispose();
                }
            }

            Console.WriteLine($"降级图片已保存。尝试解码 {degradedImages.Count} 张图片...");

            string degradedDecodedFile = Path.Combine(outputDir, "decoded_degraded.zip");
            if (File.Exists(degradedDecodedFile))
            {
                File.Delete(degradedDecodedFile);
            }

            bool decodeSuccess = DecodeImages(degradedImages, degradedDecodedFile, false);
            if (decodeSuccess && File.Exists(degradedDecodedFile))
            {
                Console.WriteLine($"  解码文件: {degradedDecodedFile} ({new FileInfo(degradedDecodedFile).Length} 字节)");
            }
            else
            {
                Console.WriteLine("  失败：降级图片解码失败。");
            }

            // 与源文件进行逐图片、逐二维码对比
            Console.WriteLine("\n开始与源文件进行逐图片、逐二维码对比...");
            CompareDecodeResults(originalImages, degradedImages);

            // 如果生成了解码文件，再与原始文件做字节级对比
            string originalInputFile = Path.Combine(projectRoot, "data", "sample.zip");
            if (File.Exists(degradedDecodedFile) && File.Exists(originalInputFile))
            {
                Console.WriteLine("\n开始与原始输入文件进行字节级对比...");
                VerifyFiles(originalInputFile, degradedDecodedFile);
            }

            Console.WriteLine();
        }

        /// <summary>
        /// 逐图片、逐二维码对比原始图片与降级图片的解码结果
        /// </summary>
        private static void CompareDecodeResults(List<string> originalImages, List<string> degradedImages)
        {
            bool anyDiff = false;
            for (int imgIndex = 0; imgIndex < originalImages.Count; imgIndex++)
            {
                string origPath = originalImages[imgIndex];
                string degPath = degradedImages[imgIndex];
                string degName = Path.GetFileName(degPath);

                ImageDecoder.DecodeResult origResult;
                ImageDecoder.DecodeResult degResult;

                try
                {
                    origResult = ImageDecoder.DecodeImageWithMetadata(origPath, false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  第 {imgIndex + 1} 张图片 ({degName})：原始图片解析异常: {ex.Message}");
                    anyDiff = true;
                    continue;
                }

                try
                {
                    degResult = ImageDecoder.DecodeImageWithMetadata(degPath, false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  第 {imgIndex + 1} 张图片 ({degName})：降级图片解析异常: {ex.Message}");
                    anyDiff = true;
                    continue;
                }

                var origBlocks = origResult.DataBlocks?.ToDictionary(b => (b.row, b.col), b => b.data) ?? new Dictionary<(int, int), byte[]>();
                var degBlocks = degResult.DataBlocks?.ToDictionary(b => (b.row, b.col), b => b.data) ?? new Dictionary<(int, int), byte[]>();

                // 检查降级后缺失或数据不一致的二维码
                foreach (var kvp in origBlocks)
                {
                    if (!degBlocks.ContainsKey(kvp.Key))
                    {
                        Console.WriteLine($"  第 {imgIndex + 1} 张图片 ({degName})：缺失二维码 ({kvp.Key.row}, {kvp.Key.col})");
                        ImageDecoder.DecodeImageWithMetadata(degPath, true);
                        anyDiff = true;
                    }
                    else if (!kvp.Value.SequenceEqual(degBlocks[kvp.Key]))
                    {
                        Console.WriteLine($"  第 {imgIndex + 1} 张图片 ({degName})：二维码 ({kvp.Key.row}, {kvp.Key.col}) 数据不一致");
                        ImageDecoder.DecodeImageWithMetadata(degPath, true);
                        anyDiff = true;
                    }
                }

                // 检查降级后多出的二维码
                foreach (var kvp in degBlocks)
                {
                    if (!origBlocks.ContainsKey(kvp.Key))
                    {
                        Console.WriteLine($"  第 {imgIndex + 1} 张图片 ({degName})：多出二维码 ({kvp.Key.row}, {kvp.Key.col})");
                        anyDiff = true;
                    }
                }
            }

            if (!anyDiff)
            {
                Console.WriteLine("  所有降级图片的二维码解码结果与原始图片一致。");
            }
        }

        /// <summary>
        /// 验证原始文件和解码后的文件是否一致
        /// </summary>
        private static void VerifyFiles(string originalFile, string decodedFile)
        {
            if (!File.Exists(originalFile))
            {
                Console.WriteLine($"错误：找不到原始文件：{originalFile}");
                return;
            }

            if (!File.Exists(decodedFile))
            {
                Console.WriteLine($"错误：找不到解码文件：{decodedFile}");
                return;
            }

            var originalBytes = File.ReadAllBytes(originalFile);
            var decodedBytes = File.ReadAllBytes(decodedFile);

            Console.WriteLine($"原始文件大小: {originalBytes.Length} 字节");
            Console.WriteLine($"解码文件大小:  {decodedBytes.Length} 字节");
            Console.WriteLine();

            if (originalBytes.Length != decodedBytes.Length)
            {
                Console.WriteLine("警告：文件大小不一致！");
            }

            // 逐字节比较
            int differences = 0;
            int maxDiffToShow = 5;

            int minLength = Math.Min(originalBytes.Length, decodedBytes.Length);
            for (int i = 0; i < minLength; i++)
            {
                if (originalBytes[i] != decodedBytes[i])
                {
                    differences++;
                    if (differences <= maxDiffToShow)
                    {
                        Console.WriteLine($"  第 {i} 字节处存在差异：");
                        Console.WriteLine($"    原始: 0x{originalBytes[i]:X2} ({(char)originalBytes[i]})");
                        Console.WriteLine($"    解码:  0x{decodedBytes[i]:X2} ({(char)decodedBytes[i]})");
                    }
                }
            }

            // 检查长度差异
            if (originalBytes.Length > decodedBytes.Length)
            {
                int missing = originalBytes.Length - decodedBytes.Length;
                Console.WriteLine($"  解码文件末尾缺少 {missing} 字节");
                differences += missing;
            }
            else if (decodedBytes.Length > originalBytes.Length)
            {
                int extra = decodedBytes.Length - originalBytes.Length;
                Console.WriteLine($"  解码文件末尾多出 {extra} 字节");
                differences += extra;
            }

            Console.WriteLine();
            if (differences == 0)
            {
                Console.WriteLine("成功：文件完全一致！");
                Console.WriteLine("编解码测试通过。");
            }
            else
            {
                Console.WriteLine($"失败：共发现 {differences} 处差异。");
            }
        }
    }
}
