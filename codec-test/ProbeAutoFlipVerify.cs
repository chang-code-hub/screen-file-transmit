using screen_file_transmit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace qr_codec_test
{
    /// <summary>
    /// 验证 ImageDecoder.VerifyDecodeResult（自动翻页用的「解码无误」判据）：
    /// 逐页输出 PASS/FAIL 以及各分项指标，供核对判据是否合理。
    /// 用法：--afverify <图片目录>
    /// </summary>
    internal class ProbeAutoFlipVerify
    {
        static byte[] CalcCrc(byte[] data)
        {
            uint c = 0xFFFFFFFF;
            foreach (byte b in data)
            {
                c ^= b;
                for (int k = 0; k < 8; k++) c = ((c & 1) != 0) ? (c >> 1) ^ 0xEDB88320 : c >> 1;
            }
            c ^= 0xFFFFFFFF;
            return BitConverter.GetBytes(c);
        }

        public static void Run(string dir)
        {
            var files = Directory.GetFiles(dir, "*.png").OrderBy(f => f).ToList();
            Console.WriteLine("[AFVERIFY] dir=" + dir + " files=" + files.Count);

            int pass = 0, fail = 0;
            foreach (var f in files)
            {
                try
                {
                    using (var bmp = new System.Drawing.Bitmap(f))
                    {
                        var meta = ImageDecoder.ReadMetadata(bmp, false);
                        var res = ImageDecoder.DecodeImageWithMetadata(new System.Drawing.Bitmap(f), false);
                        var blocks = res.DataBlocks ?? new List<(int row, int col, byte[] data)>();

                        int expectCells = meta.MaxRows * meta.MaxCols;
                        var coords = blocks.Select(b => (b.row, b.col)).ToList();
                        int dup = coords.Count - coords.Distinct().Count();
                        int shortBlk = blocks.Count(b => b.data == null || b.data.Length < 6);
                        int crcBad = 0, crcChecked = 0;
                        foreach (var b in blocks)
                        {
                            var bytes = b.data;
                            if (bytes == null || bytes.Length < 2) continue;
                            int payloadLength = (bytes[0] << 8) | bytes[1];
                            if (payloadLength <= 0 || payloadLength + 4 > bytes.Length - 2) continue;
                            var data = new byte[payloadLength];
                            Array.Copy(bytes, 2, data, 0, payloadLength);
                            var crc = new byte[4];
                            Array.Copy(bytes, 2 + payloadLength, crc, 0, 4);
                            crcChecked++;
                            if (!crc.SequenceEqual(CalcCrc(data))) crcBad++;
                        }

                        bool ok = ImageDecoder.VerifyDecodeResult(res, meta, 0.95);
                        if (ok) pass++; else fail++;

                        Console.WriteLine(string.Format(
                            "  {0,-4} {1} page={2}/{3} grid={4}x{5} color={6} cells={7} blocks={8} qr={9}/{10} dup={11} short={12} crcBad={13}/{14}",
                            ok ? "PASS" : "FAIL", Path.GetFileName(f), meta.CurrentPage, meta.TotalPages,
                            meta.MaxRows, meta.MaxCols, meta.Colorful, expectCells, blocks.Count,
                            res.DecodedQrCodeCount, meta.TotalQrCodeCount, dup, shortBlk, crcBad, crcChecked));
                    }
                }
                catch (Exception ex)
                {
                    fail++;
                    Console.WriteLine("  FAIL " + Path.GetFileName(f) + " " + ex.Message.Replace("\r", " ").Replace("\n", " "));
                }
            }

            Console.WriteLine("[AFVERIFY] pass=" + pass + " fail=" + fail);
        }
    }
}
