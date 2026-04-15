using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using ZXing;
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;
using Size = OpenCvSharp.Size;

namespace screen_file_receiver
{
    public static class DataMatrixReader
    {
        private static string rcString = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        private static readonly Encoding Iso88591 = Encoding.GetEncoding("ISO-8859-1");

        /// <summary>
        /// 从图片读取文件
        /// </summary>
        /// <param name="fileName">图片文件路径</param>
        /// <param name="fileStream">输出流</param>
        /// <param name="outputFileName">解析出的原始文件名（如果存在）</param>
        /// <returns>是否成功</returns>
        public static bool ReadToFile(string fileName, Stream fileStream, bool debug)
        {
            var decodeResult = DecodeImageWithMetadata(fileName, debug);
            var meta = decodeResult.Metadata;
            var dataBlocks = decodeResult.DataBlocks;

            if (meta?.Metadata == null || meta.Metadata.Length < 4)
            {
                MessageBox.Show("找不到信息或元数据格式错误: " + fileName);
                return false;
            }

            if (dataBlocks == null || dataBlocks.Count == 0)
            {
                MessageBox.Show("未解析到任何数据块: " + fileName);
                return false;
            }

            var currentPage = meta.CurrentPage;
            var totalPages = meta.TotalPages;
            var outputFileName = meta.FileName;
            long offset = fileStream.Position;

            var final = dataBlocks.OrderBy(c => c.row).ThenBy(c => c.col).ToList();

            fileStream.Position = offset;
            foreach (var block in final)
            {
                var bytes = block.data;
                fileStream.Write(bytes, 0, bytes.Length);
            }

            return true;
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
            Cv2.Resize(cropped, stretched, new Size(cropWidth * 3, image.Height), 0, 0, InterpolationFlags.Linear);

            return stretched;
        }

        public static List<string> DetectBarcodes(string imageFile, bool isLeft = true, bool debug = false)
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

                Mat debugImg = img.Clone();
                var allRects = new List<Rect>();

                Console.WriteLine($"  Image size: {imgWidth}x{imgHeight}");

                int x0 = isLeft ? 0 : (int)imgWidth - edgeWidth;
                var roiRect = new Rect(x0, 0, edgeWidth, (int)imgHeight);
                using (Mat roi = new Mat(img, roiRect))
                {
                    var found = DetectBarcodesInRoi(roi, isLeft ? "Left" : "Right", roiRect, debug);
                    allRects = found;

                    var reader = new BarcodeReader();
                    reader.Options.TryHarder = true;
                    reader.Options.PossibleFormats = new[] { BarcodeFormat.CODE_128 };

                    foreach (var r in found)
                    {
                        var absRect = new Rect(r.X + x0, r.Y, r.Width, r.Height);
                        var color = isLeft ? new Scalar(0, 0, 255) : new Scalar(0, 255, 255);
                        Cv2.Rectangle(debugImg, absRect, color, 3);

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

                Cv2.PutText(debugImg, $"Detected: {allRects.Count}", new Point(10, 30),
                    HersheyFonts.HersheySimplex, 1, new Scalar(0, 0, 255), 2);
                Cv2.PutText(debugImg, $"Decoded: {results.Count}", new Point((int)imgWidth - 250, 30),
                    HersheyFonts.HersheySimplex, 1, new Scalar(0, 255, 255), 2);

                Console.WriteLine($"  {(isLeft ? "Left" : "Right")} Barcodes : {allRects.Count}");
                foreach (var r in allRects)
                    Console.WriteLine($"    -> [{r.X},{r.Y}] {r.Width}x{r.Height}");
                if (debug)
                {
                    Cv2.ImShow($"{(isLeft ? "Left" : "Right")} Detection Result", debugImg);
                    Cv2.WaitKey();
                }

                debugImg.Dispose();
            }
            return results;
        }

        /// <summary>
        /// 图像解码结果
        /// </summary>
        public class DecodeResult
        {
            public List<(int row, int col, byte[] data)> DataBlocks { get; set; } = new List<(int row, int col, byte[] data)>();
            public MetadataResult Metadata { get; set; }
        }

        /// <summary>
        /// 元数据读取结果
        /// </summary>
        public class MetadataResult
        {
            public byte[] Metadata { get; set; }
            public string FileName { get; set; }
            public string FileId { get; set; }

