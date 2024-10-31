using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
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
        public static void Read(string fileName)
        {

            // 读取图像
            Mat img = Cv2.ImRead(fileName);

            Mat gray = new Mat();
            Cv2.CvtColor(img, gray, ColorConversionCodes.BGR2GRAY);

            // 二值化处理，便于轮廓检测
            Mat thresh = new Mat();
            Cv2.Threshold(gray, thresh, 16, 255, ThresholdTypes.BinaryInv);
            //Cv2.ImShow("Threshold", thresh);

            // 查找轮廓
            Cv2.FindContours(thresh, out Point[][] contours, out HierarchyIndex[] hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            var lagerContours = contours.Where(c => Cv2.ContourArea(c) > 100).ToList();

            // 在原始图像上绘制所有轮廓
            Mat contoursOutput = img.Clone();
            Cv2.DrawContours(contoursOutput, lagerContours, -1, new Scalar(0, 0, 255), 2); // 用红色绘制所有轮廓，线宽为2

            // 显示结果图像
            Cv2.ImShow("All Contours", contoursOutput);


            Rect datamatrixRegion = new Rect();
            List<Rect> colorBlocks = new List<Rect>();
            bool datamatrixFound = false;

            foreach (var contour in contours)
            {
                double area = Cv2.ContourArea(contour);

                // 过滤掉较小的区域，避免噪声
                if (area > 100)
                {
                    // 获取外接矩形
                    Rect rect = Cv2.BoundingRect(contour);

                    // 假设第一个符合条件的正方形为 DataMatrix 码
                    if (!datamatrixFound) // && Math.Abs(rect.Width - rect.Height) < 10 && rect.X < img.Width / 2 && rect.Y < img.Height / 2)
                    {
                        datamatrixRegion = rect;
                        datamatrixFound = true;
                    }
                    else if (datamatrixFound && rect.Y > datamatrixRegion.Y) // DataMatrix 下方的色块
                    {
                        colorBlocks.Add(rect);
                    }
                }
            }

            // 在图像上绘制 DataMatrix 区域和色块区域
            Mat output = img.Clone();
            if (datamatrixFound)
            {
                Cv2.Rectangle(output, datamatrixRegion, new Scalar(0, 255, 0), 2); // DataMatrix 区域用绿色
            }

            foreach (var block in colorBlocks)
            {
                Cv2.Rectangle(output, block, new Scalar(255, 0, 0), 2); // 色块区域用蓝色
            }

            // 显示结果图像
            Cv2.ImShow("Detected DataMatrix and Blocks", output);
            Cv2.WaitKey(0);
            Cv2.DestroyAllWindows();


            //Mat gray = new Mat();
            //Cv2.CvtColor(img, gray, ColorConversionCodes.BGR2GRAY);

            //// 设置阈值并进行二值化   
            //Mat binary = new Mat();
            //Cv2.Threshold(gray, binary, 32, 255, ThresholdTypes.Binary);
            //Cv2.ImShow("binary", binary);

            //// 转换为 HSV 色彩空间
            //Mat hsvImage = new Mat();
            //Cv2.CvtColor(img, hsvImage, ColorConversionCodes.BGR2HSV);
            //Cv2.ImShow("BGR2HSV", hsvImage);

            //// 设置颜色范围，找到类似的颜色方块（根据需要调整范围）
            //Scalar lowerBound = new Scalar(90, 50, 50);  // 最低HSV值
            //Scalar upperBound = new Scalar(130, 255, 255); // 最高HSV值

            //// 创建掩码
            //Mat mask = new Mat();
            //Cv2.InRange(hsvImage, lowerBound, upperBound, mask);
            //Cv2.ImShow("InRange", mask);

            //// 形态学操作去噪
            //Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(2, 2));
            //Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);
            //Cv2.ImShow("MorphologyEx", mask);

            //// 寻找轮廓
            //Point[][] contours;
            //HierarchyIndex[] hierarchy;
            //Cv2.FindContours(mask, out contours, out hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            //// 绘制和输出每个检测到的方块的边界
            //foreach (var contour in contours)
            //{
            //    var rect = Cv2.BoundingRect(contour);
            //    Cv2.Rectangle(img, rect, new Scalar(0, 0, 255), 2); // 绘制红色矩形框
            //}

            //// 显示或保存结果
            //Cv2.ImShow("Detected Rectangles", img);
            //Cv2.WaitKey(0);
            //Cv2.DestroyAllWindows();



            ////Cv2.ImShow("BGray", bgray);
            ////Cv2.ImShow("Gray", gray);
            //Cv2.ImShow("Binary", binary);  

            //// 查找轮廓
            //Mat hierarchy = new Mat();
            //Cv2.FindContours(binary, out var contours, hierarchy, RetrievalModes.Tree, ContourApproximationModes.ApproxSimple);

            //// 筛选轮廓
            //for (int i = 0; i < contours.Length; i++)
            //{
            //    // 计算轮廓面积、周长等特征
            //    double area = Cv2.ContourArea(contours[i]);
            //    double perimeter = Cv2.ArcLength(contours[i], true);

            //    // 根据特征筛选

            //    if(perimeter >= ( img.Height + img.Width) * 2 -10) 
            //        continue;

            //    //if (area > minArea && perimeter > minPerimeter)
            //    //{
            //    //    // 绘制轮廓
            //    //    Cv2.DrawContours(img, contours, i, Scalar.Red, 1);
            //    //    break;
            //    //}
            //}

            //// 显示结果
            //Cv2.ImShow("Hierarchy", img);
            //Cv2.WaitKey(0);
        }
    }
}
