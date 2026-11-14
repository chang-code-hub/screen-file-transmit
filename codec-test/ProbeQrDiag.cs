using OpenCvSharp;
using screen_file_transmit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace qr_codec_test
{
    /// <summary>
    /// 诊断单个 QR cell 解码失败的原因：记录轮廓、尺寸、各 attempt 结果。
    /// </summary>
    internal class ProbeQrDiag
    {
        public static void Run(string path)
        {
            Console.WriteLine("[DIAG] " + Path.GetFileName(path));
            using (Mat raw = Cv2.ImRead(path, ImreadModes.Color))
            {
                // 直接调用边界检测
                var mi = typeof(ImageDecoder).GetMethod("FindDataMatrixContours",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                var contours = (List<OpenCvSharp.Rect>)mi.Invoke(null, new object[] { raw });
                Console.WriteLine("[DIAG] located=" + contours.Count);

                // 调试：打印模块估计和阈值
                using (Mat gray = new Mat())
                {
                    Cv2.CvtColor(raw, gray, ColorConversionCodes.BGR2GRAY);
                    var mi2 = typeof(ImageDecoder).GetMethod("EstimateModuleSize",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    double module = (double)mi2.Invoke(null, new object[] { gray });
                    Console.WriteLine($"[DIAG] module={module} minCell={module*12} maxCell={Math.Min(gray.Width, gray.Height)*0.9}");
                }

                for (int i = 0; i < contours.Count; i++)
                {
                    var r = contours[i];
                    int pad = Math.Max(8, r.Width / 20);
                    int x = Math.Max(0, r.X - pad), y = Math.Max(0, r.Y - pad);
                    int w = Math.Min(raw.Width - x, r.Width + 2 * pad), h = Math.Min(raw.Height - y, r.Height + 2 * pad);
                    using (Mat roi = new Mat(raw, new OpenCvSharp.Rect(x, y, w, h)))
                    {
                        var txt = ImageDecoder.TryDecodeDataMatrix(roi, false, 10);
                        string status = txt == null ? "FAIL" : ("OK len=" + txt.Length);
                        Console.WriteLine($"[DIAG] [{i}] rect=({r.X},{r.Y},{r.Width}x{r.Height}) pad={pad} -> {status}");
                    }
                }
            }
        }
    }
}
