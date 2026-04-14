using OpenCvSharp;
using OpenCvSharp.Flann;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using ZXing;
using ZXing.Common;
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;
using Size = OpenCvSharp.Size;

namespace screen_file_receiver
{
    public static class DataMatrixReader
    {
        private static string rcString = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

        /// <summary>
        /// 从图片读取文件
        /// </summary>
        /// <param name="fileName">图片文件路径</param>
        /// <param name="fileStream">输出流</param>
        /// <param name="outputFileName">解析出的原始文件名（如果存在）</param>
        /// <returns>是否成功</returns>
        public static bool ReadToFile(string fileName, Stream fileStream, out string outputFileName, out int currentPage, out int totalPages, bool showDebugImages = false)
        {
            outputFileName = null;
            currentPage = 0;
            totalPages = 0;
            // 读取图像
            Mat image = Cv2.ImRead(fileName);

            // 尝试读取元数据条码（可能在左侧，旋转90度）
            var reader = new BarcodeReader();
            string info = null;

            // 首先尝试从左侧区域读取旋转的条码
            info = TryReadRotatedBarcode(image, reader);

            // 如果找不到，尝试原来的方式（兼容旧格式）
            if (info == null)
            {
                info = TryReadHorizontalBarcode(image, reader);
            }

            if (info == null)
            {
                MessageBox.Show("找不到信息" + fileName);
                return false;
            }

            string infoText = info.TrimStart('$');
            int rowCount, colCount, colorDepth;
            bool isColorful;
            long offset = 0, length = 0, totalLength = 0;

             
            var base64Part = infoText.TrimStart('-');
            var hexParts = Convert.FromBase64String(base64Part);
            if (hexParts.Length < 4)
            {
                MessageBox.Show("元数据格式错误: " + info);
                return false;
            }

            byte b0 = hexParts[0];
            byte b1 = hexParts[1];
            byte b2 = hexParts[2];
            byte b3 = hexParts[3];

            rowCount = b0 >> 4;
            colCount = b0 & 0x0F;
            isColorful = (b1 & 0x80) != 0;
            colorDepth = b1 & 0x7F;
            currentPage = b2;
            totalPages = b3;

            // 新格式不含文件偏移，使用当前流位置追加
            offset = fileStream.Position;
            

            Mat gray = new Mat();
            Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);

            // 二值化处理，便于轮廓检测
            Mat thresh = new Mat();
            Cv2.Threshold(gray, thresh, 127, 255, ThresholdTypes.BinaryInv);

