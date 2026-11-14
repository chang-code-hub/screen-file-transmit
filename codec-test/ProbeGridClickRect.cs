using OpenCvSharp;
using screen_file_transmit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace qr_codec_test
{
    /// <summary>
    /// 用「左上 / 右下」两个 Code128 条码框定一个矩形，在该矩形内取 99%/15% 处的点，
    /// 再换算成相对整图（=选区）的百分比，并标注到输出图上。
    /// 条码检测直接复用 ImageDecoder.DetectBarcodesWithRects（与 ReadMetadata 同一套逻辑）。
    ///
    /// 注意：DetectBarcodesInRoi 检测到的条码矩形是「片段」（只覆盖一小段竖直范围），
    /// 不是完整条码列。因此这里只用条码的 x 坐标（左右边界），
    /// y 范围用内容区域（DataMatrix 网格）的完整高度。
    /// </summary>
    internal class ProbeGridClickRect
    {
        public static void Run(string path)
        {
            Console.WriteLine("[GRIDCLICK] " + path);

            if (!File.Exists(path))
            {
                Console.WriteLine("[GRIDCLICK] 找不到文件: " + path);
                return;
            }

            try
            {
                RunCore(path);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[GRIDCLICK] EX: " + ex.GetType().FullName);
                Console.WriteLine("[GRIDCLICK] EX: " + ex.Message);
                var inner = ex.InnerException;
                while (inner != null)
                {
                    Console.WriteLine("[GRIDCLICK] INNER: " + inner.GetType().FullName + " " + inner.Message);
                    inner = inner.InnerException;
                }
            }
        }

        private static void RunCore(string path)
        {
            using (Mat image = Cv2.ImRead(path, ImreadModes.Color))
            {
                if (image.Empty())
                {
                    Console.WriteLine("[GRIDCLICK] OpenCV 加载失败");
                    return;
                }

                Console.WriteLine($"[GRIDCLICK] 图片尺寸: {image.Width}x{image.Height}");

                var leftBars = ImageDecoder.DetectBarcodesWithRects(image, isLeft: true);
                var rightBars = ImageDecoder.DetectBarcodesWithRects(image, isLeft: false);

                Console.WriteLine($"[GRIDCLICK] 左侧条码数: {leftBars.Count}");
                foreach (var b in leftBars) Console.WriteLine($"    L {b.Rect} text={b.Text}");
                Console.WriteLine($"[GRIDCLICK] 右侧条码数: {rightBars.Count}");
                foreach (var b in rightBars) Console.WriteLine($"    R {b.Rect} text={b.Text}");

                if (leftBars.Count == 0 && rightBars.Count == 0)
                {
                    Console.WriteLine("[GRIDCLICK] 未检测到任何条码，退出");
                    return;
                }

                // 用条码的 x 坐标确定左右边界（取所有条码的并集 x 范围）
                int leftX = leftBars.Count > 0 ? leftBars.Min(b => b.Rect.X) : 0;
                int rightX = rightBars.Count > 0 ? rightBars.Max(b => b.Rect.Right) : image.Width;

                // 用内容区域（DataMatrix 网格）的完整高度作为 y 范围
                var contentRect = FindContentRect(image, leftX, rightX);
                Console.WriteLine($"[GRIDCLICK] 内容区域: {contentRect}");

                var box = new Rect(leftX, contentRect.Y, rightX - leftX, contentRect.Height);
                Console.WriteLine($"[GRIDCLICK] 框定矩形: {box}");

                // 矩形内 99%/15% 处的点
                double gx = box.X + box.Width * 0.99;
                double gy = box.Y + box.Height * 0.15;
                Console.WriteLine($"[GRIDCLICK] 默认点击点(截图像素): ({gx:F1}, {gy:F1})");

                // 相对整图（=选区）width/height 的百分比
                double px = gx * 100.0 / image.Width;
                double py = gy * 100.0 / image.Height;
                Console.WriteLine($"[GRIDCLICK] 相对整图百分比: ({px:F1}%, {py:F1}%)");

                // 标注
                // 黄框：整张图外框，用来对照百分比基准
                Cv2.Rectangle(image, new OpenCvSharp.Point(1, 1),
                    new OpenCvSharp.Point(image.Width - 2, image.Height - 2),
                    new Scalar(0, 255, 255), 2);

                // 蓝框：由条码 x 坐标 + 内容 y 范围框定的矩形
                Cv2.Rectangle(image, box, new Scalar(255, 0, 0), 2);

                // 条码片段的外框（品红）
                foreach (var b in leftBars)
                    Cv2.Rectangle(image, b.Rect, new Scalar(255, 0, 255), 2);
                foreach (var b in rightBars)
                    Cv2.Rectangle(image, b.Rect, new Scalar(255, 0, 255), 2);

                // 红十字 + 圆点：默认点击点
                var dot = new OpenCvSharp.Point((int)Math.Round(gx), (int)Math.Round(gy));
                int arm = Math.Max(12, Math.Min(image.Width, image.Height) / 60);
                Cv2.Line(image, dot.X - arm, dot.Y, dot.X + arm, dot.Y, new Scalar(0, 0, 255), 2);
                Cv2.Line(image, dot.X, dot.Y - arm, dot.X, dot.Y + arm, new Scalar(0, 0, 255), 2);
                Cv2.Circle(image, dot, Math.Max(4, arm / 3), new Scalar(0, 0, 255), 2);

                string outPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".",
                    Path.GetFileNameWithoutExtension(path) + "_annotated.png");
                Cv2.ImWrite(outPath, image);
                Console.WriteLine($"[GRIDCLICK] 标注图已保存: {outPath}");
            }
        }

        /// <summary>
        /// 找内容区域（DataMatrix 网格）的 y 范围。
        /// 在条码 x 范围之间扫描每行的黑白跳变次数，跳变多的行属于网格内容。
        /// </summary>
        private static Rect FindContentRect(Mat image, int leftX, int rightX)
        {
            using (Mat gray = new Mat())
            {
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);

                int x0 = Math.Max(0, leftX);
                int x1 = Math.Min(image.Width - 1, rightX);
                if (x1 <= x0) return new Rect(0, 0, image.Width, image.Height);

                int top = -1, bottom = -1;
                for (int y = 0; y < gray.Height; y++)
                {
                    int transitions = 0;
                    byte prev = gray.At<byte>(y, x0);
                    for (int x = x0 + 1; x <= x1; x++)
                    {
                        byte cur = gray.At<byte>(y, x);
                        if ((prev < 128) != (cur < 128)) transitions++;
                        prev = cur;
                    }

                    // 网格行：黑白跳变多（DataMatrix 模块密集，约 200+），
                    // 阈值取 200 以跳过顶部工具栏按钮（约 60~120 跳变）
                    if (transitions > 200)
                    {
                        if (top < 0) top = y;
                        bottom = y;
                    }
                }

                if (top < 0) return new Rect(0, 0, image.Width, image.Height);
                return new Rect(0, top, image.Width, bottom - top + 1);
            }
        }
    }
}
