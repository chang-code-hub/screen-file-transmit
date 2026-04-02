using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using ZXing;
using ZXing.Common;
using CvPoint = OpenCvSharp.Point;
using CvSize = OpenCvSharp.Size;

namespace qr_codec_test
{
    /// <summary>
    /// 简化的 DataMatrix 解码器，支持彩色模式
    /// </summary>
    public static class DataMatrixDecoder
    {
        private static string rcString = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

        /// <summary>
        /// 图像解码结果
        /// </summary>
        public class DecodeResult
        {
            public List<(int row, int col, byte[] data)> DataBlocks { get; set; } = new List<(int row, int col, byte[] data)>();
            public int MaxRows { get; set; }
            public int MaxCols { get; set; }
            public bool Colorful { get; set; }
            public int ColorDepth { get; set; }
            public int CurrentPage { get; set; }
            public int TotalPages { get; set; }
            public string SessionGuid { get; set; }
        }

        /// <summary>
        /// 从图片解码所有数据块（包含元数据）
        /// </summary>
        public static DecodeResult DecodeImageWithMetadata(string imageFile)
        {
            var result = new DecodeResult();

            using (Mat image = Cv2.ImRead(imageFile))
            {
                if (image.Empty())
                {
                    throw new Exception($"Failed to load image: {imageFile}");
                }

                // 1. 首先解码元数据条码
                var metadata = ExtractMetadata(image, imageFile + ".debug.png");
                if (metadata != null)
                {
                    result.MaxRows = metadata.MaxRows;
                    result.MaxCols = metadata.MaxCols;
                    result.Colorful = metadata.Colorful;
                    result.ColorDepth = metadata.ColorDepth;
                    result.CurrentPage = metadata.CurrentPage;
                    result.TotalPages = metadata.TotalPages;
                }

                // 2. 查找所有 DataMatrix 轮廓
                var dataMatrixContours = FindDataMatrixContours(image);

                // 3. 根据是否彩色模式解码
                if (result.Colorful)
                {
                    result.DataBlocks = DecodeColorfulMode(image, dataMatrixContours );
                }
                else
                {
                    result.DataBlocks = DecodeGrayscaleMode(image, dataMatrixContours);
                }
            }

            return result;
        }

        /// <summary>
        /// 从图片解码所有数据块（向后兼容的简化版本）
        /// </summary>
        public static List<(int row, int col, byte[] data)> DecodeImage(string imageFile)
        {
            var result = DecodeImageWithMetadata(imageFile);
            return result.DataBlocks;
        }

        /// <summary>
        /// 提取元数据条码信息
        /// </summary>
        private class Metadata
        {
            public int MaxRows { get; set; }
            public int MaxCols { get; set; }
            public bool Colorful { get; set; }
            public int ColorDepth { get; set; }
            public int CurrentPage { get; set; }
            public int TotalPages { get; set; }
        }