            // 查找轮廓
            Cv2.FindContours(thresh, out Point[][] contours, out HierarchyIndex[] hierarchy, RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

            var lagerContours = contours.Where(c => Cv2.ContourArea(c) > 150).ToList();

            // 在原始图像上绘制所有轮廓
            Mat contoursOutput = image.Clone();
            Cv2.DrawContours(contoursOutput, lagerContours, -1, new Scalar(0, 0, 255), 2);

            if (showDebugImages)
            {
                //Cv2.ImShow("Image  ", contoursOutput);
                Cv2.WaitKey();
            }

            List<string> readResult = new List<string>();
            // 设置 X 坐标容差值
            var list = lagerContours.Select(c => new { Rect = Cv2.BoundingRect(c), Contour = c })
                .Where(c => c.Rect.Height > 2 & c.Rect.Width > 2 && (c.Rect.Height * 1.0 / c.Rect.Width) < 4 &&
                            (c.Rect.Height * 1.0 / c.Rect.Width) > 1.0 / 4)
                .OrderBy(c => c.Rect.Width + c.Rect.Height).ThenBy(c => c.Rect.Y).ThenBy(c => c.Rect.X).ToList();

            // 找到元数据条码后，移除它（通常是最小的）
            list = list.Skip(1).ToList();

            int rindex = 0;
            foreach (var contour in list)
            {
                rindex++;
                Rect rect = contour.Rect;
                Cv2.Rectangle(image, rect, new Scalar(0, 0, 255), 2);
                Mat roi = new Mat(image, rect);

                if (!ReadImage(roi, reader, readResult))
                {
                    Mat roiBin = new Mat();
                    Cv2.Threshold(roi, roiBin, 127, 255, ThresholdTypes.Binary);
                    if (!ReadImage(roiBin, reader, readResult))
                    {
                        if (showDebugImages)
                        {
                            //Cv2.ImShow($"找不到信息 {fileName}/{rindex}/{offset}/{totalLength}", roi);
                            //Cv2.ImShow($"找不到信息 {fileName}/{rindex}/{offset}/{totalLength} Bin", roiBin);
                        }
                        return false;
                    }
                }
            }

            List<(int row, int column, byte[] data)> decodes = new List<(int row, int column, byte[] data)>();

            fileStream.Position = offset;
            foreach (var code in readResult)
            {
                int row = rcString.IndexOf(code[0]);
                int column = rcString.IndexOf(code[1]);
                var base64 = code.Substring(2);

                var bytes = Convert.FromBase64String(base64);
                decodes.Add((row, column, bytes));
            }

            var final = decodes.OrderBy(c => c.row).ThenBy(c => c.column).ToList();

            // 解析第一个 chunk (0,0) 中的文件名
            bool isFirstChunk = true;
            foreach (var valueTuple in final)
            {
                var bytes = valueTuple.data;

                // 如果是第一个 chunk，尝试解析文件名
                if (isFirstChunk)
                {
                    isFirstChunk = false;
                    var fileNameResult = ExtractFileName(bytes);
                    if (fileNameResult.hasFileName)
                    {
                        outputFileName = fileNameResult.fileName;
                        // 写入剩余的数据（去掉文件名部分）
                        var remainingData = bytes.Skip(fileNameResult.totalLength).ToArray();
                        if (remainingData.Length > 0)
                        {
                            fileStream.Write(remainingData, 0, remainingData.Length);
                        }
                        continue;
                    }
                }

                fileStream.Write(bytes, 0, bytes.Length);
            }

            return true;
        }

        /// <summary>
        /// 尝试从左侧区域读取旋转90度的条码
        /// </summary>
        private static string TryReadRotatedBarcode(Mat image, BarcodeReader reader)
        {
            int leftRegionWidth = Math.Min(100, image.Width / 4);  // 左侧区域宽度

            // 提取左侧区域
            Rect leftRegion = new Rect(0, 0, leftRegionWidth, image.Height);
            if (leftRegion.Width > image.Width || leftRegion.Height > image.Height)
                return null;

            Mat leftRoi = new Mat(image, leftRegion);

            // 逆时针旋转90度（使条码变为水平）
            Mat rotated = new Mat();
            Cv2.Transpose(leftRoi, rotated);
            Cv2.Flip(rotated, rotated, FlipMode.Y);

            using (var bitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(rotated))
            {
                var result = reader.Decode(bitmap);
                if (result != null)
                {
                    return result.Text;
                }
            }

            return null;
        }

        /// <summary>
        /// 尝试从顶部区域读取水平条码（兼容旧格式）
        /// </summary>
        private static string TryReadHorizontalBarcode(Mat image, BarcodeReader reader)
        {
            int topRegionHeight = Math.Min(60, image.Height / 8);

            Rect topRegion = new Rect(0, 0, image.Width, topRegionHeight);
            Mat topRoi = new Mat(image, topRegion);

            using (var bitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(topRoi))
            {
                var result = reader.Decode(bitmap);
                if (result != null)
                {
                    return result.Text;
                }
            }

            return null;
        }

        /// <summary>
        /// 从字节数组中提取文件名
        /// </summary>
        private static (bool hasFileName, string fileName, int totalLength) ExtractFileName(byte[] data)
        {
            // 查找 \0 分隔符
            int separatorIndex = Array.IndexOf(data, (byte)0x00);
            if (separatorIndex <= 0)
            {
                // 没有找到分隔符或文件名为空
                return (false, null, 0);
            }

            // 提取文件名（UTF-8 编码）
            var fileNameBytes = data.Take(separatorIndex).ToArray();
            var fileName = Encoding.UTF8.GetString(fileNameBytes);

            return (true, fileName, separatorIndex + 1); // +1 包含分隔符本身
        }

