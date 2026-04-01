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
        /// <param name="fileStream">输出文件流</param>
        /// <param name="outputFileName">解析出的原始文件名（如果存在）</param>
        /// <returns>是否成功</returns>
        public static bool ReadToFile(string fileName, FileStream fileStream, out string outputFileName)
        {
            outputFileName = null;
            // 读取图像
            Mat image = Cv2.ImRead(fileName);

            Mat gray = new Mat();
            Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);

            // 二值化处理，便于轮廓检测
            Mat thresh = new Mat();
            Cv2.Threshold(gray, thresh, 127, 255, ThresholdTypes.BinaryInv);

            // 查找轮廓
            Cv2.FindContours(thresh, out Point[][] contours, out HierarchyIndex[] hierarchy, RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

            var lagerContours = contours.Where(c => Cv2.ContourArea(c) > 100).ToList();

            // 在原始图像上绘制所有轮廓
            Mat contoursOutput = image.Clone();
            Cv2.DrawContours(contoursOutput, lagerContours, -1, new Scalar(0, 0, 255), 2);

            var reader = new BarcodeReader();
            List<string> readResult = new List<string>();
            // 设置 X 坐标容差值
            var list = lagerContours.Select(c => new { Rect = Cv2.BoundingRect(c), Contour = c })
                .Where(c => c.Rect.Height > 2 & c.Rect.Width > 2 && (c.Rect.Height * 1.0 / c.Rect.Width) < 4 &&
                            (c.Rect.Height * 1.0 / c.Rect.Width) > 1.0 / 4)
                .OrderBy(c => c.Rect.Width + c.Rect.Height).ThenBy(c => c.Rect.Y).ThenBy(c => c.Rect.X).ToList();

            string info = null;
            int index = 0;
            foreach (var contour in list.ToList())
            {
                index++;
                // 获取外接矩形
                Rect rect = contour.Rect;
                Cv2.Rectangle(image, rect, new Scalar(0, 0, 255), 2);

                // 从图像中裁剪该矩形区域
                Mat roi = new Mat(image, rect);

                if (!ReadImage(roi, reader, readResult))
                {
                    list.Remove(contour);
                }
                else
                {
                    info = readResult.First();
                    readResult.Clear();
                    break;
                }
            }

            if (info == null)
            {
                MessageBox.Show("找不到信息" + fileName);
                return false;
            }

            var splited = info.TrimStart('$').Split(',');
            int rowCount = int.Parse(splited[0]);
            int colCount = int.Parse(splited[1]);
            bool isColorful = splited[2] == "1";
            int colorDepth = int.Parse(splited[3]);
            int offset = int.Parse(splited[4]);
            int length = int.Parse(splited[5]);
            int totalLength = int.Parse(splited[6]);

            int rindex = 0;
            foreach (var contour in list.Skip(index))
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
                        Cv2.ImShow($"找不到信息 {fileName}/{rindex}/{offset}/{totalLength}", roi);
                        Cv2.ImShow($"找不到信息 {fileName}/{rindex}/{offset}/{totalLength} Bin", roiBin);
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
    }
}