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
    /// 简化的 DataMatrix 解码器，用于测试集成
    /// </summary>
    public static class DataMatrixDecoder
    {
        private static string rcString = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

        /// <summary>
        /// 从图片解码所有数据块
        /// </summary>
        public static List<(int row, int col, byte[] data)> DecodeImage(string imageFile)
        {
            var results = new List<(int row, int col, byte[] data)>();

            // 读取图像
            using (Mat image = Cv2.ImRead(imageFile))
            using (Mat gray = new Mat())
            using (Mat thresh = new Mat())
            {
                if (image.Empty())
                {
                    throw new Exception($"Failed to load image: {imageFile}");
                }

                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
                Cv2.Threshold(gray, thresh, 127, 255, ThresholdTypes.BinaryInv);

                // 查找轮廓
                Cv2.FindContours(thresh, out CvPoint[][] contours, out HierarchyIndex[] hierarchy,
                    RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                // 筛选并排序轮廓
                var validContours = contours
                    .Where(c => Cv2.ContourArea(c) > 150)
                    .Select(c => new { Rect = Cv2.BoundingRect(c), Contour = c })
                    .Where(c => c.Rect.Height > 2 && c.Rect.Width > 2 &&
                               (c.Rect.Height * 1.0 / c.Rect.Width) < 4 &&
                               (c.Rect.Height * 1.0 / c.Rect.Width) > 1.0 / 4)
                    .OrderBy(c => c.Rect.Width + c.Rect.Height)
                    .ThenBy(c => c.Rect.Y)
                    .ThenBy(c => c.Rect.X)
                    .ToList();

                if (validContours.Count == 0)
                {
                    return results;
                }

                var reader = new BarcodeReader();
                var decodedStrings = new List<string>();

                // 第一个轮廓通常是元数据条码，跳过
                int startIndex = 1;

                for (int i = startIndex; i < validContours.Count; i++)
                {
                    var contour = validContours[i];
                    Mat roi = new Mat(image, contour.Rect);

                    string decoded = TryDecodeBarcode(roi, reader);
                    if (decoded != null)
                    {
                        decodedStrings.Add(decoded);
                    }
                    roi.Dispose();
                }

                // 解析所有解码结果
                foreach (var code in decodedStrings)
                {
                    if (code.Length < 2) continue;

                    int row = rcString.IndexOf(code[0]);
                    int col = rcString.IndexOf(code[1]);

                    if (row < 0 || col < 0) continue;

                    string base64 = code.Substring(2);
                    try
                    {
                        byte[] bytes = System.Convert.FromBase64String(base64);
                        results.Add((row, col, bytes));
                    }
                    catch (System.FormatException)
                    {
                        // 跳过无效的 Base64
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// 尝试解码条码，带重试逻辑
        /// </summary>
        private static string TryDecodeBarcode(Mat image, BarcodeReader reader, int retryCount = 3)
        {
            for (int attempt = 0; attempt <= retryCount; attempt++)
            {
                using (var bitmap = BitmapConverter.ToBitmap(image))
                {
                    var result = reader.Decode(bitmap);

                    if (result != null && result.BarcodeFormat == BarcodeFormat.DATA_MATRIX)
                    {
                        return result.Text;
                    }
                }

                if (attempt < retryCount)
                {
                    // 预处理重试
                    image = PreprocessForRetry(image);
                }
            }

            return null;
        }

        /// <summary>
        /// 预处理图像以提高解码成功率
        /// </summary>
        private static Mat PreprocessForRetry(Mat image)
        {
            // 放大图像
            double scaleFactor = 2.0;
            Mat resized = new Mat();
            Cv2.Resize(image, resized, new CvSize(), scaleFactor, scaleFactor, InterpolationFlags.Linear);

            // 锐化
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
            // 找到 (0,0) 位置的数据块
            var firstChunk = decodedData.FirstOrDefault(d => d.row == 0 && d.col == 0);
            if (firstChunk.data == null || firstChunk.data.Length == 0)
            {
                return null;
            }

            // 查找 \0 分隔符
            int separatorIndex = Array.IndexOf(firstChunk.data, (byte)0x00);
            if (separatorIndex <= 0)
            {
                return null;
            }

            // 提取文件名
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

            // 返回分隔符之后的数据
            int startIndex = separatorIndex + 1;
            if (startIndex >= data.Length)
            {
                return new byte[0];
            }

            return data.Skip(startIndex).ToArray();
        }
    }
}
