using OpenCvSharp;
using screen_file_receiver;
using screen_file_transmit;
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace qr_codec_test
{
    internal class Program
    {
        // 屏幕配置参数
        private const int ScreenWidth = 1920;//- 79;

        private const int ScreenHeight = 1080;//- 8;
        private const int Scale = 3;
        private const int ColorDepth = 1;
        private const bool Colorful = true;
        private const int NoiseStdDev = 0;   // 高斯噪点标准差，值越大噪点越重
        private const int JpegQuality = 80;  // JPEG 压缩质量，值越小块效应/伪影越重

        private static void Main(string[] args)
        {
            TestEncodeAndDecode();
            //TestNoisyDecode();
            // 运行条码区域检测测试
            //TestBarcodeDetection();
        }

        private static void TestBarcodeDetection()
        {
            Console.WriteLine("=== Barcode Detection Test ===\n");

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string projectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
            string dataDir = Path.Combine(projectRoot, "data");

            for (int i = 1; i <= 1; i++)
            {
                string imageFile = Path.Combine(dataDir, $"page_{i:D6}.png");
                if (!File.Exists(imageFile))
                {
                    Console.WriteLine($"SKIP: {imageFile} not found");
                    continue;
                }

                Console.WriteLine($"Processing: {Path.GetFileName(imageFile)}");
                var meta = DataMatrixReader.ReadMetadata(imageFile);
                Console.WriteLine($"  Metadata:     {(meta.Metadata != null ? BitConverter.ToString(meta.Metadata) : "null")}");
                Console.WriteLine($"  MaxRows:      {meta.MaxRows}");
                Console.WriteLine($"  MaxCols:      {meta.MaxCols}");
                Console.WriteLine($"  Colorful:     {meta.Colorful}");
                Console.WriteLine($"  ColorDepth:   {meta.ColorDepth}");
                Console.WriteLine($"  CurrentPage:  {meta.CurrentPage}");
                Console.WriteLine($"  TotalPages:   {meta.TotalPages}");
                Console.WriteLine($"  FileName:     {meta.FileName}");
                Console.WriteLine($"  Timestamp:    {meta.FileId}");
            }

            Console.WriteLine("\nDetection completed. Press any key to exit...");
            Cv2.WaitKey();
            Cv2.DestroyAllWindows();
        }

        private static void TestEncodeAndDecode()
        {
            Console.WriteLine("=== QR File Transfer Codec Test ===\n");

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

            if (File.Exists(decodedFile))
            {
                File.Delete(decodedFile);
            }

            Console.WriteLine($"Input file: {inputFile}");
            Console.WriteLine($"Output directory: {outputDir}");
            Console.WriteLine();

            // 步骤1: 编码文件为二维码图片
            Console.WriteLine("[Step 1] Encoding file to DataMatrix images...");
            var encodedImages = EncodeFile(inputFile, outputDir);
            if (encodedImages.Count == 0)
            {
                Console.WriteLine("ERROR: No images generated!");
                Console.WriteLine("\nPress any key to exit...");
                Console.ReadKey();
                return;
            }

            Console.WriteLine($"Generated {encodedImages.Count} image(s):");
            foreach (var img in encodedImages)
            {
                Console.WriteLine($"  - {Path.GetFileName(img)}");
            }

            Console.WriteLine();

            // 步骤2: 从二维码图片解码文件
            Console.WriteLine("[Step 2] Decoding DataMatrix images to file...");
            bool decodeSuccess = DecodeImages(encodedImages, decodedFile);
            Console.WriteLine();

            // 步骤3: 验证结果
            Console.WriteLine("[Step 3] Verifying decoded content...");
            if (decodeSuccess && File.Exists(decodedFile))
            {
                VerifyFiles(inputFile, decodedFile);
            }
            else
            {
                Console.WriteLine("ERROR: Decoding failed!");
            }

            Console.WriteLine("\nTest completed.");
        }

        /// <summary>
        /// 将文件编码为 DataMatrix 图片序列
        /// </summary>
        private static List<string> EncodeFile(string inputFile, string outputDir)
        {
            var generatedImages = new List<string>();

            if (!File.Exists(inputFile))
            {
                Console.WriteLine($"ERROR: Input file not found: {inputFile}");
                return generatedImages;
            }

            // 计算最优 DataMatrix 配置
            var matrix = DataMatrixEncoder.CalculateScreenDataMatrix(ScreenWidth, ScreenHeight, Scale);
            Console.WriteLine($"Matrix configuration:");
            Console.WriteLine($"  Version: {matrix.BestVersion}");
            Console.WriteLine($"  Grid: {matrix.MaxRows}x{matrix.MaxCols}");
            Console.WriteLine($"  Code size: {matrix.CodeSize}px");
            Console.WriteLine($"  Byte count per code: {matrix.CodeByteCount}");
            Console.WriteLine($"  Page capacity: {matrix.PageByteCount} bytes");

            var pageInfo = DataMatrixEncoder.CalculatePageInfo(matrix, Scale);
            Console.WriteLine($"  Screen size: {ScreenWidth}x{ScreenHeight}");
            Console.WriteLine($"  Image size: {pageInfo.BitmapWidth}x{pageInfo.BitmapHeight}");
            Console.WriteLine();
            var ts = DataMatrixEncoder.GenerateFileId();

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

                    var bitmap = DataMatrixEncoder.GenerateDataMatrixBitmap(
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
                        ts
                    );

                    if (bitmap != null)
                    {
                        string outputPath = Path.Combine(outputDir, $"page_{pageNumber:D6}.png");
                        bitmap.Save(outputPath, ImageFormat.Png);
                        bitmap.Dispose();
                        generatedImages.Add(outputPath);

                        long bytesEncoded = fileStream.Position - pageOffset;
                        Console.WriteLine($"  Page {pageNumber}/{totalPages}: {bytesEncoded} bytes encoded");
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
                    bool isFirstPage = true;
                    string detectedFileName = null;

                    foreach (var imageFile in imageFiles.OrderBy(f => f))
                    {
                        Console.WriteLine($"  Processing: {Path.GetFileName(imageFile)}");

                        // 解码当前页（带元数据）
                        var decodeResult = DataMatrixReader.DecodeImageWithMetadata(imageFile, debug);

                        if (decodeResult.DataBlocks.Count == 0)
                        {
                            Console.WriteLine($"    WARNING: No DataMatrix codes found!");
                            continue;
                        }

                        var meta = decodeResult.Metadata;
                        Console.WriteLine($"    Metadata: Grid={meta?.MaxRows}x{meta?.MaxCols}, " +
                            $"Colorful={meta?.Colorful}, Depth={meta?.ColorDepth}, " +
                            $"Page={meta?.CurrentPage}/{meta?.TotalPages}");

                        // 按行列排序
                        var sortedData = decodeResult.DataBlocks.OrderBy(d => d.row).ThenBy(d => d.col).ToList();
                        Console.WriteLine($"    Decoded {sortedData.Count} blocks");

                        // 写入数据
                        foreach (var block in sortedData)
                        {
                            byte[] dataToWrite;

                            dataToWrite = block.data;

                            if (dataToWrite.Length > 0)
                            {
                                outputStream.Write(dataToWrite, 0, dataToWrite.Length);
                            }
                        }

                        Console.WriteLine($"    OK - Current output size: {outputStream.Length} bytes");
                    }

                    Console.WriteLine($"\nDecoded file: {outputFile}");
                    if (!string.IsNullOrEmpty(detectedFileName))
                    {
                        Console.WriteLine($"Original name: {detectedFileName}");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR during decoding: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return false;
            }
        }

        /// <summary>
        /// 为编码后的图像添加噪点、水印、压缩后进行解码测试
        /// </summary>
        private static void TestNoisyDecode()
        {
            Console.WriteLine("\n=== Noisy Decode Test ===\n");

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string projectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
            string outputDir = Path.Combine(projectRoot, "data", "out");

            var originalImages = Directory.GetFiles(outputDir, "page_*.png").Where(c => !c.Contains("degraded")).OrderBy(f => f).ToList();
            if (originalImages.Count == 0)
            {
                Console.WriteLine($"SKIP: no page_*.png files found in {outputDir}");
                return;
            }

            Directory.CreateDirectory(outputDir);
            var degradedImages = new List<string>();

            foreach (var originalImage in originalImages)
            {
                string fileName = Path.GetFileNameWithoutExtension(originalImage);
                Console.WriteLine($"Processing: {Path.GetFileName(originalImage)}");

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

                    // 绘制十字分割线
                    //Cv2.Line(noisy, new OpenCvSharp.Point(cx, 0), new OpenCvSharp.Point(cx, h), new Scalar(200, 200, 200), 2);
                    //Cv2.Line(noisy, new OpenCvSharp.Point(0, cy), new OpenCvSharp.Point(w, cy), new Scalar(200, 200, 200), 2);

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

                    //// 从压缩后的图像重新加载并解码
                    //using (Mat degraded = Cv2.ImRead(degradedPath, ImreadModes.Color))
                    //{
                    //    string degradedPng = Path.Combine(outputDir, $"{fileName}_degraded.png");
                    //    Cv2.ImWrite(degradedPng, degraded);
                    //    degradedImages.Add(degradedPng);
                    //}

                    noisy.Dispose();
                }
            }

            Console.WriteLine($"Degraded images saved. Attempting to decode {degradedImages.Count} image(s)...");

            string degradedDecodedFile = Path.Combine(outputDir, "decoded_degraded.zip");
            if (File.Exists(degradedDecodedFile))
            {
                File.Delete(degradedDecodedFile);
            }

            bool decodeSuccess = DecodeImages(degradedImages, degradedDecodedFile, false);
            if (decodeSuccess && File.Exists(degradedDecodedFile))
            {
                Console.WriteLine($"  Decoded file: {degradedDecodedFile} ({new FileInfo(degradedDecodedFile).Length} bytes)");
            }
            else
            {
                Console.WriteLine("  FAILED: Decoding degraded images failed.");
            }

            Console.WriteLine();
        }

        /// <summary>
        /// 验证原始文件和解码后的文件是否一致
        /// </summary>
        private static void VerifyFiles(string originalFile, string decodedFile)
        {
            if (!File.Exists(originalFile))
            {
                Console.WriteLine($"ERROR: Original file not found: {originalFile}");
                return;
            }

            if (!File.Exists(decodedFile))
            {
                Console.WriteLine($"ERROR: Decoded file not found: {decodedFile}");
                return;
            }

            var originalBytes = File.ReadAllBytes(originalFile);
            var decodedBytes = File.ReadAllBytes(decodedFile);

            Console.WriteLine($"Original file size: {originalBytes.Length} bytes");
            Console.WriteLine($"Decoded file size:  {decodedBytes.Length} bytes");
            Console.WriteLine();

            if (originalBytes.Length != decodedBytes.Length)
            {
                Console.WriteLine("WARNING: File sizes differ!");
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
                        Console.WriteLine($"  Difference at byte {i}:");
                        Console.WriteLine($"    Original: 0x{originalBytes[i]:X2} ({(char)originalBytes[i]})");
                        Console.WriteLine($"    Decoded:  0x{decodedBytes[i]:X2} ({(char)decodedBytes[i]})");
                    }
                }
            }

            // 检查长度差异
            if (originalBytes.Length > decodedBytes.Length)
            {
                int missing = originalBytes.Length - decodedBytes.Length;
                Console.WriteLine($"  Missing {missing} bytes at end of decoded file");
                differences += missing;
            }
            else if (decodedBytes.Length > originalBytes.Length)
            {
                int extra = decodedBytes.Length - originalBytes.Length;
                Console.WriteLine($"  Extra {extra} bytes at end of decoded file");
                differences += extra;
            }

            Console.WriteLine();
            if (differences == 0)
            {
                Console.WriteLine("SUCCESS: Files are identical!");
                Console.WriteLine("Encoding and decoding test PASSED.");
            }
            else
            {
                Console.WriteLine($"FAILED: Total {differences} differences found.");
            }
        }
    }
}