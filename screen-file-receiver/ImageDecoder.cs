using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using Witteborn.ReedSolomon;
using ZXing;
using ZXing.Common;
using ZXing.Datamatrix;
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;
using Size = OpenCvSharp.Size;

namespace screen_file_transmit
{ 

    public static class ImageDecoder
    {
        private static readonly string rcString = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        private static readonly Encoding Iso88591 = Encoding.GetEncoding("ISO-8859-1");
        private static readonly DataMatrixReader reader = new DataMatrixReader();

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
                throw new Exception("找不到信息或元数据格式错误: " + fileName);
            }

            if (dataBlocks == null || dataBlocks.Count == 0)
            {
                throw new Exception("未解析到任何数据块: " + fileName);
            }

            if (meta.TotalQrCodeCount > 0)
            {
                int actualQrCount = decodeResult.DecodedQrCodeCount;
                if (actualQrCount < meta.TotalQrCodeCount * 0.8)
                {
                    throw new Exception($"二维码数量校验失败: 期望 {meta.TotalQrCodeCount} 个，实际解析到 {actualQrCount} 个");
                }
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
                if (bytes.Length < 2)
                {
                    fileStream.Write(bytes, 0, bytes.Length);
                    continue;
                }

                // 前 2 字节为长度前缀（大端序），表示原始数据长度（不含 CRC32）
                int payloadLength = (bytes[0] << 8) | bytes[1];
                int cellDataLength = payloadLength + 4; // 原始数据 + CRC32
                int availableLength = bytes.Length - 2;
                if (cellDataLength > availableLength)
                    cellDataLength = availableLength;

                if (cellDataLength < 4)
                {
                    fileStream.Write(bytes, 0, bytes.Length);
                    continue;
                }

                var cellData = new byte[cellDataLength];
                Array.Copy(bytes, 2, cellData, 0, cellDataLength);

                var data = new byte[payloadLength];
                Array.Copy(cellData, 0, data, 0, payloadLength);
                var crcBytes = new byte[4];
                Array.Copy(cellData, payloadLength, crcBytes, 0, 4);
                var computedCrc = Crc32.ComputeHash(data);
                if (!crcBytes.SequenceEqual(computedCrc))
                {
                    throw new Exception($"CRC32 校验失败: 数据块 ({block.row}, {block.col})");
                }
                fileStream.Write(data, 0, data.Length);
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
            using (Mat rawImg = Cv2.ImRead(imageFile))
            {
                if (rawImg.Empty())
                {
                    Console.WriteLine("  Failed to load image.");
                    return new List<string>();
                }
                return DetectBarcodes(rawImg, isLeft, debug);
            }
        }

        public static List<string> DetectBarcodes(Mat rawImg, bool isLeft = true, bool debug = false)
        {
            var results = new List<string>();

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
                var found = DetectBarcodesInRoi(roi, isLeft ? "Left" : "Right", roiRect, false);
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
                if ((isLeft && results.Count < 1) || (!isLeft && results.Count < 2))
                {
                    using (Mat roi = new Mat(img, roiRect))
                        DetectBarcodesInRoi(roi, isLeft ? "Left" : "Right", roiRect, true);
                    Cv2.ImShow($"{(isLeft ? "Left" : "Right")} Detection Result", debugImg);
                    Cv2.WaitKey();
                }
            }

            debugImg.Dispose();

