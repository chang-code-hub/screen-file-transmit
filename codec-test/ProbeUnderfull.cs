using screen_file_transmit;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Rect = OpenCvSharp.Rect;

namespace qr_codec_test
{
    /// <summary>
    /// 用 samples/cxmt 目录下的分页截图，验证「自动翻页未满载重试 + 屏蔽放大降级解码」整套逻辑，
    /// 并把所有页重组回原始文件（期望为 cxmt.7z）。
    ///
    /// 流程复刻 ScreenshotToolWindow.AutoFlipTick 的核心判据：
    ///   1. 默认路径解码 → VerifyDecodeResult 校验
    ///   2. 通过校验但 IsUnderfull → 累计 _underfullRetries，最多 MaxUnderfullRetries 次
    ///   3. 超过上限后临时 ImageDecoder.DisableUpscale=true 用当前帧重解码（降级）
    ///   4. 降级后仍不满 → 走「保底保存」路径（模拟翻页继续）
    ///   5. 通过校验且不 underfull → 正常保存并翻页
    ///
    /// 用法：--underfull <图片目录> <输出文件路径>
    /// </summary>
    internal class ProbeUnderfull
    {
        private const int MaxUnderfullRetries = 5;
        private const double DecodeQrRatioThreshold = 0.95;

        public static void Run(string dir, string outPath)
        {
            var files = Directory.GetFiles(dir, "*.png").OrderBy(f => f).ToList();
            if (files.Count == 0)
            {
                Console.WriteLine("[UNDERFULL] 没有找到 png 文件: " + dir);
                return;
            }

            Console.WriteLine("[UNDERFULL] dir=" + dir + " files=" + files.Count + " out=" + outPath);

            using (var outputStream = new FileStream(outPath, FileMode.Create, FileAccess.Write))
            {
                int totalPages = 0;
                string fileName = null;
                int fileIdHash = 0;

                foreach (var f in files)
                {
                    string name = Path.GetFileName(f);
                    int underfullRetries = 0;
                    bool disableUpscaleUsed = false;
                    bool saved = false;
                    string mode = null;

                    using (var bmp = new Bitmap(f))
                    {
                        var meta = ImageDecoder.ReadMetadata(bmp, false);
                        if (meta?.Metadata == null || meta.Metadata.Length < 4)
                        {
                            Console.WriteLine("  FAIL " + name + " 元数据不可读");
                            continue;
                        }

                        totalPages = meta.TotalPages;
                        fileName = meta.FileName;
                        fileIdHash = meta.FileId != null ? meta.FileId.GetHashCode() : 0;

                        ImageDecoder.DecodeResult decodeResult = null;
                        bool decodeVerified = false;
                        try
                        {
                            using (var tempBmp = new Bitmap(bmp))
                            {
                                decodeResult = ImageDecoder.DecodeImageWithMetadata(tempBmp, false);
                                decodeVerified = ImageDecoder.VerifyDecodeResult(decodeResult, meta, DecodeQrRatioThreshold);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("  FAIL " + name + " 默认路径解码异常: " + ex.Message);
                            continue;
                        }

                        // 只有「校验通过但未满载」才进入重试循环；
                        // 本身已满载的页不需要 sleep(200) 白等 5 次（真实场景同理：
                        // 未满才会留在本页重截，已满直接走保存翻页路径）。
                        bool needsRetry = decodeVerified && decodeResult != null && decodeResult.IsUnderfull;

                        // 模拟重试循环：未满就 sleep(200) 重试，最多 MaxUnderfullRetries 次
                        // （真实场景是 DispatcherTimer 每 500ms 一帧，这里同步模拟同一逻辑）
                        while (needsRetry && underfullRetries < MaxUnderfullRetries)
                        {
                            underfullRetries++;
                            Thread.Sleep(200);

                            // 重新用当前帧解码（真实场景是下一 tick 重新截图，帧内容相同）
                            try
                            {
                                using (var retryBmp = new Bitmap(bmp))
                                {
                                    decodeResult = ImageDecoder.DecodeImageWithMetadata(retryBmp, false);
                                    decodeVerified = ImageDecoder.VerifyDecodeResult(decodeResult, meta, DecodeQrRatioThreshold);
                                }
                            }
                            catch (Exception)
                            {
                                decodeVerified = false;
                            }

                            needsRetry = decodeVerified && decodeResult != null && decodeResult.IsUnderfull;
                        }

                        // 5 次后仍不满 → 降级：屏蔽放大算法重试
                        bool degraded = false;
                        if (needsRetry && !disableUpscaleUsed)
                        {
                            disableUpscaleUsed = true;
                            degraded = true;
                            ImageDecoder.DisableUpscale = true;
                            try
                            {
                                using (var retryBmp = new Bitmap(bmp))
                                {
                                    decodeResult = ImageDecoder.DecodeImageWithMetadata(retryBmp, false);
                                    decodeVerified = ImageDecoder.VerifyDecodeResult(decodeResult, meta, DecodeQrRatioThreshold);
                                }
                            }
                            catch (Exception)
                            {
                                decodeVerified = false;
                            }
                            finally
                            {
                                ImageDecoder.DisableUpscale = false;
                            }
                        }

                        // 校验失败：真实场景会停止翻页；这里直接报错继续处理下一张
                        if (!decodeVerified)
                        {
                            Console.WriteLine("  FAIL " + name + " 校验未通过 (降级=" + degraded + ")");
                            continue;
                        }

                        bool stillUnderfull = decodeResult != null && decodeResult.IsUnderfull;
                        if (stillUnderfull)
                        {
                            mode = "UNDERFULL(fallback)";
                        }
                        else if (underfullRetries > 0 || degraded)
                        {
                            mode = "OK(retry=" + underfullRetries + ",degraded=" + degraded + ")";
                        }
                        else
                        {
                            mode = "OK";
                        }

                        // 写入输出流（真实场景 SaveCapture，这里直接写 payload）
                        WriteBlocksToStream(decodeResult, outputStream);
                        saved = true;

                        Console.WriteLine(string.Format(
                            "  {0,-4} {1} page={2}/{3} blocks={4} payload={5}B mode={6}",
                            "PASS", name, meta.CurrentPage, meta.TotalPages,
                            decodeResult.DataBlocks?.Count ?? 0,
                            decodeResult.DecodedPayloadBytes, mode));
                    }

                    if (!saved)
                    {
                        Console.WriteLine("  FAIL " + name + " 未写入输出流");
                    }
                }

                Console.WriteLine();
                Console.WriteLine("[UNDERFULL] totalPages=" + totalPages +
                                  " fileName=" + fileName +
                                  " fileIdHash=" + fileIdHash.ToString("X") +
                                  " outSize=" + outputStream.Length + "B");
                Console.WriteLine("[UNDERFULL] 输出: " + outPath);
            }
        }

        /// <summary>
        /// 把整页 DataBlocks 按网格顺序写入输出流，格式与 ImageDecoder.ReadToFile 一致：
        /// 每个 cell = 前 2 字节长度前缀(大端) + payload + CRC32(4)，只写 payload 部分。
        /// </summary>
        private static void WriteBlocksToStream(ImageDecoder.DecodeResult result, Stream outputStream)
        {
            var blocks = (result.DataBlocks ?? new List<(int row, int col, byte[] data)>())
                .GroupBy(c => (c.row, c.col))
                .Select(g => g.First())
                .OrderBy(c => c.row).ThenBy(c => c.col)
                .ToList();

            foreach (var block in blocks)
            {
                var bytes = block.data;
                if (bytes.Length < 2)
                {
                    outputStream.Write(bytes, 0, bytes.Length);
                    continue;
                }

                int payloadLength = (bytes[0] << 8) | bytes[1];
                int availableLength = bytes.Length - 2 - 4;
                if (payloadLength < 0 || availableLength < 0)
                    throw new Exception("长度前缀非法: row=" + block.row + " col=" + block.col);
                if (payloadLength > availableLength)
                    payloadLength = availableLength;

                if (payloadLength == 0)
                    continue;

                var data = new byte[payloadLength];
                Array.Copy(bytes, 2, data, 0, payloadLength);
                outputStream.Write(data, 0, data.Length);
            }
        }
    }
}
