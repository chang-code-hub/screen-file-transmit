using OpenCvSharp;
using System;
namespace qr_codec_test
{
    internal class ProbeMapCheck
    {
        public static void Run()
        {
            // 已知标记：原图 100x40，红色矩形在 (10,5) 尺寸 20x10
            var orig = new Mat(40, 100, MatType.CV_8UC3, Scalar.White);
            Cv2.Rectangle(orig, new Rect(10, 5, 20, 10), new Scalar(0, 0, 255), -1);

            // 复刻 StretchSideRegion：裁左侧 1/10=10px 宽，拉到 30 宽
            var crop = new Mat(orig, new Rect(0, 0, 10, 40));
            var stretched = new Mat();
            Cv2.Resize(crop, stretched, new Size(30, 40));
            Console.WriteLine($"stretched: {stretched.Width}x{stretched.Height}");

            var rotated = new Mat();
            Cv2.Rotate(stretched, rotated, RotateFlags.Rotate90Counterclockwise);
            Console.WriteLine($"rotated: {rotated.Width}x{rotated.Height}");

            // 在旋转图里找红色标记
            var mask = new Mat();
            Cv2.InRange(rotated, new Scalar(0, 0, 0), new Scalar(80, 80, 255), mask);
            var pts = new Mat();
            Cv2.FindNonZero(mask, pts);
            var b = Cv2.BoundingRect(pts);
            Console.WriteLine($"marker in rotated: X={b.X} Y={b.Y} W={b.Width} H={b.Height}");

            // 反推：旋转图 -> 拉伸图
            double Hr = rotated.Rows;
            double sx1 = Hr - 1 - b.Y, sy1 = b.X;
            double sx2 = Hr - 1 - (b.Bottom - 1), sy2 = (b.Right - 1);
            double sL = Math.Min(sx1, sx2), sR = Math.Max(sx1, sx2);
            double sT = Math.Min(sy1, sy2), sB = Math.Max(sy1, sy2);
            Console.WriteLine($"stretched coords: x={sL:F1}..{sR:F1} y={sT:F1}..{sB:F1}");

            // 反推：拉伸图 -> 原图（除以 stretch=3，左侧裁剪偏移 0）
            double stretch = 3.0;
            Console.WriteLine($"orig coords: x={sL / stretch:F1}..{sR / stretch:F1} y={sT / stretch:F1}..{sB / stretch:F1}");
            Console.WriteLine("EXPECTED: x=10..30 y=5..15");
        }
    }
}
