using OpenCvSharp;
using screen_file_transmit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace qr_codec_test
{
    /// <summary>
    /// 量化边界检测与解码成功率，用于优化前后对比。
    /// 通过 --qrm <dir> 触发（见 Program.cs 的分发）。
    /// </summary>
    internal class ProbeQrMetrics
    {
        public static void Run(string dir)
        {
            var files = Directory.GetFiles(dir, "*.png").Where(f => !f.Contains("degraded")).OrderBy(f => f).ToList();
            int totalCells = 0, located = 0, decoded = 0, fullPages = 0, pages = 0;
            var failures = new List<string>();

            foreach (var f in files)
            {
                using (Mat raw = Cv2.ImRead(f, ImreadModes.Color))
                {
                    if (raw.Empty()) continue;
                    var mi = typeof(ImageDecoder).GetMethod("FindDataMatrixContours",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    var contours = (List<OpenCvSharp.Rect>)mi.Invoke(null, new object[] { raw });

                    int expect = 0;
                    try
                    {
                        var m = ImageDecoder.ReadMetadata(f, false);
                        expect = m.MaxRows * m.MaxCols;
                    }
                    catch { }

                    int ok = 0;
                    foreach (var r in contours)
                    {
                        int pad = 5;
                        int x = Math.Max(0, r.X - pad), y = Math.Max(0, r.Y - pad);
                        int w = Math.Min(raw.Width - x, r.Width + 2 * pad), h = Math.Min(raw.Height - y, r.Height + 2 * pad);
                        using (Mat roi = new Mat(raw, new OpenCvSharp.Rect(x, y, w, h)))
                            if (ImageDecoder.TryDecodeDataMatrix(roi) != null) ok++;
                    }

                    pages++;
                    totalCells += expect;
                    located += contours.Count;
                    decoded += ok;
                    if (expect > 0 && ok >= expect) fullPages++;
                    else failures.Add($"{Path.GetFileName(f)} loc={contours.Count}/{expect} dec={ok}/{expect}");
                }
            }

            Console.WriteLine($"[METRICS] pages={pages} totalCells={totalCells}");
            Console.WriteLine($"[METRICS] located={located} ({Pct(located, totalCells)})  decoded={decoded} ({Pct(decoded, totalCells)})");
            Console.WriteLine($"[METRICS] fullPages={fullPages}/{pages} ({Pct(fullPages, pages)})");
            foreach (var x in failures) Console.WriteLine("[METRICS]   MISS " + x);
        }

        static string Pct(int a, int b) => b == 0 ? "-" : (100.0 * a / b).ToString("F1") + "%";
    }
}