            // 解析后的元数据含义
            public int MaxRows { get; set; }

            public int MaxCols { get; set; }
            public bool Colorful { get; set; }
            public bool HasPassword { get; set; }
            public int ColorDepth { get; set; }
            public int CurrentPage { get; set; }
            public int TotalPages { get; set; }
        }

        /// <summary>
        /// 从图片解码所有数据块（包含元数据）
        /// </summary>
        public static DecodeResult DecodeImageWithMetadata(string imageFile, bool debug)
        {
            var result = new DecodeResult();

            using (Mat image = Cv2.ImRead(imageFile))
            {
                if (image.Empty())
                {
                    throw new Exception($"加载图片失败: {imageFile}");
                }

                result.Metadata = ReadMetadata(imageFile, debug);

                var dataMatrixContours = FindDataMatrixContours(image);

                bool isColorful = result.Metadata?.Colorful ?? false;
                if (isColorful)
                {
                    result.DataBlocks = DecodeColorfulMode(image, dataMatrixContours);
                }
                else
                {
                    result.DataBlocks = DecodeGrayscaleMode(image, dataMatrixContours);
                }
            }

            return result;
        }

        ///// <summary>
        ///// 从图片解码所有数据块（向后兼容的简化版本）
        ///// </summary>
        //public static List<(int row, int col, byte[] data)> DecodeImage(string imageFile)
        //{
        //    var result = DecodeImageWithMetadata(imageFile);
        //    return result.DataBlocks;
        //}