        private static bool ReadImage(Mat image, BarcodeReader reader, List<string> readResult, int retryCount = 3,
            int retryIndex = 0)
        {
            // 转换为 Bitmap 格式，因为 ZXing 读取 Bitmap 格式
            using (var bitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(image))
            {
                // 尝试解码 DataMatrix 编码
                var result = reader.Decode(bitmap);

                if (result != null && result.BarcodeFormat == BarcodeFormat.DATA_MATRIX)
                {
                    readResult.Add(result.Text);
                    // 在图像上绘制矩形框并显示
                    //Cv2.Rectangle(image, rect, new Scalar(0, 255, 0), 2);
                    return true;
                }
                else
                {
                    if (retryIndex >= retryCount)
                    {
                        return false;
                    }
                    //Cv2.ImShow("Error " + retryIndex, image);

                    // 放大图像 (双线性插值)
                    double scaleFactor = 2.0; // 放大倍数
                    Mat resizedImage = new Mat();
                    Cv2.Resize(image, resizedImage, new Size(), scaleFactor, scaleFactor, InterpolationFlags.Linear);

                    // 创建锐化滤波器内核 
                    Mat sharpenKernel = Mat.FromArray(new float[,]
                    {
                        { -1, -1, -1 },
                        { -1, 9, -1 },
                        { -1, -1, -1 }
                    });

                    // 应用锐化滤波器
                    Mat sharpenedImage = new Mat();
                    Cv2.Filter2D(resizedImage, sharpenedImage, -1, sharpenKernel);

                    // 显示结果
                    //Cv2.ImShow("Resized Image " + retryIndex, resizedImage);
                    //Cv2.ImShow("Sharpened Image " + retryIndex, sharpenedImage);
                    //Cv2.WaitKey(0);

                    return ReadImage(sharpenedImage, reader, readResult, retryCount, retryIndex + 1);
                }
            }
        }

        /// <summary>
        /// 截取图像左侧或右侧 1/10 区域，水平拉宽 5 倍
        /// </summary>
        public static Mat StretchSideRegion(Mat image, bool takeLeft)
        {
            if (image == null || image.Empty())
                return null;

            int cropWidth = image.Width / 10;
            if (cropWidth < 1)
                cropWidth = 1;

            Rect cropRect;
            if (takeLeft)
            {
                cropRect = new Rect(0, 0, cropWidth, image.Height);
            }
            else
            {
                cropRect = new Rect(image.Width - cropWidth, 0, cropWidth, image.Height);
            }

            Mat cropped = new Mat(image, cropRect);
            Mat stretched = new Mat();
            Cv2.Resize(cropped, stretched, new Size(cropWidth * 5, image.Height), 0, 0, InterpolationFlags.Linear);

            return stretched;
        }

        public static List<string> DetectBarcodes(string imageFile, bool isLeft = true)
        {
            var results = new List<string>();
            using (Mat rawImg = Cv2.ImRead(imageFile))
            {
                if (rawImg.Empty())
                {
                    Console.WriteLine("  Failed to load image.");
                    return results;
                }

                var img = StretchSideRegion(rawImg, isLeft);
                Cv2.Rotate(img, img, RotateFlags.Rotate90Counterclockwise);

                double imgWidth = img.Width;
                double imgHeight = img.Height;
                int edgeWidth = (int)imgWidth;

                Mat debug = img.Clone();
                var allRects = new List<Rect>();

                Console.WriteLine($"  Image size: {imgWidth}x{imgHeight}");

                int x0 = isLeft ? 0 : (int)imgWidth - edgeWidth;
                var roiRect = new Rect(x0, 0, edgeWidth, (int)imgHeight);
                using (Mat roi = new Mat(img, roiRect))
                {
                    var found = DetectBarcodesInRoi(roi, isLeft ? "Left" : "Right", roiRect);
                    allRects = found;

                    var reader = new BarcodeReader();
                    reader.Options.TryHarder = true;
                    reader.Options.PossibleFormats = new[] { BarcodeFormat.CODE_128 };

                    foreach (var r in found)
                    {
                        var absRect = new Rect(r.X + x0, r.Y, r.Width, r.Height);
                        var color = isLeft ? new Scalar(0, 0, 255) : new Scalar(0, 255, 255);
                        Cv2.Rectangle(debug, absRect, color, 3);

                        using (Mat barcodeRoi = new Mat(img, absRect))
                        using (var bitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(barcodeRoi))
                        {
                            var decodeResult = reader.Decode(bitmap);
                            if (decodeResult != null)
                            {
                                results.Add(decodeResult.Text);
                                Console.WriteLine($"    DECODED: {decodeResult.Text}");
                            }
                        }
                    }
                }

                Cv2.PutText(debug, $"Detected: {allRects.Count}", new Point(10, 30),
                    HersheyFonts.HersheySimplex, 1, new Scalar(0, 0, 255), 2);
                Cv2.PutText(debug, $"Decoded: {results.Count}", new Point((int)imgWidth - 250, 30),
                    HersheyFonts.HersheySimplex, 1, new Scalar(0, 255, 255), 2);

                //Cv2.ImShow($"{(isLeft ? "Left" : "Right")} Detection Result", debug);

                Console.WriteLine($"  {(isLeft?"Left":"Right")} Barcodes : {allRects.Count}");
                foreach (var r in allRects)
                    Console.WriteLine($"    -> [{r.X},{r.Y}] {r.Width}x{r.Height}");

                debug.Dispose();
            }
            return results;
        }