        private static Metadata ExtractMetadata(Mat image, string debugImagePath = null)
        {
            // 元数据条码通常在图像左侧边缘区域
            var leftRegion = new Mat(image, new Rect(0, 0, image.Width / 4, image.Height));

            // 创建调试图像（复制原图用于绘制轮廓）
            Mat debugImage = null;
            if (!string.IsNullOrEmpty(debugImagePath))
            {
                debugImage = image.Clone();
            }

            using (Mat gray = new Mat())
            using (Mat thresh = new Mat())
            {
                Cv2.CvtColor(leftRegion, gray, ColorConversionCodes.BGR2GRAY);
                Cv2.Threshold(gray, thresh, 127, 255, ThresholdTypes.BinaryInv);

                // 形态学操作：膨胀以连接条码条纹
                using (Mat dilated = new Mat())
                using (Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(15, 3)))
                {
                    Cv2.Dilate(thresh, dilated, kernel, null, 2);

                    // 保存调试图像查看形态学效果
                    if (debugImagePath != null)
                    {
                        Cv2.ImWrite(debugImagePath + ".morph.png", dilated);
                    }

                    Cv2.FindContours(dilated, out CvPoint[][] contours, out HierarchyIndex[] hierarchy,
                        RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                    // 寻找旋转90度的条码轮廓 - 条码是水平方向的（宽 > 高）
                    var barcodeContours = contours
                        .Where(c => Cv2.ContourArea(c) > 500)
                        .Select(c => Cv2.BoundingRect(c))
                        .Where(r => r.Width > r.Height * 1.5 && r.Width > 40 && r.Height > 10)
                        .OrderBy(r => r.X)
                        .ToList();

                    // 在调试图像上绘制所有检测到的轮廓（坐标需要加上 leftRegion 的偏移）
                    if (debugImage != null)
                    {
                        foreach (var contour in contours)
                        {
                            var r = Cv2.BoundingRect(contour);
                            // 绘制所有轮廓（蓝色）- 转换为原图坐标
                            var debugRect = new Rect(r.X, r.Y, r.Width, r.Height);
                            Cv2.Rectangle(debugImage, debugRect, new Scalar(255, 0, 0), 2);
                        }
                        // 高亮显示筛选后的条码轮廓（绿色）
                        foreach (var rect in barcodeContours)
                        {
                            Cv2.Rectangle(debugImage, rect, new Scalar(0, 255, 0), 3);
                        }
                    }

                    var reader = new BarcodeReader();
                    reader.Options.TryHarder = true;
                    reader.Options.PossibleFormats = new[] { BarcodeFormat.CODE_128 };

                    foreach (var rect in barcodeContours)
                    {
                        // 扩大 ROI（限制在左侧区域内，避免文字干扰）
                        int padding = 5;
                        int x = Math.Max(0, rect.X - padding);
                        int y = Math.Max(0, rect.Y - padding);
                        int w = Math.Min(leftRegion.Width - x, rect.Width + 2 * padding);
                        int h = Math.Min(leftRegion.Height - y, rect.Height + 2 * padding);

                        // 限制ROI高度，避免包含旁边的文字
                        h = Math.Min(h, rect.Height + 20);

                        Mat roi = new Mat(leftRegion, new Rect(x, y, w, h));

                        // 条码旋转了90度，需要旋转回垂直方向解码
                        Mat rotatedRoi = new Mat();
                        Cv2.Transpose(roi, rotatedRoi);
                        Cv2.Flip(rotatedRoi, rotatedRoi, FlipMode.Y);

                        string decoded = TryDecodeAnyBarcode(rotatedRoi, reader);
                        roi.Dispose();
                        rotatedRoi.Dispose();

                        if (!string.IsNullOrEmpty(decoded) && decoded.StartsWith("$"))
                        {
                            // 解析元数据: $XX-XX-XX-XX
                            var metadata = ParseMetadata(decoded);
                            if (metadata != null)
                            {
                                // 高亮显示成功解码的条码（红色）
                                if (debugImage != null)
                                {
                                    Cv2.Rectangle(debugImage, rect, new Scalar(0, 0, 255), 4);
                                    Cv2.PutText(debugImage, decoded, new OpenCvSharp.Point(rect.X, rect.Y - 10),
                                        HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 0, 255), 1);

                                    Cv2.ImWrite(debugImagePath, debugImage);
                                    debugImage.Dispose();
                                }

                                leftRegion.Dispose();
                                return metadata;
                            }
                        }
                    }
                }

                // 保存调试图像（即使没有找到元数据）
                if (debugImage != null)
                {
                    Cv2.ImWrite(debugImagePath, debugImage);
                    debugImage.Dispose();
                }

                leftRegion.Dispose();
                return null;
            }
        }

        /// <summary>
        /// 解析元数据字符串
        /// </summary>
        private static Metadata ParseMetadata(string metadataString)
        {
            try
            {
                // 格式: $XX-XX-XX-XX
                if (!metadataString.StartsWith("$")) return null;

                var parts = metadataString.Substring(1).Split('-');
                if (parts.Length < 4) return null;

                byte[] bytes = parts.Select(p => Convert.ToByte(p, 16)).ToArray();

                var meta = new Metadata();
                meta.MaxRows = (bytes[0] >> 4) & 0x0F;
                meta.MaxCols = bytes[0] & 0x0F;
                meta.Colorful = (bytes[1] & 0x80) != 0;
                meta.ColorDepth = bytes[1] & 0x7F;
                meta.CurrentPage = bytes[2];
                meta.TotalPages = bytes[3];

                return meta;
            }
            catch
            {
                return null;
            }
        }

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

