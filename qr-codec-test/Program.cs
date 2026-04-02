using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using OpenCvSharp;
using screen_file_transmit; 
using ZXing.Datamatrix;

namespace qr_codec_test
{
    class Program
    {
        // 屏幕配置参数
        private const int ScreenWidth = 1920;//- 79;
        private const int ScreenHeight = 1080;//- 8;
        private const int Scale = 3;
        private const int ColorDepth = 1;
        private const bool Colorful = true;

        static void Main(string[] args)
        {
            // 运行条码区域检测测试
            TestBarcodeDetection();
        }

        static void TestBarcodeDetection()
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
                DetectBarcodes(imageFile);
            }

            Console.WriteLine("\nDetection completed. Press any key to exit...");
            Console.ReadKey();
            Cv2.DestroyAllWindows();
        }

        static void Test()
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

        static void DetectBarcodes(string imageFile)
        {
            using (Mat rawImg = Cv2.ImRead(imageFile))
            {
                if (rawImg.Empty())
                {
                    Console.WriteLine("  Failed to load image.");
                    return;
                }

                var img = DataMatrixDecoder.StretchSideRegion(rawImg, true);
                double imgWidth = img.Width;
                double imgHeight = img.Height;
                int edgeWidth = 120; // 只取最边缘窄条带，避免包含 DataMatrix

                Mat debug = img.Clone();
                var allLeft = new List<OpenCvSharp.Rect>();
                var allRight = new List<OpenCvSharp.Rect>();

                Console.WriteLine($"  Image size: {imgWidth}x{imgHeight}");

                // 分别检测左边缘和右边缘
                for (int side = 0; side < 2; side++)
                {
                    bool isLeft = side == 0;
                    int x0 = isLeft ? 0 : (int)imgWidth - edgeWidth;
                    var roiRect = new OpenCvSharp.Rect(x0, 0, edgeWidth, (int)imgHeight);
                    using (Mat roi = new Mat(img, roiRect))
                    {
                        var found = DetectBarcodesInRoi(roi, isLeft ? "Left" : "Right", roiRect);
                        if (isLeft) allLeft = found;
                        else allRight = found;

                        foreach (var r in found)
                        {
                            var absRect = new OpenCvSharp.Rect(r.X + x0, r.Y, r.Width, r.Height);
                            var color = isLeft ? new Scalar(0, 0, 255) : new Scalar(0, 255, 255);
                            Cv2.Rectangle(debug, absRect, color, 3);
                        }
                    }
                }

                Cv2.PutText(debug, $"Left: {allLeft.Count}", new OpenCvSharp.Point(10, 30),
                    HersheyFonts.HersheySimplex, 1, new Scalar(0, 0, 255), 2);
                Cv2.PutText(debug, $"Right: {allRight.Count}", new OpenCvSharp.Point((int)imgWidth - 250, 30),
                    HersheyFonts.HersheySimplex, 1, new Scalar(0, 255, 255), 2);

                Cv2.ImShow("Detection Result", debug);

                Console.WriteLine($"  Left barcodes : {allLeft.Count}");
                foreach (var r in allLeft)
                    Console.WriteLine($"    -> [{r.X},{r.Y}] {r.Width}x{r.Height}");
                Console.WriteLine($"  Right barcodes: {allRight.Count}");
                foreach (var r in allRight)
                    Console.WriteLine($"    -> [{r.X},{r.Y}] {r.Width}x{r.Height}");

                Console.WriteLine("  Press any key in image window to continue...");
                Cv2.WaitKey(0);
                debug.Dispose();
            }
        }

        static List<OpenCvSharp.Rect> MergeVerticallyAlignedRects(List<OpenCvSharp.Rect> rects, int yGapThreshold)
        {
            if (rects.Count == 0) return rects;

            // 按 Y 坐标排序
            var sorted = rects.OrderBy(r => r.Y).ToList();
            var merged = new List<OpenCvSharp.Rect>();
            var current = sorted[0];

            for (int i = 1; i < sorted.Count; i++)
            {
                var next = sorted[i];
                int xOverlap = Math.Min(current.X + current.Width, next.X + next.Width) - Math.Max(current.X, next.X);
                int minWidth = Math.Min(current.Width, next.Width);
                int yGap = next.Y - (current.Y + current.Height);

                // X 方向重叠超过较小宽度的 40%，且 Y 间隙在阈值内
                if (xOverlap > minWidth * 0.4 && yGap <= yGapThreshold)
                {
                    int newX = Math.Min(current.X, next.X);
                    int newY = Math.Min(current.Y, next.Y);
                    int newRight = Math.Max(current.X + current.Width, next.X + next.Width);
                    int newBottom = Math.Max(current.Y + current.Height, next.Y + next.Height);
                    current = new OpenCvSharp.Rect(newX, newY, newRight - newX, newBottom - newY);
                }
                else
                {
                    merged.Add(current);
                    current = next;
                }
            }
            merged.Add(current);
            return merged;
        }

        static List<OpenCvSharp.Rect> DetectBarcodesInRoi(Mat roi, string label, OpenCvSharp.Rect roiOffset)
        {
            var result = new List<OpenCvSharp.Rect>();

            using (Mat gray = new Mat())
            {
                Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);
                Cv2.ImShow($"{label} - 1 Gray", gray);

                using (Mat gradX = new Mat())
                {
                    Cv2.Sobel(gray, gradX, MatType.CV_32F, 1, 0, ksize: 3);
                    Cv2.ConvertScaleAbs(gradX, gradX);
                    Cv2.ImShow($"{label} - 2 Sobel X", gradX);

                    using (Mat binary = new Mat())
                    {
                        Cv2.Threshold(gradX, binary, 25, 255, ThresholdTypes.Binary);
                        Cv2.ImShow($"{label} - 3 Binary", binary);

                        using (Mat closed = new Mat())
                        using (Mat kernelClose = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(35, 1)))
                        {
                            Cv2.MorphologyEx(binary, closed, MorphTypes.Close, kernelClose);
                            Cv2.ImShow($"{label} - 4 Close", closed);

                            using (Mat dilated = new Mat())
                            using (Mat eroded = new Mat())
                            using (Mat kernelSmall = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3)))
                            {
                                Cv2.Dilate(closed, dilated, kernelSmall, null, 1);
                                Cv2.Erode(dilated, eroded, kernelSmall, null, 1);
                                Cv2.ImShow($"{label} - 5 OpenClose", eroded);

                                Cv2.FindContours(eroded, out OpenCvSharp.Point[][] contours, out HierarchyIndex[] hierarchy,
                                    RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                                Console.WriteLine($"    {label} contours: {contours.Length}");

                                var rawRects = new List<OpenCvSharp.Rect>();
                                foreach (var c in contours)
                                {
                                    OpenCvSharp.Rect r = Cv2.BoundingRect(c);
                                    double ratio = r.Height / (double)r.Width;
                                    double area = r.Width * r.Height;

                                    if (area > 100)
                                    {
                                        Console.WriteLine($"      rect [{r.X},{r.Y}] {r.Width}x{r.Height} ratio={ratio:F2} area={area}");
                                    }

                                    // 保留高瘦型候选（条码竖直放置）
                                    if (ratio > 1.2 && r.Height > 25 && area > 300)
                                    {
                                        rawRects.Add(r);
                                    }
                                }

                                Console.WriteLine($"    {label} rawRects after filter: {rawRects.Count}");
                                var merged = MergeVerticallyAlignedRects(rawRects, yGapThreshold: 60);
                                Console.WriteLine($"    {label} merged count: {merged.Count}");
                                result.AddRange(merged);
                            }
                        }
                    }
                }
            }

            return result;
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
            Console.WriteLine($"  Screen size: {ScreenWidth}x{ScreenHeight}");
            Console.WriteLine($"  Image size: {pageInfo.BitmapWidth}x{pageInfo.BitmapHeight}");
            Console.WriteLine();

            using (var fileStream = new FileStream(inputFile, FileMode.Open, FileAccess.Read))
            {
                string fileName = "测试文件_Sample.txt";
                int totalPages = (int)Math.Ceiling((double)fileStream.Length / matrix.PageByteCount /(Colorful?3:1));
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

                        // 解码当前页（带元数据）
                        var decodeResult = DataMatrixDecoder.DecodeImageWithMetadata(imageFile);

                        if (decodeResult.DataBlocks.Count == 0)
                        {
                            Console.WriteLine($"    WARNING: No DataMatrix codes found!");
                            continue;
                        }

                        Console.WriteLine($"    Metadata: Grid={decodeResult.MaxRows}x{decodeResult.MaxCols}, " +
                            $"Colorful={decodeResult.Colorful}, Depth={decodeResult.ColorDepth}, " +
                            $"Page={decodeResult.CurrentPage}/{decodeResult.TotalPages}");

                        // 按行列排序
                        var sortedData = decodeResult.DataBlocks.OrderBy(d => d.row).ThenBy(d => d.col).ToList();
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