            return results;
        }

        /// <summary>
        /// 图像解码结果
        /// </summary>
        public class DecodeResult
        {
            public List<(int row, int col, byte[] data)> DataBlocks { get; set; } = new List<(int row, int col, byte[] data)>();
            public MetadataResult Metadata { get; set; }
            public int DecodedQrCodeCount { get; set; }
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
            public bool HasErrorCorrection { get; set; }
            public int ErrorCorrectionPercent { get; set; }
            public int ColorDepth { get; set; }
            public int CurrentPage { get; set; }
            public int TotalPages { get; set; }
            public int TotalQrCodeCount { get; set; }
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
                    result.DataBlocks = DecodeColorfulMode(image, dataMatrixContours, debug, out int decodedCount);
                    result.DecodedQrCodeCount = decodedCount;
                }
                else
                {
                    result.DataBlocks = DecodeGrayscaleMode(image, dataMatrixContours, debug);
                    result.DecodedQrCodeCount = result.DataBlocks.Count;
                }

                if (result.Metadata?.HasErrorCorrection == true && result.DataBlocks != null && result.DataBlocks.Count > 0)
                {
                    result.DataBlocks = DecodeWithReedSolomon(result.DataBlocks, result.Metadata);
                }
            }

            return result;
        }

        private static (int dataShards, int parityShards) GetRsShardCounts(int errorCorrectionPercent)
        {
            switch (errorCorrectionPercent)
            {
                case 5: return (19, 1);
                case 10: return (9, 1);
                case 15: return (17, 3);
                case 20: return (4, 1);
                case 25: return (3, 1);
                case 30: return (7, 3);
                case 35: return (13, 7);
                case 40: return (3, 2);
                case 45: return (11, 9);
                case 50: return (1, 1);
                default: return (0, 0);
            }
        }

        /// <summary>
        /// 对每个 DataMatrix 数据块进行 Reed-Solomon 解码
        /// </summary>
        private static List<(int row, int col, byte[] data)> DecodeWithReedSolomon(List<(int row, int col, byte[] data)> dataBlocks, MetadataResult meta)
        {
            var (dataShards, parityShards) = GetRsShardCounts(meta.ErrorCorrectionPercent);
            int totalShards = dataShards + parityShards;
            if (dataShards <= 0 || parityShards <= 0) return dataBlocks;

            var result = new List<(int row, int col, byte[] data)>();
            foreach (var block in dataBlocks)
            {
                if (block.data.Length < 2)
                {
                    result.Add(block);
                    continue;
                }

                // 分离长度前缀和 RS 编码数据
                byte[] lenPrefix = new byte[2];
                Array.Copy(block.data, 0, lenPrefix, 0, 2);
                int rsDataLength = block.data.Length - 2;

                if (rsDataLength % totalShards != 0)
                {
                    result.Add(block);
                    continue;
                }

                int shardSize = rsDataLength / totalShards;
                byte[][] shards = new byte[totalShards][];
                bool[] present = new bool[totalShards];
                for (int i = 0; i < totalShards; i++)
                {
                    shards[i] = new byte[shardSize];
                    Array.Copy(block.data, 2 + i * shardSize, shards[i], 0, shardSize);
                    present[i] = true;
                }

                try
                {
                    var rs = new ReedSolomon(dataShards, parityShards);
                    rs.DecodeMissing(shards, present, 0, shardSize);
                    using (var ms = new MemoryStream())
                    {
                        ms.Write(lenPrefix, 0, 2); // 重新附加长度前缀
                        for (int i = 0; i < dataShards; i++)
                        {
                            ms.Write(shards[i], 0, shardSize);
                        }
                        result.Add((block.row, block.col, ms.ToArray()));
                    }
                }
                catch
                {
                    result.Add(block);
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
        private static List<(int row, int col, byte[] data)> DecodeGrayscaleMode(Mat image, List<Rect> contours, bool debug = false)
        {
            var results = new List<(int row, int col, byte[] data)>(); 
            //reader.Options.TryHarder = true;
            //reader.Options.PossibleFormats = new[] { BarcodeFormat.DATA_MATRIX };

            foreach (var rect in contours)
            {
                var decoded = DecodeDataMatrixAt(image, rect,  debug);
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
        private static List<(int row, int col, byte[] data)> DecodeColorfulMode(Mat image, List<Rect> contours,bool debug, out int decodedQrCodeCount)
        {
            var results = new List<(int row, int col, byte[] data)>();
            var reader = new DataMatrixReader();
            //reader.Options.TryHarder = true;
            //reader.Options.PossibleFormats = new[] { BarcodeFormat.DATA_MATRIX };

            Mat[] channels = new Mat[3];
            Cv2.Split(image, out channels);

            // OpenCV 分割顺序为 BGR，需重排为 RGB 以匹配发送端 (red, green, blue)
            var rgbChannels = new[] { channels[2], channels[1], channels[0] };
            decodedQrCodeCount = 0;

            foreach (var rect in contours)
            {
                var blockResults = new List<(int layer, int row, int col, byte[] data)>();

                for (int layer = 0; layer < 3; layer++)
                {
                    var decoded = DecodeDataMatrixFromChannel(rgbChannels[layer], rect, reader, layer, debug);
                    if (decoded != null)
                    {
                        blockResults.Add((layer, decoded.Value.row, decoded.Value.col, decoded.Value.data));
                    }
                }

                if (blockResults.Count > 0)
                {
                    decodedQrCodeCount += blockResults.Count;
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
            Mat channel, Rect rect, DataMatrixReader reader, int layer, bool debug= false)
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
                        string decoded = TryDecodeDataMatrix(bgr);

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
        private static (int row, int col, byte[] data)? DecodeDataMatrixAt(Mat image, Rect rect,  bool debug )
        {
            int padding = 5;
            int x = Math.Max(0, rect.X - padding);
            int y = Math.Max(0, rect.Y - padding);
            int w = Math.Min(image.Width - x, rect.Width + 2 * padding);
            int h = Math.Min(image.Height - y, rect.Height + 2 * padding);

            using (Mat roi = new Mat(image, new Rect(x, y, w, h)))
            {
                string decoded = TryDecodeDataMatrix(roi, debug);

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
            var sorted = layerResults.OrderBy(l => l.layer).ToList();
            var first = sorted[0];

            using (var ms = new MemoryStream())
            {
                // 从第一层提取长度前缀
                if (first.data.Length >= 2)
                {
                    ms.Write(first.data, 0, 2);
                }

                // 每层只拼接数据部分（去掉长度前缀）
                foreach (var layer in sorted)
                {
                    if (layer.data.Length > 2)
                    {
                        ms.Write(layer.data, 2, layer.data.Length - 2);
                    }
                    else if (layer.data.Length > 0)
                    {
                        ms.Write(layer.data, 0, layer.data.Length);
                    }
                }
                return (first.row, first.col, ms.ToArray());
            }
        }

        /// <summary>
        /// 尝试解码 DataMatrix
        /// </summary>
        public static string TryDecodeDataMatrix(Mat image, bool debug = false, int retryCount = 10)
        {
            for (int attempt = 0; attempt <= retryCount; attempt++)
            {
                using (Mat processed = PreprocessForDecode(image, attempt))
                {
                    using (var bitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(processed))
                    {
                        var source = new BitmapLuminanceSource(bitmap);
                        var binarizer = new HybridBinarizer(source);
                        var binaryBitmap = new BinaryBitmap(binarizer);
                        var hints = new Dictionary<DecodeHintType, object>
                        {
                            { DecodeHintType.CHARACTER_SET, "ISO-8859-1" },
                            { DecodeHintType.TRY_HARDER, true }
                        };
                        var result = reader.decode(binaryBitmap, hints); 

                        if (result != null && result.BarcodeFormat == BarcodeFormat.DATA_MATRIX)
                        {
                            return result.Text;
                        }
                    }
                }
            }

            if (debug)
            {
                image.SaveImage(Directory.GetCurrentDirectory() + "/dm.png");
                for (int attempt = 0; attempt <= retryCount; attempt++)
                {
                    using (Mat processed = PreprocessForDecode(image, attempt))
                    {
                        processed.SaveImage(Directory.GetCurrentDirectory() + $"/dm_{attempt}.png");
                    }
                }
                Cv2.ImShow("DM", image);
                Cv2.WaitKey();
            }
            return null;
        }

        public static void SaveBinaryBitmapAsImage(BinaryBitmap binaryBitmap, string filePath)
        {
            try
            {
                if (binaryBitmap == null)
                {
                    Console.WriteLine("SaveBinaryBitmapAsImage: binaryBitmap is null");
                    return;
                }

                // 获取内部的二值化位图 - 这里可能抛出异常
                var bitMatrix = binaryBitmap.BlackMatrix;

                if (bitMatrix == null)
                {
                    Console.WriteLine("SaveBinaryBitmapAsImage: BlackMatrix is null");
                    return;
                }

                int width = bitMatrix.Width;
                int height = bitMatrix.Height;

                if (width <= 0 || height <= 0)
                {
                    Console.WriteLine($"SaveBinaryBitmapAsImage: Invalid dimensions {width}x{height}");
                    return;
                }

                using (var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format1bppIndexed))
                {
                    // 设置调色板
                    var palette = bitmap.Palette;
                    palette.Entries[0] = Color.Black;
                    palette.Entries[1] = Color.White;
                    bitmap.Palette = palette;

                    // 写入像素数据 - 注意 Format1bppIndexed 需要特殊处理
                    // 方案A：使用 LockBits 高效写入
                    var data = bitmap.LockBits(
                        new Rectangle(0, 0, width, height),
                        System.Drawing.Imaging.ImageLockMode.WriteOnly,
                        System.Drawing.Imaging.PixelFormat.Format1bppIndexed);

                    try
                    {
                        byte[] bytes = new byte[data.Stride * height];
                        for (int y = 0; y < height; y++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                if (bitMatrix[x, y]) // true = 黑
                                {
                                    // 设置对应位为 0（黑色）
                                }
                                else
                                {
                                    // 设置对应位为 1（白色）
                                    int byteIndex = y * data.Stride + (x >> 3);
                                    int bitIndex = 7 - (x & 7);
                                    bytes[byteIndex] |= (byte)(1 << bitIndex);
                                }
                            }
                        }
                        System.Runtime.InteropServices.Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
                    }
                    finally
                    {
                        bitmap.UnlockBits(data);
                    }

                    bitmap.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
                    Console.WriteLine($"Saved binary bitmap to {filePath}, size={width}x{height}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SaveBinaryBitmapAsImage failed: {ex.Message}");
                // 不重新抛出异常，避免影响主流程
            }
        }
        /// <summary>
        /// 解码预处理
        /// </summary> 

        private static Mat PreprocessForDecode(Mat image, int attempt)
        {
            // === 1. 统一先放大 ===
            Mat scaled = new Mat();

            // 根据 attempt 控制放大倍数（逐步增强）
            double scale = attempt switch
            {
                0 => 1,
                1 => 2,
                2 => 3,
                _ => 3
            };

            Cv2.Resize(image, scaled, new Size(), scale, scale, InterpolationFlags.Cubic);

            // === 2. 灰度 ===
            Mat gray = new Mat();
            if (scaled.Channels() == 3)
                Cv2.CvtColor(scaled, gray, ColorConversionCodes.BGR2GRAY);
            else
                gray = scaled.Clone();

            Mat result = new Mat();

            // === 3. 多策略处理 ===
            switch (attempt)
            {
                case 0:
                case 1:
                case 2: 
                    result = gray.Clone();
                    break;

                case 3:
                    // OTSU
                    Cv2.Threshold(gray, result, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
                    break;

                case 4:
                    // 自适应阈值
                    Cv2.AdaptiveThreshold(gray, result, 255,
                        AdaptiveThresholdTypes.GaussianC,
                        ThresholdTypes.Binary,
                        11, 2);
                    break;

                case 5:
                    // 高斯模糊 + OTSU
                    using (var blur = new Mat())
                    {
                        Cv2.GaussianBlur(gray, blur, new Size(5, 5), 0);
                        Cv2.Threshold(blur, result, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
                    }
                    break;

                case 6:
                    // 中值滤波 + 自适应
                    using (var median = new Mat())
                    {
                        Cv2.MedianBlur(gray, median, 5);
                        Cv2.AdaptiveThreshold(median, result, 255,
                            AdaptiveThresholdTypes.MeanC,
                            ThresholdTypes.Binary,
                            11, 2);
                    }
                    break;

                case 7:
                    // 形态学开（去噪）
                    using (var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3)))
                    {
                        Cv2.MorphologyEx(gray, result, MorphTypes.Open, kernel);
                    }
                    break;

                case 8:
                    // 形态学闭（补断裂）
                    using (var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3)))
                    {
                        Cv2.MorphologyEx(gray, result, MorphTypes.Close, kernel);
                    }
                    break;

                case 9:
                    // 锐化
                    using (Mat kernelSharpen = Mat.FromArray(new float[,]
                    {
                                { -1, -1, -1 },
                                { -1, 9, -1 },
                                { -1, -1, -1 }
                    }))
                    {
                        Cv2.Filter2D(gray, result, -1, kernelSharpen);
                    }
                    break;

                case 10:
                    // 反色 + OTSU（应对反色二维码）
                    using (var binary = new Mat())
                    {
                        Cv2.Threshold(gray, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
                        Cv2.BitwiseNot(binary, result);
                    }
                    break;

                default:
                    result = gray.Clone();
                    break;
            }

            return result;
        }
    //private static Mat PreprocessForDecode(Mat image, int attempt)
    //{
    //    if (attempt == 0)
    //    {
    //        return image.Clone();
    //    }

    //    Mat result = new Mat();

    //    if (attempt <= 2)
    //    {
    //        return image.Clone();
    //        //Cv2.Resize(image, result, new Size(), 1 + attempt, 1 + attempt, InterpolationFlags.Linear);
    //    }

    //    Mat resize = new Mat();
    //    Cv2.Resize(image, resize, new Size(), 3, 3, InterpolationFlags.Linear);

    //    if (attempt == 3)
    //    {
    //        using (Mat gray = new Mat())
    //        {
    //            if (resize.Channels() == 3)
    //                Cv2.CvtColor(resize, gray, ColorConversionCodes.BGR2GRAY);
    //            else
    //                resize.CopyTo(gray);

    //            Cv2.AdaptiveThreshold(gray, result, 256, AdaptiveThresholdTypes.GaussianC,
    //                ThresholdTypes.Binary, 11, 2);
    //        }
    //    }
    //    else if (attempt == 4)
    //    {
    //        Mat kernel = Mat.FromArray(new float[,] {
    //            { 0, -1, 0 },
    //            { -1, 5, -1 },
    //            { 0, -1, 0 }
    //        });
    //        Cv2.Filter2D(resize, result, -1, kernel);
    //    }
    //    else if (attempt == 5)
    //    {
    //        Mat kernel = Mat.FromArray(new float[,]
    //        {
    //            { -1, -1, -1 },
    //            { -1, 9, -1 },
    //            { -1, -1, -1 }
    //        });
    //        Cv2.Filter2D(resize, result, -1, kernel);
    //    }
    //    else if (attempt == 6)
    //    {
    //        Mat kernel = Mat.FromArray(new float[,]{
    //            { -1, -1, -1 },
    //            { -1, 10, -1 },
    //            { -1, -1, -1 }
    //        });
    //        Cv2.Filter2D(resize, result, -1, kernel);
    //    }
    //    else
    //    {
    //        return image.Clone();
    //    }

    //    return result; 
    //}

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
            using (Mat rawImg = Cv2.ImRead(imageFile))
            {
                if (rawImg.Empty())
                    return new MetadataResult();
                return ReadMetadata(rawImg, debug);
            }
        }

        public static MetadataResult ReadMetadata(Bitmap bitmap, bool debug = false)
        {
            using (Mat mat = OpenCvSharp.Extensions.BitmapConverter.ToMat(bitmap))
            {
                return ReadMetadata(mat, debug);
            }
        }

        public static MetadataResult ReadMetadata(Mat image, bool debug = false)
        {
            var result = new MetadataResult();

            // 左侧：元数据条码（$ 开头，Base64 编码）
            var leftCodes = DetectBarcodes(image, isLeft: true, debug);
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
                            result.HasErrorCorrection = (result.Metadata[1] & 0x20) != 0;
                            result.ColorDepth = result.Metadata[1] & 0x1F;
                            result.CurrentPage = result.Metadata[2];
                            result.TotalPages = result.Metadata[3];
                            if (result.Metadata.Length >= 5)
                            {
                                result.ErrorCorrectionPercent = result.Metadata[4];
                            }
                            if (result.Metadata.Length >= 7)
                            {
                                result.TotalQrCodeCount = (result.Metadata[5] << 8) | result.Metadata[6];
                            }
                        }
                    }
                    catch { }
                }
            }

            // 右侧：文件名条码、时间戳条码（Base64）
            var rightCodes = DetectBarcodes(image, isLeft: false, debug);
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

        private static List<Rect> MergeVerticallyAlignedRects(List<Rect> rects, int yGapThreshold)
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
                Cv2.MedianBlur(gray, gray, 5);
                Cv2.GaussianBlur(gray, gray, new Size(7, 7), 0);

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
                                    //if (debug)
                                    //    Cv2.ImShow($"{label} - 6 All Rects", debugRoi);
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