                Cv2.FindContours(thresh, out CvPoint[][] contours, out HierarchyIndex[] hierarchy,
                    RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                var validContours = contours
                    .Where(c => Cv2.ContourArea(c) > 100)
                    .Select(c => Cv2.BoundingRect(c))
                    .Where(r => r.Height > 15 && r.Width > 15 &&
                               Math.Abs(r.Height - r.Width) < Math.Max(r.Height, r.Width) * 0.3)
                    .ToList();

                if (validContours.Count == 0) return new List<Rect>();

                // 按面积过滤噪声
                var avgArea = validContours.Average(r => r.Width * r.Height);
                return validContours
                    .Where(r => r.Width * r.Height > avgArea * 0.2)
                    .OrderBy(r => r.Y)
                    .ThenBy(r => r.X)
                    .ToList();
            }
        }

        /// <summary>
        /// 灰度模式解码
        /// </summary>
        private static List<(int row, int col, byte[] data)> DecodeGrayscaleMode(Mat image, List<OpenCvSharp.Rect> contours)
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
        private static List<(int row, int col, byte[] data)> DecodeColorfulMode(Mat image, List<OpenCvSharp.Rect> contours )
        {
            var results = new List<(int row, int col, byte[] data)>();
            var reader = new BarcodeReader();
            reader.Options.TryHarder = true;
            reader.Options.PossibleFormats = new[] { BarcodeFormat.DATA_MATRIX };

            // 分离 BGR 通道 (OpenCV 默认是 BGR)
            Mat[] channels = new Mat[3];
            Cv2.Split(image, out channels);

            // 对于每个轮廓位置，尝试从每个通道解码
            foreach (var rect in contours)
            {
                var blockResults = new List<(int layer, int row, int col, byte[] data)>();

                // 尝试从 R/G/B 三个通道解码（OpenCV 中是 B,G,R）
                for (int layer = 0; layer < 3; layer++)
                {
                    var decoded = DecodeDataMatrixFromChannel(channels[layer], rect, reader, layer);
                    if (decoded != null)
                    {
                        blockResults.Add((layer, decoded.Value.row, decoded.Value.col, decoded.Value.data));
                    }
                }

                if (blockResults.Count > 0)
                {
                    // 合并同一位置的多层数据
                    var merged = MergeLayerData(blockResults);
                    results.Add(merged);
                }
            }

            // 释放通道
            foreach (var ch in channels) ch.Dispose();

            return results;
        }

        /// <summary>
        /// 从单通道解码 DataMatrix
        /// </summary>
        private static (int row, int col, byte[] data)? DecodeDataMatrixFromChannel(
            Mat channel, OpenCvSharp.Rect rect, BarcodeReader reader, int layer )
        {
            // 提取 ROI
            int padding = 5;
            int x = Math.Max(0, rect.X - padding);
            int y = Math.Max(0, rect.Y - padding);
            int w = Math.Min(channel.Width - x, rect.Width + 2 * padding);
            int h = Math.Min(channel.Height - y, rect.Height + 2 * padding);

            using (Mat roi = new Mat(channel, new OpenCvSharp.Rect(x, y, w, h)))
            {
                // 二值化
                using (Mat binary = new Mat())
                {
                    // 根据层和色深调整阈值
                    int threshold = 127;
                    Cv2.Threshold(roi, binary, threshold, 255, ThresholdTypes.Binary);

                    // 转回 BGR 格式用于解码器
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
                                string base64 = decoded.Substring(2);
                                try
                                {
                                    byte[] bytes = Convert.FromBase64String(base64);
                                    return (row, col, bytes);
                                }
                                catch { }
                            }
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 在指定位置解码 DataMatrix（用于灰度模式）
        /// </summary>
        private static (int row, int col, byte[] data)? DecodeDataMatrixAt(Mat image, OpenCvSharp.Rect rect, BarcodeReader reader)
        {
            int padding = 5;
            int x = Math.Max(0, rect.X - padding);
            int y = Math.Max(0, rect.Y - padding);
            int w = Math.Min(image.Width - x, rect.Width + 2 * padding);
            int h = Math.Min(image.Height - y, rect.Height + 2 * padding);

            using (Mat roi = new Mat(image, new OpenCvSharp.Rect(x, y, w, h)))
            {
                string decoded = TryDecodeDataMatrix(roi, reader);

                if (!string.IsNullOrEmpty(decoded) && decoded.Length >= 2)
                {
                    int row = rcString.IndexOf(decoded[0]);
                    int col = rcString.IndexOf(decoded[1]);

                    if (row >= 0 && col >= 0)
                    {
                        string base64 = decoded.Substring(2);
                        try
                        {
                            byte[] bytes = Convert.FromBase64String(base64);
                            return (row, col, bytes);
                        }
                        catch { }
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

            // 按层排序并合并数据
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
                using (var bitmap = BitmapConverter.ToBitmap(processed))
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
        /// 尝试解码任意类型的条码
        /// </summary>
        private static string TryDecodeAnyBarcode(Mat image, BarcodeReader reader, int retryCount = 3)
        {
            for (int attempt = 0; attempt <= retryCount; attempt++)
            {
                using (Mat processed = PreprocessForDecode(image, attempt))
                using (var bitmap = BitmapConverter.ToBitmap(processed))
                {
                    var result = reader.Decode(bitmap);
                    if (result != null) return result.Text;
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
                // 第一次尝试：直接使用原图
                return image.Clone();
            }

            Mat result = new Mat();

            if (attempt == 1)
            {
                // 第二次：缩放
                Cv2.Resize(image, result, new CvSize(), 2.0, 2.0, InterpolationFlags.Linear);
            }
            else if (attempt == 2)
            {
                // 第三次：转灰度后自适应阈值
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
                // 第四次：锐化
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

        /// <summary>
        /// 预处理图像以提高解码成功率
        /// </summary>
        private static Mat PreprocessForRetry(Mat image)
        {
            double scaleFactor = 2.0;
            Mat resized = new Mat();
            Cv2.Resize(image, resized, new CvSize(), scaleFactor, scaleFactor, InterpolationFlags.Linear);

            Mat sharpenKernel = Mat.FromArray(new float[,]
            {
                { -1, -1, -1 },
                { -1, 9, -1 },
                { -1, -1, -1 }
            });

            Mat sharpened = new Mat();
            Cv2.Filter2D(resized, sharpened, -1, sharpenKernel);
            resized.Dispose();

            return sharpened;
        }

        /// <summary>
        /// 从第一页提取文件名
        /// </summary>
        public static string ExtractFileName(List<(int row, int col, byte[] data)> decodedData)
        {
            var firstChunk = decodedData.FirstOrDefault(d => d.row == 0 && d.col == 0);
            if (firstChunk.data == null || firstChunk.data.Length == 0)
            {
                return null;
            }

            int separatorIndex = Array.IndexOf(firstChunk.data, (byte)0x00);
            if (separatorIndex <= 0)
            {
                return null;
            }

            var fileNameBytes = firstChunk.data.Take(separatorIndex).ToArray();
            return Encoding.UTF8.GetString(fileNameBytes);
        }

        /// <summary>
        /// 获取数据部分（去掉文件名）
        /// </summary>
        public static byte[] ExtractDataWithoutFileName(byte[] data)
        {
            int separatorIndex = Array.IndexOf(data, (byte)0x00);
            if (separatorIndex < 0)
            {
                return data;
            }

            int startIndex = separatorIndex + 1;
            if (startIndex >= data.Length)
            {
                return new byte[0];
            }

            return data.Skip(startIndex).ToArray();
        }



        /// <summary>
        /// 截取图像左侧或右侧 1/10 区域，水平拉宽 5 倍
        /// </summary>
        /// <param name="image">输入图像</param>
        /// <param name="takeLeft">true 截取左侧，false 截取右侧</param>
        /// <returns>处理后的图像</returns>
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
            Cv2.Resize(cropped, stretched, new OpenCvSharp.Size(cropWidth * 5, image.Height), 0, 0, InterpolationFlags.Linear);

            return stretched;
        }
    }
}