        /// <summary>
        /// 元数据读取结果
        /// </summary>
        public class MetadataResult
        {
            public byte[] Metadata { get; set; }
            public string FileName { get; set; }
            public string Timestamp { get; set; }

            // 解析后的元数据含义
            public int MaxRows { get; set; }
            public int MaxCols { get; set; }
            public bool Colorful { get; set; }
            public int ColorDepth { get; set; }
            public int CurrentPage { get; set; }
            public int TotalPages { get; set; }
        }

        private static bool IsBase64String(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length % 4 != 0)
                return false;
            return s.All(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '+' || c == '/' || c == '=');
        }

        private static string ParseTimestamp(string base64Timestamp)
        {
            try
            {
                var bytes = Convert.FromBase64String(base64Timestamp);
                long unixSeconds;
                if (bytes.Length == 8)
                {
                    unixSeconds = BitConverter.ToInt64(bytes, 0);
                }
                else
                {
                    // 尝试直接解析为数字（兼容旧格式）
                    unixSeconds = long.Parse(Encoding.UTF8.GetString(bytes));
                }
                var dt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
                return dt.ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch
            {
                return base64Timestamp;
            }
        }

        /// <summary>
        /// 使用 DetectBarcodes 读取图像左右两侧条码，返回文件名文本、元数据、文件名、时间戳
        /// </summary>
        public static MetadataResult ReadMetadata(string imageFile)
        {
            var result = new MetadataResult();

            // 左侧：元数据条码（$ 开头，Base64 编码）
            var leftCodes = DetectBarcodes(imageFile, isLeft: true);
            foreach (var code in leftCodes)
            {
                if (code.StartsWith("$"))
                {
                    try
                    {
                        var base64Part = code.TrimStart('$').TrimStart('-');
                        result.Metadata = Convert.FromBase64String(base64Part);

                        if (result.Metadata != null && result.Metadata.Length >= 4)
                        {
                            result.MaxRows = (result.Metadata[0] >> 4) & 0x0F;
                            result.MaxCols = result.Metadata[0] & 0x0F;
                            result.Colorful = (result.Metadata[1] & 0x80) != 0;
                            result.ColorDepth = result.Metadata[1] & 0x7F;
                            result.CurrentPage = result.Metadata[2];
                            result.TotalPages = result.Metadata[3];
                        }
                    }
                    catch { }
                }
            }

            // 右侧：文件名条码、时间戳条码（Base64）
            var rightCodes = DetectBarcodes(imageFile, isLeft: false);
            if (rightCodes.Count >= 2)
            {
                var ordered = rightCodes.OrderByDescending(c => c.Length).ToList();
                var candidateTimestamp = ordered.FirstOrDefault(IsBase64String);
                if (candidateTimestamp != null)
                {
                    result.Timestamp = ParseTimestamp(candidateTimestamp);
                    result.FileName = ordered.First(c => c != candidateTimestamp);
                }
                else
                {
                    result.Timestamp = ParseTimestamp(ordered[0]);
                    result.FileName = ordered[1];
                }
            }
            else if (rightCodes.Count == 1)
            {
                result.FileName = rightCodes[0];
            }

            return result;
        }

        public static List<Rect> MergeVerticallyAlignedRects(List<Rect> rects, int yGapThreshold)
        {
            if (rects.Count == 0) return rects;

            var sorted = rects.OrderBy(r => r.Y).ToList();
            var merged = new List<Rect>();
            var current = sorted[0];

            for (int i = 1; i < sorted.Count; i++)
            {
                var next = sorted[i];
                int xOverlap = Math.Min(current.X + current.Width, next.X + next.Width) - Math.Max(current.X, next.X);
                int minWidth = Math.Min(current.Width, next.Width);
                int yGap = next.Y - (current.Y + current.Height);

                if (xOverlap > minWidth * 0.4 && yGap <= yGapThreshold)
                {
                    int newX = Math.Min(current.X, next.X);
                    int newY = Math.Min(current.Y, next.Y);
                    int newRight = Math.Max(current.X + current.Width, next.X + next.Width);
                    int newBottom = Math.Max(current.Y + current.Height, next.Y + next.Height);
                    current = new Rect(newX, newY, newRight - newX, newBottom - newY);
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

        public static List<Rect> DetectBarcodesInRoi(Mat roi, string label, Rect roiOffset)
        {
            var result = new List<Rect>();

            using (Mat gray = new Mat())
            {
                Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);
                //Cv2.ImShow($"{label} - 1 Gray", gray);

                using (Mat gradX = new Mat())
                {
                    Cv2.Sobel(gray, gradX, MatType.CV_32F, 1, 0, ksize: 3);
                    Cv2.ConvertScaleAbs(gradX, gradX);
                    //Cv2.ImShow($"{label} - 2 Sobel X", gradX);

                    using (Mat binary = new Mat())
                    {
                        Cv2.Threshold(gradX, binary, 25, 255, ThresholdTypes.Binary);
                        //Cv2.ImShow($"{label} - 3 Binary", binary);

                        using (Mat closed = new Mat())
                        using (Mat kernelClose = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(35, 1)))
                        {
                            Cv2.MorphologyEx(binary, closed, MorphTypes.Close, kernelClose);
                            //Cv2.ImShow($"{label} - 4 Close", closed);

                            using (Mat dilated = new Mat())
                            using (Mat eroded = new Mat())
                            using (Mat kernelSmall = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3)))
                            {
                                Cv2.Dilate(closed, dilated, kernelSmall, null, 1);
                                Cv2.Erode(dilated, eroded, kernelSmall, null, 1);
                                //Cv2.ImShow($"{label} - 5 OpenClose", eroded);

                                Cv2.FindContours(eroded, out Point[][] contours, out HierarchyIndex[] hierarchy,
                                    RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                                Console.WriteLine($"    {label} contours: {contours.Length}");

                                using (Mat debugRoi = roi.Clone())
                                {
                                    var rawRects = new List<Rect>();
                                    foreach (var c in contours)
                                    {
                                        Rect r = Cv2.BoundingRect(c);
                                        double ratio = r.Height / (double)r.Width;
                                        double area = r.Width * r.Height;

                                        Cv2.Rectangle(debugRoi, r, new Scalar(0, 255, 0), 1);

                                        if (area > 100)
                                        {
                                            Console.WriteLine($"      rect [{r.X},{r.Y}] {r.Width}x{r.Height} ratio={ratio:F2} area={area}");
                                        }

                                        if (ratio < 0.4 && r.Height > 25 && area > 300)
                                        {
                                            rawRects.Add(r);
                                        }
                                    }

                                    //Cv2.ImShow($"{label} - 6 All Rects", debugRoi);
                                    Console.WriteLine($"    {label} rawRects after filter: {rawRects.Count}");
                                    var merged = MergeVerticallyAlignedRects(rawRects, yGapThreshold: 5);
                                    Console.WriteLine($"    {label} merged count: {merged.Count}");
                                    result.AddRange(merged);
                                }
                            }
                        }
                    }
                }
            }
            return result;
        }
    }
}