        /// <summary>
        /// 查找所有 DataMatrix 二维码轮廓
        /// </summary>
        private static List<Rect> FindDataMatrixContours(Mat image)
        {
            using (Mat gray = new Mat())
            using (Mat thresh = new Mat())
            {
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
                Cv2.Threshold(gray, thresh, 127, 255, ThresholdTypes.BinaryInv);

                Cv2.FindContours(thresh, out Point[][] contours, out HierarchyIndex[] hierarchy,
                    RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                var validContours = contours
                    .Where(c => Cv2.ContourArea(c) > 100)
                    .Select(c => Cv2.BoundingRect(c))
                    .Where(r => r.Height > 15 && r.Width > 15 &&
                               Math.Abs(r.Height - r.Width) < Math.Max(r.Height, r.Width) * 0.3)
                    .ToList();

                if (validContours.Count == 0) return new List<Rect>();

                var avgArea = validContours.Average(r => r.Width * r.Height);
                return validContours
                    .Where(r => r.Width * r.Height > avgArea * 0.2)
                    .OrderBy(r => r.Y)
                    .ThenBy(r => r.X)
                    .ToList();
            }
        }

        /// <summary>
        /// 黑白模式解码
        /// </summary>
        private static List<(int row, int col, byte[] data)> DecodeGrayscaleMode(Mat image, List<Rect> contours)
        {
            var results = new List<(int row, int col, byte[] data)>();
            var reader = new BarcodeReader();
            reader.Options.TryHarder = true;
            reader.Options.PossibleFormats = new[] { BarcodeFormat.DATA_MATRIX };

            foreach (var rect in contours)
            {
                var decoded = DecodeDataMatrixAt(image, rect, reader);
                if (decoded != null)
                {
                    results.Add(decoded.Value);
                }
            }

            return results;
        }

        /// <summary>
        /// 彩色模式解码 - 分离 R/G/B 通道分别解码
        /// </summary>
        private static List<(int row, int col, byte[] data)> DecodeColorfulMode(Mat image, List<Rect> contours)
        {
            var results = new List<(int row, int col, byte[] data)>();
            var reader = new BarcodeReader();
            reader.Options.TryHarder = true;
            reader.Options.PossibleFormats = new[] { BarcodeFormat.DATA_MATRIX };

            Mat[] channels = new Mat[3];
            Cv2.Split(image, out channels);

            // OpenCV 分割顺序为 BGR，需重排为 RGB 以匹配发送端 (red, green, blue)
            var rgbChannels = new[] { channels[2], channels[1], channels[0] };

            foreach (var rect in contours)
            {
                var blockResults = new List<(int layer, int row, int col, byte[] data)>();

                for (int layer = 0; layer < 3; layer++)
                {
                    var decoded = DecodeDataMatrixFromChannel(rgbChannels[layer], rect, reader, layer);
                    if (decoded != null)
                    {
                        blockResults.Add((layer, decoded.Value.row, decoded.Value.col, decoded.Value.data));
                    }
                }

                if (blockResults.Count > 0)
                {
                    var merged = MergeLayerData(blockResults);
                    results.Add(merged);
                }
            }

            foreach (var ch in channels) ch.Dispose();

            return results;
        }

        /// <summary>
        /// 从单通道解码 DataMatrix
        /// </summary>
        private static (int row, int col, byte[] data)? DecodeDataMatrixFromChannel(
            Mat channel, Rect rect, BarcodeReader reader, int layer)
        {
            int padding = 5;
            int x = Math.Max(0, rect.X - padding);
            int y = Math.Max(0, rect.Y - padding);
            int w = Math.Min(channel.Width - x, rect.Width + 2 * padding);
            int h = Math.Min(channel.Height - y, rect.Height + 2 * padding);

            using (Mat roi = new Mat(channel, new Rect(x, y, w, h)))
            {
                using (Mat binary = new Mat())
                {
                    int threshold = 127;
                    Cv2.Threshold(roi, binary, threshold, 255, ThresholdTypes.Binary);

                    using (Mat bgr = new Mat())
                    {
                        Cv2.CvtColor(binary, bgr, ColorConversionCodes.GRAY2BGR);
                        string decoded = TryDecodeDataMatrix(bgr, reader);

                        if (!string.IsNullOrEmpty(decoded) && decoded.Length >= 2)
                        {
                            int row = rcString.IndexOf(decoded[0]);
                            int col = rcString.IndexOf(decoded[1]);

                            if (row >= 0 && col >= 0)
                            {
                                string payload = decoded.Substring(2);
                                byte[] bytes = Iso88591.GetBytes(payload);
                                return (row, col, bytes);
                            }
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 在指定位置解码 DataMatrix（用于黑白模式）
        /// </summary>
        private static (int row, int col, byte[] data)? DecodeDataMatrixAt(Mat image, Rect rect, BarcodeReader reader)
        {
            int padding = 5;
            int x = Math.Max(0, rect.X - padding);
            int y = Math.Max(0, rect.Y - padding);
            int w = Math.Min(image.Width - x, rect.Width + 2 * padding);
            int h = Math.Min(image.Height - y, rect.Height + 2 * padding);

            using (Mat roi = new Mat(image, new Rect(x, y, w, h)))
            {
                string decoded = TryDecodeDataMatrix(roi, reader);

                if (!string.IsNullOrEmpty(decoded) && decoded.Length >= 2)
                {
                    int row = rcString.IndexOf(decoded[0]);
                    int col = rcString.IndexOf(decoded[1]);

                    if (row >= 0 && col >= 0)
                    {
                        string payload = decoded.Substring(2);
                        byte[] bytes = Iso88591.GetBytes(payload);
                        return (row, col, bytes);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 合并多层数据
        /// </summary>
        private static (int row, int col, byte[] data) MergeLayerData(List<(int layer, int row, int col, byte[] data)> layerResults)
        {
            if (layerResults.Count == 1)
            {
                var r = layerResults[0];
                return (r.row, r.col, r.data);
            }

            var sorted = layerResults.OrderBy(l => l.layer).ToList();
            var first = sorted[0];

            using (var ms = new MemoryStream())
            {
                foreach (var layer in sorted)
                {
                    ms.Write(layer.data, 0, layer.data.Length);
                }
                return (first.row, first.col, ms.ToArray());
            }
        }

        /// <summary>
        /// 尝试解码 DataMatrix
        /// </summary>
        private static string TryDecodeDataMatrix(Mat image, BarcodeReader reader, int retryCount = 3)
        {
            for (int attempt = 0; attempt <= retryCount; attempt++)
            {
                using (Mat processed = PreprocessForDecode(image, attempt))
                using (var bitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(processed))
                {
                    var result = reader.Decode(bitmap);

                    if (result != null && result.BarcodeFormat == BarcodeFormat.DATA_MATRIX)
                    {
                        return result.Text;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 解码预处理
        /// </summary>
        private static Mat PreprocessForDecode(Mat image, int attempt)
        {
            if (attempt == 0)
            {
                return image.Clone();
            }

            Mat result = new Mat();

            if (attempt == 1)
            {
                Cv2.Resize(image, result, new Size(), 2.0, 2.0, InterpolationFlags.Linear);
            }
            else if (attempt == 2)
            {
                using (Mat gray = new Mat())
                {
                    if (image.Channels() == 3)
                        Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
                    else
                        image.CopyTo(gray);

                    Cv2.AdaptiveThreshold(gray, result, 255, AdaptiveThresholdTypes.GaussianC,
                        ThresholdTypes.Binary, 11, 2);
                }
            }
            else
            {
                Mat kernel = Mat.FromArray(new float[,]
                {
                    { -1, -1, -1 },
                    { -1, 9, -1 },
                    { -1, -1, -1 }
                });
                Cv2.Filter2D(image, result, -1, kernel);
            }

            return result;
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
        public static MetadataResult ReadMetadata(string imageFile, bool debug = false)
        {
            var result = new MetadataResult();

            // 左侧：元数据条码（$ 开头，Base64 编码）
            var leftCodes = DetectBarcodes(imageFile, isLeft: true, debug);
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
                            result.HasPassword = (result.Metadata[1] & 0x40) != 0;
                            result.ColorDepth = result.Metadata[1] & 0x3F;
                            result.CurrentPage = result.Metadata[2];
                            result.TotalPages = result.Metadata[3];
                        }
                    }
                    catch { }
                }
            }

            // 右侧：文件名条码、时间戳条码（Base64）
            var rightCodes = DetectBarcodes(imageFile, isLeft: false, debug);
            if (rightCodes.Count >= 2)
            {
                var ordered = rightCodes.OrderByDescending(c => c.Length).ToList();
                var candidateFileId = ordered.FirstOrDefault(c => c.StartsWith("#"));// ordered.FirstOrDefault(IsBase64String);
                if (candidateFileId != null)
                {
                    result.FileId = candidateFileId.TrimStart('#');// ParseTimestamp(candidateTimestamp);
                    result.FileName = ordered.First(c => c != candidateFileId);
                }
                else
                {
                    result.FileId = ordered[0].TrimStart('#');// ParseTimestamp(ordered[0]);
                    result.FileName = ordered[1];
                }
            }
            else if (rightCodes.Count == 1)
            {
                result.FileName = rightCodes[0].TrimStart('#');
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

        public static List<Rect> DetectBarcodesInRoi(Mat roi, string label, Rect roiOffset, bool debug)
        {
            var result = new List<Rect>();

            using (Mat gray = new Mat())
            {
                Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);

                if (debug) Cv2.ImShow($"{label} - 1 Gray", gray);

                using (Mat gradX = new Mat())
                {
                    Cv2.Sobel(gray, gradX, MatType.CV_32F, 1, 0, ksize: 3);
                    Cv2.ConvertScaleAbs(gradX, gradX);

                    if (debug) Cv2.ImShow($"{label} - 2 Sobel X", gradX);

                    using (Mat binary = new Mat())
                    {
                        Cv2.Threshold(gradX, binary, 64, 255, ThresholdTypes.Binary);
                        if (debug)
                            Cv2.ImShow($"{label} - 3 Binary", binary);

                        using (Mat closed = new Mat())
                        using (Mat kernelClose = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(35, 1)))
                        {
                            Cv2.MorphologyEx(binary, closed, MorphTypes.Close, kernelClose);

                            if (debug) Cv2.ImShow($"{label} - 4 Close", closed);

                            using (Mat dilated = new Mat())
                            using (Mat eroded = new Mat())
                            using (Mat kernelSmall = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3)))
                            {
                                Cv2.Dilate(closed, dilated, kernelSmall, null, 1);
                                Cv2.Erode(dilated, eroded, kernelSmall, null, 1);
                                if (debug)
                                    Cv2.ImShow($"{label} - 5 OpenClose", eroded);

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

                                        if (ratio < 0.5 && r.Height > 25 && area > 300)
                                        {
                                            rawRects.Add(r);
                                        }
                                    }
                                    if (debug)
                                        Cv2.ImShow($"{label} - 6 All Rects", debugRoi);
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