using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using screen_file_transmit;

namespace qr_codec_test
{
    class Program
    {
        // 屏幕配置参数
        private const int ScreenWidth = 1920 - 18;
        private const int ScreenHeight = 1080 - 89;
        private const int Scale = 4;
        private const int ColorDepth = 1;
        private const bool Colorful = false;

        static void Main(string[] args)
        {
            Console.WriteLine("=== QR File Transfer Codec Test ===\n");

            // 路径配置
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string projectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
            string inputFile = Path.Combine(projectRoot, "data", "sample.txt");
            string outputDir = Path.Combine(projectRoot, "data", "out");
            string decodedFile = Path.Combine(outputDir, "decoded.txt");

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
        static List<string> EncodeFile(string inputFile, string outputDir)
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
            Console.WriteLine($"  Image size: {pageInfo.BitmapWidth}x{pageInfo.BitmapHeight}");
            Console.WriteLine();

            using (var fileStream = new FileStream(inputFile, FileMode.Open, FileAccess.Read))
            {
                string fileName = "测试文件_Sample.txt";
                int totalPages = (int)Math.Ceiling((double)fileStream.Length / matrix.PageByteCount);
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
                        Guid.NewGuid().ToString("N").Substring(0, 10) // sessionGuid
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
        static bool DecodeImages(List<string> imageFiles, string outputFile)
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

                        // 解码当前页
                        var decodedData = DataMatrixDecoder.DecodeImage(imageFile);

                        if (decodedData.Count == 0)
                        {
                            Console.WriteLine($"    WARNING: No DataMatrix codes found!");
                            continue;
                        }

                        // 按行列排序
                        var sortedData = decodedData.OrderBy(d => d.row).ThenBy(d => d.col).ToList();
                        Console.WriteLine($"    Decoded {sortedData.Count} blocks");

                        // 处理第一页
                        if (isFirstPage)
                        {
                            detectedFileName = DataMatrixDecoder.ExtractFileName(sortedData);
                            if (!string.IsNullOrEmpty(detectedFileName))
                            {
                                Console.WriteLine($"    Original filename: {detectedFileName}");
                            }

                            isFirstPage = false;
                        }

                        // 写入数据
                        foreach (var block in sortedData)
                        {
                            byte[] dataToWrite;

                            if (block.row == 0 && block.col == 0)
                            {
                                // 第一个块需要去掉文件名部分
                                dataToWrite = DataMatrixDecoder.ExtractDataWithoutFileName(block.data);
                            }
                            else
                            {
                                dataToWrite = block.data;
                            }

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
        /// 验证原始文件和解码后的文件是否一致
        /// </summary>
        static void VerifyFiles(string originalFile, string decodedFile)
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