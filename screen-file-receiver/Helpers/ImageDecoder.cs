using OpenCvSharp;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using Witteborn.ReedSolomon;
using ZXing;
using ZXing.Common;
using ZXing.Datamatrix;
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;
using Size = OpenCvSharp.Size;

namespace screen_file_transmit
{
    public static class ImageDecoder
    {
        private static readonly string rcString = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        private static readonly Encoding Iso88591 = Encoding.GetEncoding("ISO-8859-1");

        // ===== 自适应放大尝试排序 =====
        // 每个「放大变体」用固定名字标识（持久化时存名字而非下标，避免顺序变化导致错配）。
        // 尝试顺序按历史成功率动态排序：成功率高的排前面，退出时保存到配置，下次启动继承。
        public sealed class UpscaleVariant
        {
            public string Name { get; }
            public InterpolationFlags Interp { get; }
            public double Scale { get; }

            public UpscaleVariant(string name, InterpolationFlags interp, double scale)
            {
                Name = name;
                Interp = interp;
                Scale = scale;
            }
        }

        // 所有放大变体（含默认顺序）。顺序在运行期会被成功频率重排。
        private static readonly List<UpscaleVariant> AllVariants = new List<UpscaleVariant>
        {
            new UpscaleVariant("cubic_1x",     InterpolationFlags.Cubic,    1),
            new UpscaleVariant("nearest_2x",   InterpolationFlags.Nearest,  2),
            new UpscaleVariant("lanczos4_2x",  InterpolationFlags.Lanczos4, 2),
            new UpscaleVariant("cubic_2x",     InterpolationFlags.Cubic,    2),
            new UpscaleVariant("cubic_3x",     InterpolationFlags.Cubic,    3),
            new UpscaleVariant("nearest_3x",   InterpolationFlags.Nearest,  3),
        };

        private const int StrategiesPerVariant = 4;

        /// <summary>
        /// 解码并行度：CPU 核心数 - 2，至少 1。
        /// 每个二维码独立解码，可安全并行；留 2 个核心给 UI 与系统。
        /// </summary>
        public static readonly int DecodeParallelism = Math.Max(1, Environment.ProcessorCount - 2);

        // 成功/尝试计数（按「固定 attempt 编号」），线程安全：截图工具与转换流程可能并发解码。
        // attempt 编号 = 变体初始索引 × StrategiesPerVariant + 策略号，共 24 个，固定不变。
        private static readonly object StatLock = new object();
        private static readonly Dictionary<int, long> SuccessCount = new Dictionary<int, long>();
        private static readonly Dictionary<int, long> AttemptCount = new Dictionary<int, long>();

        // 当前尝试顺序（算法名 = 变体名），按各变体下 4 个策略的汇总成功率降序。
        private static List<string> _variantOrder = AllVariants.Select(v => v.Name).ToList();

        /// <summary>当前放大变体的尝试顺序（算法名列表）。</summary>
        public static IReadOnlyList<string> VariantOrder
        {
            get { lock (StatLock) return _variantOrder.ToList(); }
        }

        /// <summary>
        /// 由「初始变体索引 + 策略号」算固定 attempt 编号。
        /// 用 AllVariants 的初始定义位置，不受运行期排序变化影响。
        /// </summary>
        private static int AttemptNo(UpscaleVariant variant, int strategy)
        {
            int idx = AllVariants.IndexOf(variant);
            return idx * StrategiesPerVariant + strategy;
        }

        /// <summary>
        /// 记录某个具体 attempt 的解码结果，用于统计成功频率。
        /// </summary>
        private static void RecordAttempt(int attemptNo, bool success)
        {
            lock (StatLock)
            {
                if (!AttemptCount.ContainsKey(attemptNo)) AttemptCount[attemptNo] = 0;
                if (!SuccessCount.ContainsKey(attemptNo)) SuccessCount[attemptNo] = 0;
                AttemptCount[attemptNo]++;
                if (success) SuccessCount[attemptNo]++;
            }
        }

        /// <summary>
        /// 汇总某变体下 4 个策略 attempt 的成功/尝试总数。
        /// </summary>
        private static void GetVariantTotals(UpscaleVariant variant, out long suc, out long att)
        {
            suc = 0;
            att = 0;
            for (int st = 0; st < StrategiesPerVariant; st++)
            {
                int no = AttemptNo(variant, st);
                if (AttemptCount.TryGetValue(no, out var a)) att += a;
                if (SuccessCount.TryGetValue(no, out var s)) suc += s;
            }
        }

        /// <summary>
        /// 按成功率重新排序变体(算法名)尝试顺序。
        /// 变体排序分 = 该变体下 4 个策略 attempt 的汇总成功率（Σsuc/Σatt）；
        /// 样本不足（Σatt < 2）的排后，避免噪声误导；同分保持默认相对顺序。
        /// </summary>
        private static void ReorderBySuccessRate()
        {
            lock (StatLock)
            {
                _variantOrder = AllVariants
                    .OrderByDescending(v =>
                    {
                        GetVariantTotals(v, out long suc, out long att);
                        if (att < 2) return -1.0; // 样本不足，排后
                        return (double)suc / att;
                    })
                    .ThenBy(v => AllVariants.IndexOf(v)) // 成功率相同时保持默认相对顺序
                    .Select(v => v.Name)
                    .ToList();
            }
        }

        /// <summary>
        /// 导出为配置：Order = 算法名逗号串；stat_attempt_{N} = 每个 attempt 的 suc/att。
        /// </summary>
        public static Dictionary<string, string> ExportStats()
        {
            ReorderBySuccessRate();
            lock (StatLock)
            {
                var d = new Dictionary<string, string>();
                d["Order"] = string.Join(",", _variantOrder);
                int total = AllVariants.Count * StrategiesPerVariant;
                for (int no = 0; no < total; no++)
                {
                    long a = AttemptCount.TryGetValue(no, out var ac) ? ac : 0;
                    long suc = SuccessCount.TryGetValue(no, out var sc) ? sc : 0;
                    d["stat_attempt_" + no] = suc + "/" + a;
                }
                return d;
            }
        }

        /// <summary>
        /// 从配置导入。order 为算法名逗号串；stats 含 stat_attempt_{N}=suc/att 条目。
        /// 兼容旧格式（stat_变体名）时忽略，不参与 attempt 计数。
        /// </summary>
        public static void ImportStats(string order, IEnumerable<KeyValuePair<string, string>> stats)
        {
            lock (StatLock)
            {
                if (!string.IsNullOrWhiteSpace(order))
                {
                    var names = order.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim()).ToList();
                    // 只保留真实存在的算法名，并补上缺失的（放后面）
                    var valid = names.Where(n => AllVariants.Any(v => v.Name == n)).ToList();
                    foreach (var v in AllVariants)
                        if (!valid.Contains(v.Name)) valid.Add(v.Name);
                    if (valid.Count > 0) _variantOrder = valid;
                }

                if (stats != null)
                {
                    foreach (var kv in stats)
                    {
                        // 期望键名 stat_attempt_{N}
                        if (!kv.Key.StartsWith("stat_attempt_")) continue;
                        if (!int.TryParse(kv.Key.Substring("stat_attempt_".Length), out int no)) continue;
                        if (no < 0 || no >= AllVariants.Count * StrategiesPerVariant) continue;

                        var parts = (kv.Value ?? "").Split('/');
                        if (parts.Length == 2 &&
                            long.TryParse(parts[0], out long suc) &&
                            long.TryParse(parts[1], out long att))
                        {
                            SuccessCount[no] = suc;
                            AttemptCount[no] = att;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 从图片读取文件
        /// </summary>
        /// <param name="fileName">图片文件路径</param>
        /// <param name="fileStream">输出流</param>
        /// <param name="outputFileName">解析出的原始文件名（如果存在）</param>
        /// <returns>是否成功</returns>
        public static bool ReadToFile(string fileName, Stream fileStream, bool debug,
            Action<int, int> progress = null)
        {
            var decodeResult = DecodeImageWithMetadata(fileName, debug, progress);
            var meta = decodeResult.Metadata;
            var dataBlocks = decodeResult.DataBlocks;

            if (meta?.Metadata == null || meta.Metadata.Length < 4)
            {
                throw new Exception(string.Format(Properties.Resources.ResourceManager.GetString("Error_MetaNotFound"),
                    fileName));
            }

            if (dataBlocks == null || dataBlocks.Count == 0)
            {
                throw new Exception(string.Format(Properties.Resources.ResourceManager.GetString("Error_NoDataBlocks"),
                    fileName));
            }

            // 用「单元格数」而不是 DecodedQrCodeCount 做完整性判断：
            // 彩色模式下 DecodedQrCodeCount 是单元格数×色层数，两者不是同一个量纲。
            // 注意末尾页可能不满一页——发送端在流读完后即停止填充单元格，
            // 所以不能用 MaxRows*MaxCols 当期望值；期望值应为实际存在的单元格数。
            // 这里只在完全没有解出单元格时才报错，真正的完整性由每 cell 的 CRC32 兜底。
            var distinctBlocks = dataBlocks
                .GroupBy(c => (c.row, c.col))
                .Select(g => g.First())
                .OrderBy(c => c.row).ThenBy(c => c.col)
                .ToList();

            if (distinctBlocks.Count == 0)
            {
                throw new Exception(string.Format(
                    Properties.Resources.ResourceManager.GetString("Error_BlockIncomplete"), 1, 0));
            }

            long offset = fileStream.Position;

            // 按网格顺序排列：cell (row,col) 的文件顺序就是 row*MaxCols+col
            var final = distinctBlocks;

            // 发送端按 cell 顺序流式填充文件，每个 cell 的封装为：
            //   坐标前缀(2) + 长度前缀(2, 大端, 该 cell 的 payloadLength) + payload + CRC32(4)
            // payload 是文件里属于该 cell 的那一段，末尾 cell 可能不满；
            // CRC32 只覆盖该 cell 自己的 payload。
            // 因此校验/剥离的单位是「每个 cell」，长度前缀只决定写多少字节，与二维码内部容量无关。
            // cell (row,col) 的顺序即文件顺序：row = col / MaxCols，col = col % MaxCols。
            fileStream.Position = offset;
            foreach (var block in final)
            {
                var bytes = block.data;
                if (bytes.Length < 2)
                {
                    fileStream.Write(bytes, 0, bytes.Length);
                    continue;
                }

                // 前 2 字节为长度前缀（大端序），表示本 cell 的 payload 长度（不含 CRC32）
                int payloadLength = (bytes[0] << 8) | bytes[1];
                int availableLength = bytes.Length - 2 - 4; // 减去 CRC32
                if (payloadLength < 0 || availableLength < 0)
                {
                    throw new Exception(string.Format(
                        Properties.Resources.ResourceManager.GetString("Error_Crc32Failed"), block.row, block.col));
                }
                if (payloadLength > availableLength)
                    payloadLength = availableLength;

                if (payloadLength == 0)
                    continue;

                var data = new byte[payloadLength];
                Array.Copy(bytes, 2, data, 0, payloadLength);
                var crcBytes = new byte[4];
                Array.Copy(bytes, 2 + payloadLength, crcBytes, 0, 4);
                var computedCrc = Crc32.ComputeHash(data);
                if (!crcBytes.SequenceEqual(computedCrc))
                {
                    throw new Exception(string.Format(
                        Properties.Resources.ResourceManager.GetString("Error_Crc32Failed"), block.row, block.col));
                }

                fileStream.Write(data, 0, data.Length);
            }

            return true;
        }

        /// <summary>
        /// 截取图像左侧或右侧 1/10 区域，水平拉宽 5 倍
        /// </summary>
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
            Cv2.Resize(cropped, stretched, new Size(cropWidth * 3, image.Height), 0, 0, InterpolationFlags.Linear);

            return stretched;
        }

        public static List<string> DetectBarcodes(string imageFile, bool isLeft = true, bool debug = false)
        {
            using (Mat rawImg = Cv2.ImRead(imageFile))
            {
                if (rawImg.Empty())
                {
                    Console.WriteLine("  Failed to load image.");
                    return new List<string>();
                }

                return DetectBarcodes(rawImg, isLeft, debug);
            }
        }

        public static List<string> DetectBarcodes(Mat rawImg, bool isLeft = true, bool debug = false)
        {
            var results = new List<string>();

            var img = StretchSideRegion(rawImg, isLeft);
            Cv2.Rotate(img, img, RotateFlags.Rotate90Counterclockwise);

            double imgWidth = img.Width;
            double imgHeight = img.Height;
            int edgeWidth = (int)imgWidth;

            Mat debugImg = img.Clone();
            var allRects = new List<Rect>();

            Console.WriteLine($"  Image size: {imgWidth}x{imgHeight}");

            int x0 = isLeft ? 0 : (int)imgWidth - edgeWidth;
            var roiRect = new Rect(x0, 0, edgeWidth, (int)imgHeight);
            using (Mat roi = new Mat(img, roiRect))
            {
                var found = DetectBarcodesInRoi(roi, isLeft ? "Left" : "Right", roiRect, false);
                allRects = found;

                var reader = new BarcodeReader();
                reader.Options.TryHarder = true;
                reader.Options.PossibleFormats = new[] { BarcodeFormat.CODE_128 };

                foreach (var r in found)
                {
                    var absRect = new Rect(r.X + x0, r.Y, r.Width, r.Height);
                    var color = isLeft ? new Scalar(0, 0, 255) : new Scalar(0, 255, 255);
                    Cv2.Rectangle(debugImg, absRect, color, 3);

                    using (Mat barcodeRoi = new Mat(img, absRect))
                    using (var bitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(barcodeRoi))
                    {
                        var decodeResult = reader.Decode(bitmap);
                        if (decodeResult != null)
                        {
                            results.Add(decodeResult.Text);
                            Console.WriteLine($"    DECODED: {decodeResult.Text}");
                        }
                    }
                }
            }

            Cv2.PutText(debugImg, $"Detected: {allRects.Count}", new Point(10, 30),
                HersheyFonts.HersheySimplex, 1, new Scalar(0, 0, 255), 2);
            Cv2.PutText(debugImg, $"Decoded: {results.Count}", new Point((int)imgWidth - 250, 30),
                HersheyFonts.HersheySimplex, 1, new Scalar(0, 255, 255), 2);

            Console.WriteLine($"  {(isLeft ? "Left" : "Right")} Barcodes : {allRects.Count}");
            foreach (var r in allRects)
                Console.WriteLine($"    -> [{r.X},{r.Y}] {r.Width}x{r.Height}");
            if (debug)
            {
                if ((isLeft && results.Count < 1) || (!isLeft && results.Count < 2))
                {
                    using (Mat roi = new Mat(img, roiRect))
                        DetectBarcodesInRoi(roi, isLeft ? "Left" : "Right", roiRect, debug);
                    Cv2.ImShow($"{(isLeft ? "Left" : "Right")} Detection Result", debugImg);
                    Cv2.WaitKey();
                }
            }

            debugImg.Dispose();

            return results;
        }

        /// <summary>
        /// 图像解码结果
        /// </summary>
        public class DecodeResult
        {
            public List<(int row, int col, byte[] data)> DataBlocks { get; set; } =
                new List<(int row, int col, byte[] data)>();

            public MetadataResult Metadata { get; set; }
            public int DecodedQrCodeCount { get; set; }

            /// <summary>
            /// 尝试解码的 DataMatrix 二维码总数（黑白=单元格数；彩色=单元格数×通道数），
            /// 用于与元数据的 TotalQrCodeCount 比对以判断解码完整性。
            /// </summary>
            public int DecodedQrCodeAttempts { get; set; }

            /// <summary>
            /// 解码出的各 block 长度前缀之和（本页实际承载的文件字节数）。
            /// </summary>
            public long DecodedPayloadBytes { get; set; }

            /// <summary>
            /// 单个 cell 的满载容量（取各 block payloadLength 的最大值）。
            /// </summary>
            public int SingleCellCapacity { get; set; }

            /// <summary>
            /// 是否「非末页不满载」：非末页时解码字节数小于满页容量。
            /// 末页天然不满属正常，不算。仅用于标记，不影响解码成败。
            /// </summary>
            public bool IsUnderfull { get; set; }
        }

        /// <summary>
        /// 元数据读取结果
        /// </summary>
        public class MetadataResult
        {
            public byte[] Metadata { get; set; }
            public string FileName { get; set; }
            public string FileId { get; set; }

            // 解析后的元数据含义
            public int MaxRows { get; set; }

            public int MaxCols { get; set; }
            public bool Colorful { get; set; }
            public bool HasPassword { get; set; }
            public bool HasErrorCorrection { get; set; }
            public int ErrorCorrectionPercent { get; set; }
            public int ColorDepth { get; set; }
            public int CurrentPage { get; set; }
            public int TotalPages { get; set; }
            public int TotalQrCodeCount { get; set; }
        }

        /// <summary>
        /// 从图片解码所有数据块（包含元数据）
        /// </summary>
        public static DecodeResult DecodeImageWithMetadata(string imageFile, bool debug,
            Action<int, int> progress = null)
        {
            using (Mat image = Cv2.ImRead(imageFile))
            {
                if (image.Empty())
                {
                    throw new Exception(string.Format(
                        Properties.Resources.ResourceManager.GetString("Error_LoadImageFailed"), imageFile));
                }

                return DecodeImageWithMetadata(image, debug, progress);
            }
        }

        /// <summary>
        /// 从 Bitmap 解码所有数据块（包含元数据）
        /// </summary>
        public static DecodeResult DecodeImageWithMetadata(Bitmap bitmap, bool debug,
            Action<int, int> progress = null)
        {
            using (Mat image = OpenCvSharp.Extensions.BitmapConverter.ToMat(bitmap))
            {
                return DecodeImageWithMetadata(image, debug, progress);
            }
        }

        private static DecodeResult DecodeImageWithMetadata(Mat image, bool debug,
            Action<int, int> progress = null)
        {
            var result = new DecodeResult();

            result.Metadata = ReadMetadata(image, debug);

            var dataMatrixContours = FindDataMatrixContours(image);

            bool isColorful = result.Metadata?.Colorful ?? false;
            if (isColorful)
            {
                result.DataBlocks = DecodeColorfulMode(image, dataMatrixContours, debug, out int decodedCount,
                    out int attempts, progress);
                result.DecodedQrCodeCount = decodedCount;
                result.DecodedQrCodeAttempts = attempts;
            }
            else
            {
                result.DataBlocks = DecodeGrayscaleMode(image, dataMatrixContours, debug, out int attempts, progress);
                result.DecodedQrCodeCount = result.DataBlocks.Count;
                result.DecodedQrCodeAttempts = attempts;
            }

            if (result.Metadata?.HasErrorCorrection == true && result.DataBlocks != null && result.DataBlocks.Count > 0)
            {
                result.DataBlocks = DecodeWithReedSolomon(result.DataBlocks, result.Metadata);
            }

            ComputeDecodeLength(result);

            return result;
        }

        /// <summary>
        /// 计算解码后 length 自洽信息并标记「非末页不满载」。
        ///
        /// 公式：
        ///   L    = Σ payloadLength_i            各 block 长度前缀之和（本页承载的文件字节数）
        ///   cap  = max(payloadLength_i)         单 cell 满载容量（非末页逐 cell 填满）
        ///   Cmax = MaxRows × MaxCols × cap      本页满载容量
        ///   非末页 (CurrentPage &lt; TotalPages) 且 L &lt; Cmax → IsUnderfull
        ///   末页天然不满属正常，不标记。
        ///
        /// 仅用于标记，不影响解码成败/翻页门禁。
        /// </summary>
        private static void ComputeDecodeLength(DecodeResult result)
        {
            if (result == null || result.DataBlocks == null) return;

            long total = 0;
            int cap = 0;
            foreach (var block in result.DataBlocks)
            {
                var bytes = block.data;
                if (bytes == null || bytes.Length < 6) continue;
                int payloadLength = (bytes[0] << 8) | bytes[1];
                if (payloadLength <= 0 || payloadLength + 4 > bytes.Length - 2) continue;
                total += payloadLength;
                if (payloadLength > cap) cap = payloadLength;
            }

            result.DecodedPayloadBytes = total;
            result.SingleCellCapacity = cap;

            var meta = result.Metadata;
            bool isLastPage = IsLastPage(meta);

            if (!isLastPage && cap > 0 && meta?.MaxRows > 0 && meta.MaxCols > 0)
            {
                long cmax = (long)meta.MaxRows * meta.MaxCols * cap;
                result.IsUnderfull = total < cmax;
            }
            else
            {
                result.IsUnderfull = false;
            }
        }

        /// <summary>
        /// 判断本页是否末页。
        /// 优先用元数据的 totalQrCodeCount（字节7-8 = 发送端实际生成的 cell 数 ×层数）：
        /// 末页发送端流读完即停，cell 数会小于满页 cell 数，是数据层面的事实。
        /// 满页 cell 数 = MaxRows×MaxCols（黑白）或 ×colorDepth×3（彩色，totalQrCodeCount 已含）。
        /// 退回页码声明 CurrentPage &gt;= TotalPages 作兜底。
        /// </summary>
        private static bool IsLastPage(MetadataResult meta)
        {
            if (meta == null) return false;

            // 数据事实优先：totalQrCodeCount 小于满页应有数 → 末页不满
            if (meta.TotalQrCodeCount > 0 && meta.MaxRows > 0 && meta.MaxCols > 0)
            {
                // totalQrCodeCount = count × (colorful?3:1)，count 为实际 cell 数
                int cellsPerLayer = meta.MaxRows * meta.MaxCols;
                int layers = meta.Colorful ? 3 : 1;
                int fullCount = cellsPerLayer * layers;
                if (meta.TotalQrCodeCount < fullCount) return true;
            }

            // 兜底：页码声明
            return meta.TotalPages > 0 && meta.CurrentPage >= meta.TotalPages;
        }

        /// <summary>
        /// 校验单页解码结果是否完整可靠（块数、坐标、CRC32）。
        /// 用于自动翻页：元数据可读不代表数据区解码无误，必须通过本校验才允许翻页。
        /// </summary>
        /// <param name="result">整页解码结果</param>
        /// <param name="qrCodeRatio">DataMatrix 二维码解码成功率阈值（0-1），元数据未给出总数时按块数估算</param>
        /// <returns>true 表示本页数据可安全保存</returns>
        public static bool VerifyDecodeResult(DecodeResult result, double qrCodeRatio = 0.95)
        {
            return VerifyDecodeResult(result, null, qrCodeRatio);
        }

        /// <summary>
        /// 校验单页解码结果是否完整可靠（块数、坐标、CRC32）。
        /// </summary>
        /// <param name="result">整页解码结果</param>
        /// <param name="meta">元数据（为空时使用 result.Metadata）</param>
        /// <param name="qrCodeRatio">DataMatrix 二维码解码成功率阈值（0-1）</param>
        public static bool VerifyDecodeResult(DecodeResult result, MetadataResult meta, double qrCodeRatio = 0.95)
        {
            if (result == null) return false;

            meta = meta ?? result.Metadata;
            var blocks = result.DataBlocks;
            if (blocks == null || blocks.Count == 0) return false;

            if (meta != null && meta.MaxRows > 0 && meta.MaxCols > 0)
            {
                // 与 ReadToFile 一致：单元格必须齐全，否则长度链条断裂、重组会错位
                if (blocks.Count < meta.MaxRows * meta.MaxCols) return false;
            }

            if (meta != null && meta.TotalQrCodeCount > 0)
            {
                // 元数据记录的是本页应解出的 DataMatrix 二维码总数。
                // DecodedQrCodeCount 仅统计「合并前」成功解出的层数（彩色模式下同一单元格的
                // 多层会合并成一块，但每层各自计数），因此这里以实际尝试解码的二维码数为基准
                // 判断解码成功率，避免把已合并完整的单元格误判为失败。
                int attempts = result.DecodedQrCodeAttempts > 0
                    ? result.DecodedQrCodeAttempts
                    : result.DecodedQrCodeCount;
                if (attempts < meta.TotalQrCodeCount * qrCodeRatio) return false;
            }

            var seen = new HashSet<(int row, int col)>();
            foreach (var block in blocks)
            {
                if (!seen.Add((block.row, block.col))) return false; // 坐标重复
                if (block.data == null || block.data.Length < 6) return false;

                // 前 2 字节为长度前缀（大端序），其后为 payload + CRC32(4)
                int payloadLength = (block.data[0] << 8) | block.data[1];
                if (payloadLength <= 0 || payloadLength + 4 > block.data.Length - 2) return false;

                var data = new byte[payloadLength];
                Array.Copy(block.data, 2, data, 0, payloadLength);
                var crcBytes = new byte[4];
                Array.Copy(block.data, 2 + payloadLength, crcBytes, 0, 4);
                if (!crcBytes.SequenceEqual(Crc32.ComputeHash(data))) return false;
            }

            return true;
        }

        private static (int dataShards, int parityShards) GetRsShardCounts(int errorCorrectionPercent)
        {
            switch (errorCorrectionPercent)
            {
                case 5: return (19, 1);
                case 10: return (9, 1);
                case 15: return (17, 3);
                case 20: return (4, 1);
                case 25: return (3, 1);
                case 30: return (7, 3);
                case 35: return (13, 7);
                case 40: return (3, 2);
                case 45: return (11, 9);
                case 50: return (1, 1);
                default: return (0, 0);
            }
        }

        /// <summary>
        /// 对每个 DataMatrix 数据块进行 Reed-Solomon 解码
        /// </summary>
        private static List<(int row, int col, byte[] data)> DecodeWithReedSolomon(
            List<(int row, int col, byte[] data)> dataBlocks, MetadataResult meta)
        {
            var (dataShards, parityShards) = GetRsShardCounts(meta.ErrorCorrectionPercent);
            int totalShards = dataShards + parityShards;
            if (dataShards <= 0 || parityShards <= 0) return dataBlocks;

            var result = new List<(int row, int col, byte[] data)>();
            foreach (var block in dataBlocks)
            {
                if (block.data.Length < 2)
                {
                    result.Add(block);
                    continue;
                }

                // 分离长度前缀和 RS 编码数据
                byte[] lenPrefix = new byte[2];
                Array.Copy(block.data, 0, lenPrefix, 0, 2);
                int rsDataLength = block.data.Length - 2;

                if (rsDataLength % totalShards != 0)
                {
                    result.Add(block);
                    continue;
                }

                int shardSize = rsDataLength / totalShards;
                byte[][] shards = new byte[totalShards][];
                bool[] present = new bool[totalShards];
                for (int i = 0; i < totalShards; i++)
                {
                    shards[i] = new byte[shardSize];
                    Array.Copy(block.data, 2 + i * shardSize, shards[i], 0, shardSize);
                    present[i] = true;
                }

                try
                {
                    var rs = new ReedSolomon(dataShards, parityShards);
                    rs.DecodeMissing(shards, present, 0, shardSize);
                    using (var ms = new MemoryStream())
                    {
                        ms.Write(lenPrefix, 0, 2); // 重新附加长度前缀
                        for (int i = 0; i < dataShards; i++)
                        {
                            ms.Write(shards[i], 0, shardSize);
                        }

                        result.Add((block.row, block.col, ms.ToArray()));
                    }
                }
                catch
                {
                    result.Add(block);
                }
            }

            return result;
        }

        ///// <summary>
        ///// 从图片解码所有数据块（向后兼容的简化版本）
        ///// </summary>
        //public static List<(int row, int col, byte[] data)> DecodeImage(string imageFile)
        //{
        //    var result = DecodeImageWithMetadata(imageFile);
        //    return result.DataBlocks;
        //}

        /// <summary>
        /// 查找所有 DataMatrix 二维码轮廓
        /// </summary>
        /// <summary>
        /// 估算图像中 DataMatrix 的模块边长（像素）。
        /// </summary>
        private static double EstimateModuleSize(Mat gray)
        {
            int w = gray.Width, h = gray.Height;
            if (w < 16 || h < 16) return 4;

            var counts = new Dictionary<int, int>();
            int stepY = Math.Max(1, h / 40);

            for (int y = stepY; y < h - stepY; y += stepY)
            {
                int run = 1;
                bool prev = gray.At<byte>(y, 0) < 128;
                for (int x = 1; x < w; x++)
                {
                    bool cur = gray.At<byte>(y, x) < 128;
                    if (cur == prev) run++;
                    else
                    {
                        if (run >= 1 && run <= 24) counts[run] = counts.TryGetValue(run, out var c) ? c + 1 : 1;
                        run = 1;
                        prev = cur;
                    }
                }
            }

            if (counts.Count == 0) return 4;

            int best = 4, bestCount = -1;
            foreach (var kv in counts)
                if (kv.Value > bestCount) { bestCount = kv.Value; best = kv.Key; }
            return Math.Max(1, best);
        }

        /// <summary>
        /// 定位 DataMatrix 单元格边界。
        ///
        /// 核心思路：DataMatrix 有清晰的黑色 L 形实线边框，直接取外轮廓即可，
        /// 不需要闭运算（闭运算反而会把相邻 DataMatrix 合并）。
        ///
        /// 相比旧实现的改进：
        /// 1. 用 Otsu 自适应阈值，适应低对比度/偏色图像；
        /// 2. 用模块尺寸估算最小面积，代替硬编码 100/15 像素；
        /// 3. 用聚类估计预期的单元格尺寸，过滤异常大/小的轮廓；
        /// 4. 多阈值取并集，宁可多检也不漏检。
        /// </summary>
        private static List<Rect> FindDataMatrixContours(Mat image)
        {
            using (Mat gray = new Mat())
            {
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);

                double module = EstimateModuleSize(gray);
                double minCell = module * 10;

                var candidates = new List<Rect>();

                // 多阈值策略
                foreach (var threshVal in new int[] { -1, 127, 180, 90, 150, 110 })
                {
                    using (Mat thresh = new Mat())
                    {
                        if (threshVal < 0)
                            Cv2.Threshold(gray, thresh, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
                        else
                            Cv2.Threshold(gray, thresh, threshVal, 255, ThresholdTypes.BinaryInv);

                        Cv2.FindContours(thresh, out Point[][] contours, out HierarchyIndex[] _,
                            RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                        foreach (var c in contours)
                        {
                            Rect r = Cv2.BoundingRect(c);
                            if (r.Width < minCell || r.Height < minCell) continue;

                            double ratio = r.Width / (double)r.Height;
                            if (ratio < 0.5 || ratio > 2.0) continue;

                            candidates.Add(r);
                        }
                    }
                }

                if (candidates.Count == 0) return new List<Rect>();

                // 合并近邻矩形（gap 必须远小于 DataMatrix 之间的间隔，否则会把相邻单元格粘住）
                candidates = MergeNearbyRects(candidates, (int)module);

                // 估计预期的单元格尺寸（取中位数面积），过滤过大/过小
                var areas = candidates.Select(r => r.Width * (double)r.Height).OrderBy(a => a).ToList();
                double medianArea = areas[areas.Count / 2];

                return candidates
                    .Where(r =>
                    {
                        double area = r.Width * (double)r.Height;
                        return area > medianArea * 0.4 && area < medianArea * 2.5;
                    })
                    .OrderBy(r => r.Y).ThenBy(r => r.X)
                    .ToList();
            }
        }

        /// <summary>
        /// 合并空间上重叠或贴近的矩形，避免一个 DataMatrix 被拆成多个框。
        /// </summary>
        private static List<Rect> MergeNearbyRects(List<Rect> rects, int gap)
        {
            if (rects.Count <= 1) return rects;

            var list = rects.ToList();
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < list.Count && !changed; i++)
                {
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        var a = list[i];
                        var b = list[j];
                        bool overlap = a.Left - gap < b.Right && b.Left - gap < a.Right &&
                                       a.Top - gap < b.Bottom && b.Top - gap < a.Bottom;
                        if (!overlap) continue;

                        int x1 = Math.Min(a.Left, b.Left);
                        int y1 = Math.Min(a.Top, b.Top);
                        int x2 = Math.Max(a.Right, b.Right);
                        int y2 = Math.Max(a.Bottom, b.Bottom);
                        list[i] = new Rect(x1, y1, x2 - x1, y2 - y1);
                        list.RemoveAt(j);
                        changed = true;
                        break;
                    }
                }
            }

            return list;
        }

        /// <summary>
        /// 黑白模式解码
        /// </summary>
        private static List<(int row, int col, byte[] data)> DecodeGrayscaleMode(Mat image, List<Rect> contours,
            bool debug, out int qrCodeAttempts, Action<int, int> progress = null)
        {
            // 每个轮廓对应一个 DataMatrix 二维码
            qrCodeAttempts = contours.Count;

            // 并行解码：各二维码彼此独立，只读同一个 image，安全
            var decoded = DecodeInParallel(contours.Count,
                i => DecodeDataMatrixAt(image, contours[i], debug), progress);

            var results = new List<(int row, int col, byte[] data)>();
            var rowCols = new HashSet<(int, int)>();
            foreach (var item in decoded)
            {
                if (item == null) continue;
                if (!rowCols.Add((item.Value.row, item.Value.col))) continue;
                results.Add(item.Value);
            }

            return results.OrderBy(x => x.row).ThenBy(x => x.col).ToList();
        }

        /// <summary>
        /// 彩色模式解码 - 分离 R/G/B 通道分别解码
        /// </summary>
        private static List<(int row, int col, byte[] data)> DecodeColorfulMode(Mat image, List<Rect> contours,
            bool debug, out int decodedQrCodeCount, out int qrCodeAttempts, Action<int, int> progress = null)
        {
            Mat[] channels = new Mat[3];
            Cv2.Split(image, out channels);

            // OpenCV 分割顺序为 BGR，需重排为 RGB 以匹配发送端 (red, green, blue)
            var rgbChannels = new[] { channels[2], channels[1], channels[0] };
            decodedQrCodeCount = 0;
            // 每个轮廓 × 3 个颜色通道 = 3 个 DataMatrix 二维码
            qrCodeAttempts = contours.Count * 3;

            try
            {
                // 并行解码：各轮廓独立，channel 只读，DataMatrixReader 每次调用新建，安全
                var perContour = DecodeInParallel(contours.Count, i =>
                {
                    var rect = contours[i];
                    var blockResults = new List<(int layer, int row, int col, byte[] data)>();
                    for (int layer = 0; layer < 3; layer++)
                    {
                        var decoded = DecodeDataMatrixFromChannel(rgbChannels[layer], rect, layer, debug);
                        if (decoded != null)
                        {
                            blockResults.Add((layer, decoded.Value.row, decoded.Value.col, decoded.Value.data));
                        }
                    }

                    return blockResults.Count > 0 ? blockResults : null;
                }, progress);

                var results = new List<(int row, int col, byte[] data)>();
                foreach (var blockResults in perContour)
                {
                    if (blockResults == null) continue;
                    decodedQrCodeCount += blockResults.Count;
                    results.Add(MergeLayerData(blockResults));
                }

                return results;
            }
            finally
            {
                foreach (var ch in channels) ch.Dispose();
            }
        }

        /// <summary>
        /// 用「CPU 核心数 - 2」个线程并行解码一组独立的二维码，
        /// 结果按原始顺序返回；每完成一个回调一次 progress(已完成数, 总数)。
        /// </summary>
        private static T[] DecodeInParallel<T>(int count, Func<int, T> work, Action<int, int> progress)
        {
            var results = new T[count];
            if (count == 0) return results;

            int done = 0;
            int dop = Math.Min(DecodeParallelism, count);

            // 数量太少没必要起线程池
            if (dop <= 1)
            {
                for (int i = 0; i < count; i++)
                {
                    results[i] = work(i);
                    int d = Interlocked.Increment(ref done);
                    progress?.Invoke(d, count);
                }

                return results;
            }

            int next = -1;
            var workers = new Task[dop];
            for (int w = 0; w < dop; w++)
            {
                workers[w] = Task.Run(() =>
                {
                    while (true)
                    {
                        int i = Interlocked.Increment(ref next);
                        if (i >= count) break;

                        results[i] = work(i);
                        int d = Interlocked.Increment(ref done);
                        progress?.Invoke(d, count);
                    }
                });
            }

            Task.WaitAll(workers);
            return results;
        }

        /// <summary>
        /// 从单通道解码 DataMatrix
        /// </summary>
        private static (int row, int col, byte[] data)? DecodeDataMatrixFromChannel(
            Mat channel, Rect rect, int layer, bool debug = false)
        {
            int padding = 5;
            int x = Math.Max(0, rect.X - padding);
            int y = Math.Max(0, rect.Y - padding);
            int w = Math.Min(channel.Width - x, rect.Width + 2 * padding);
            int h = Math.Min(channel.Height - y, rect.Height + 2 * padding);

            using (Mat roi = new Mat(channel, new Rect(x, y, w, h)))
            {
                using (Mat binary = new Mat())
                {
                    int threshold = 127;
                    Cv2.Threshold(roi, binary, threshold, 255, ThresholdTypes.Binary);

                    using (Mat bgr = new Mat())
                    {
                        Cv2.CvtColor(binary, bgr, ColorConversionCodes.GRAY2BGR);
                        string decoded = TryDecodeDataMatrix(bgr);

                        if (!string.IsNullOrEmpty(decoded) && decoded.Length >= 2)
                        {
                            int row = rcString.IndexOf(decoded[0]);
                            int col = rcString.IndexOf(decoded[1]);

                            if (row >= 0 && col >= 0)
                            {
                                string payload = decoded.Substring(2);
                                byte[] bytes = Iso88591.GetBytes(payload);
                                return (row, col, bytes);
                            }
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 在指定位置解码 DataMatrix（用于黑白模式）
        /// </summary>
        private static (int row, int col, byte[] data)? DecodeDataMatrixAt(Mat image, Rect rect, bool debug)
        {
            int padding = 5;
            int x = Math.Max(0, rect.X - padding);
            int y = Math.Max(0, rect.Y - padding);
            int w = Math.Min(image.Width - x, rect.Width + 2 * padding);
            int h = Math.Min(image.Height - y, rect.Height + 2 * padding);

            using (Mat roi = new Mat(image, new Rect(x, y, w, h)))
            {
                string decoded = TryDecodeDataMatrix(roi, debug);

                if (!string.IsNullOrEmpty(decoded) && decoded.Length >= 2)
                {
                    int row = rcString.IndexOf(decoded[0]);
                    int col = rcString.IndexOf(decoded[1]);

                    if (row >= 0 && col >= 0)
                    {
                        string payload = decoded.Substring(2);
                        byte[] bytes = Iso88591.GetBytes(payload);
                        return (row, col, bytes);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 合并多层数据
        /// </summary>
        private static (int row, int col, byte[] data) MergeLayerData(
            List<(int layer, int row, int col, byte[] data)> layerResults)
        {
            var sorted = layerResults.OrderBy(l => l.layer).ToList();
            var first = sorted[0];

            using (var ms = new MemoryStream())
            {
                // 从第一层提取长度前缀
                if (first.data.Length >= 2)
                {
                    ms.Write(first.data, 0, 2);
                }

                // 每层只拼接数据部分（去掉长度前缀）
                foreach (var layer in sorted)
                {
                    if (layer.data.Length > 2)
                    {
                        ms.Write(layer.data, 2, layer.data.Length - 2);
                    }
                    else if (layer.data.Length > 0)
                    {
                        ms.Write(layer.data, 0, layer.data.Length);
                    }
                }

                return (first.row, first.col, ms.ToArray());
            }
        }

        /// <summary>
        /// 尝试解码 DataMatrix
        /// </summary>
        /// <summary>
        /// 尝试解码 DataMatrix。按「历史成功率」动态排序的放大变体依次尝试，
        /// 每个变体再配 4 种预处理策略。成功即返回并记录该变体的成功统计。
        /// </summary>
        public static string TryDecodeDataMatrix(Mat image, bool debug = false)
        {
            // ZXing 的 DataMatrixReader 内部状态可变，非线程安全：
            // 截图工具与转换流程并发调用时共享实例会导致偶发解不出，故每次调用新建。
            var dmReader = new DataMatrixReader();

            // 取当前排序（成功率高的在前），未统计到的保持默认顺序
            List<string> order = VariantOrder.ToList();
            var variantByName = AllVariants.ToDictionary(v => v.Name);

            foreach (var vName in order)
            {
                var variant = variantByName[vName];
                for (int strategy = 0; strategy < StrategiesPerVariant; strategy++)
                {
                    // 固定 attempt 编号（按初始变体定义），与运行期排序无关
                    int attemptNo = AttemptNo(variant, strategy);

                    using (Mat processed = PreprocessForDecode(image, variant, strategy))
                    {
                        using (var bitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(processed))
                        {
                            var source = new BitmapLuminanceSource(bitmap);
                            var binarizer = new HybridBinarizer(source);
                            var binaryBitmap = new BinaryBitmap(binarizer);
                            var hints = new Dictionary<DecodeHintType, object>
                            {
                                { DecodeHintType.CHARACTER_SET, "ISO-8859-1" },
                                { DecodeHintType.TRY_HARDER, true }
                            };
                            var result = dmReader.decode(binaryBitmap, hints);

                            if (result != null && result.BarcodeFormat == BarcodeFormat.DATA_MATRIX)
                            {
                                // 记录命中的这个具体 attempt 成功
                                RecordAttempt(attemptNo, true);
                                ReorderBySuccessRate();
                                return result.Text;
                            }
                        }
                    }

                    // 该 attempt 未命中，记一次失败
                    RecordAttempt(attemptNo, false);
                }
            }

            if (debug)
            {
                image.SaveImage(Directory.GetCurrentDirectory() + "/dm.png");
                Cv2.ImShow("DM", image);
                Cv2.WaitKey();
            }

            return null;
        }

        public static void SaveBinaryBitmapAsImage(BinaryBitmap binaryBitmap, string filePath)
        {
            try
            {
                if (binaryBitmap == null)
                {
                    Console.WriteLine("SaveBinaryBitmapAsImage: binaryBitmap is null");
                    return;
                }

                // 获取内部的二值化位图 - 这里可能抛出异常
                var bitMatrix = binaryBitmap.BlackMatrix;

                if (bitMatrix == null)
                {
                    Console.WriteLine("SaveBinaryBitmapAsImage: BlackMatrix is null");
                    return;
                }

                int width = bitMatrix.Width;
                int height = bitMatrix.Height;

                if (width <= 0 || height <= 0)
                {
                    Console.WriteLine($"SaveBinaryBitmapAsImage: Invalid dimensions {width}x{height}");
                    return;
                }

                using (var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format1bppIndexed))
                {
                    // 设置调色板
                    var palette = bitmap.Palette;
                    palette.Entries[0] = Color.Black;
                    palette.Entries[1] = Color.White;
                    bitmap.Palette = palette;

                    // 写入像素数据 - 注意 Format1bppIndexed 需要特殊处理
                    // 方案A：使用 LockBits 高效写入
                    var data = bitmap.LockBits(
                        new Rectangle(0, 0, width, height),
                        System.Drawing.Imaging.ImageLockMode.WriteOnly,
                        System.Drawing.Imaging.PixelFormat.Format1bppIndexed);

                    try
                    {
                        byte[] bytes = new byte[data.Stride * height];
                        for (int y = 0; y < height; y++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                if (bitMatrix[x, y]) // true = 黑
                                {
                                    // 设置对应位为 0（黑色）
                                }
                                else
                                {
                                    // 设置对应位为 1（白色）
                                    int byteIndex = y * data.Stride + (x >> 3);
                                    int bitIndex = 7 - (x & 7);
                                    bytes[byteIndex] |= (byte)(1 << bitIndex);
                                }
                            }
                        }

                        System.Runtime.InteropServices.Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
                    }
                    finally
                    {
                        bitmap.UnlockBits(data);
                    }

                    bitmap.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
                    Console.WriteLine($"Saved binary bitmap to {filePath}, size={width}x{height}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SaveBinaryBitmapAsImage failed: {ex.Message}");
                // 不重新抛出异常，避免影响主流程
            }
        }

        /// <summary>
        /// 解码预处理
        /// </summary> 
        /// <summary>
        /// 对指定放大变体做指定策略的预处理。
        /// </summary>
        private static Mat PreprocessForDecode(Mat image, UpscaleVariant variant, int strategy)
        {
            // === 1. 放大 ===
            Mat scaled = new Mat();
            Cv2.Resize(image, scaled, new Size(), variant.Scale, variant.Scale, variant.Interp);

            // === 2. 灰度 ===
            // BGRA(4通道) 也要转灰度，否则后续 OTSU/自适应阈值会抛 "src_type is CV_8UC4"
            Mat gray = new Mat();
            if (scaled.Channels() == 4)
                Cv2.CvtColor(scaled, gray, ColorConversionCodes.BGRA2GRAY);
            else if (scaled.Channels() == 3)
                Cv2.CvtColor(scaled, gray, ColorConversionCodes.BGR2GRAY);
            else
                gray = scaled.Clone();

            // === 3. 每个放大变体都尝试同样的 4 种预处理策略 ===
            Mat result = new Mat();
            switch (strategy)
            {
                case 0:
                    // 直接灰度，不做二值化
                    result = gray.Clone();
                    break;

                case 1:
                    // OTSU 自适应二值化
                    Cv2.Threshold(gray, result, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
                    break;

                case 2:
                    // 自适应阈值（应对光照不均）
                    Cv2.AdaptiveThreshold(gray, result, 255,
                        AdaptiveThresholdTypes.GaussianC,
                        ThresholdTypes.Binary,
                        11, 2);
                    break;

                case 3:
                    // 锐化（补模块边缘）
                    using (Mat kernelSharpen = Mat.FromArray(new float[,]
                           {
                               { -1, -1, -1 },
                               { -1, 9, -1 },
                               { -1, -1, -1 }
                           }))
                    {
                        Cv2.Filter2D(gray, result, -1, kernelSharpen);
                    }
                    break;

                default:
                    result = gray.Clone();
                    break;
            }

            return result;
        }
        //private static Mat PreprocessForDecode(Mat image, int attempt)
        //{
        //    if (attempt == 0)
        //    {
        //        return image.Clone();
        //    }

        //    Mat result = new Mat();

        //    if (attempt <= 2)
        //    {
        //        return image.Clone();
        //        //Cv2.Resize(image, result, new Size(), 1 + attempt, 1 + attempt, InterpolationFlags.Linear);
        //    }

        //    Mat resize = new Mat();
        //    Cv2.Resize(image, resize, new Size(), 3, 3, InterpolationFlags.Linear);

        //    if (attempt == 3)
        //    {
        //        using (Mat gray = new Mat())
        //        {
        //            if (resize.Channels() == 3)
        //                Cv2.CvtColor(resize, gray, ColorConversionCodes.BGR2GRAY);
        //            else
        //                resize.CopyTo(gray);

        //            Cv2.AdaptiveThreshold(gray, result, 256, AdaptiveThresholdTypes.GaussianC,
        //                ThresholdTypes.Binary, 11, 2);
        //        }
        //    }
        //    else if (attempt == 4)
        //    {
        //        Mat kernel = Mat.FromArray(new float[,] {
        //            { 0, -1, 0 },
        //            { -1, 5, -1 },
        //            { 0, -1, 0 }
        //        });
        //        Cv2.Filter2D(resize, result, -1, kernel);
        //    }
        //    else if (attempt == 5)
        //    {
        //        Mat kernel = Mat.FromArray(new float[,]
        //        {
        //            { -1, -1, -1 },
        //            { -1, 9, -1 },
        //            { -1, -1, -1 }
        //        });
        //        Cv2.Filter2D(resize, result, -1, kernel);
        //    }
        //    else if (attempt == 6)
        //    {
        //        Mat kernel = Mat.FromArray(new float[,]{
        //            { -1, -1, -1 },
        //            { -1, 10, -1 },
        //            { -1, -1, -1 }
        //        });
        //        Cv2.Filter2D(resize, result, -1, kernel);
        //    }
        //    else
        //    {
        //        return image.Clone();
        //    }

        //    return result; 
        //}

        private static bool IsBase64String(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length % 4 != 0)
                return false;
            return s.All(c =>
                (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '+' || c == '/' ||
                c == '=');
        }

        private static string ParseTimestamp(string base64Timestamp)
        {
            try
            {
                var bytes = Convert.FromBase64String(base64Timestamp);
                long unixSeconds;
                if (bytes.Length == 8)
                {
                    unixSeconds = BitConverter.ToInt64(bytes, 0);
                }
                else
                {
                    // 尝试直接解析为数字（兼容旧格式）
                    unixSeconds = long.Parse(Encoding.UTF8.GetString(bytes));
                }

                var dt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
                return dt.ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch
            {
                return base64Timestamp;
            }
        }

        /// <summary>
        /// 使用 DetectBarcodes 读取图像左右两侧条码，返回文件名文本、元数据、文件名、时间戳
        /// </summary>
        public static MetadataResult ReadMetadata(string imageFile, bool debug = false)
        {
            using (Mat rawImg = Cv2.ImRead(imageFile))
            {
                if (rawImg.Empty())
                    return new MetadataResult();
                return ReadMetadata(rawImg, debug);
            }
        }

        public static MetadataResult ReadMetadata(Bitmap bitmap, bool debug = false)
        {
            using (Mat mat = OpenCvSharp.Extensions.BitmapConverter.ToMat(bitmap))
            {
                return ReadMetadata(mat, debug);
            }
        }

        public static MetadataResult ReadMetadata(Mat image, bool debug = false)
        {
            var result = new MetadataResult();

            // 左侧：元数据条码（$ 开头，Base64 编码）
            var leftCodes = DetectBarcodes(image, isLeft: true, debug);
            foreach (var code in leftCodes)
            {
                if (code.StartsWith("$"))
                {
                    try
                    {
                        var base64Part = code.TrimStart('$').TrimStart('-');
                        result.Metadata = Convert.FromBase64String(base64Part);

                        if (result.Metadata != null && result.Metadata.Length >= 6)
                        {
                            result.MaxRows = (result.Metadata[0] >> 4) & 0x0F;
                            result.MaxCols = result.Metadata[0] & 0x0F;
                            result.Colorful = (result.Metadata[1] & 0x80) != 0;
                            result.HasPassword = (result.Metadata[1] & 0x40) != 0;
                            result.HasErrorCorrection = (result.Metadata[1] & 0x20) != 0;
                            result.ColorDepth = result.Metadata[1] & 0x1F;
                            result.CurrentPage = (result.Metadata[2] << 8) | result.Metadata[3];
                            result.TotalPages = (result.Metadata[4] << 8) | result.Metadata[5];
                            if (result.Metadata.Length >= 7)
                            {
                                result.ErrorCorrectionPercent = result.Metadata[6];
                            }

                            if (result.Metadata.Length >= 9)
                            {
                                result.TotalQrCodeCount = (result.Metadata[7] << 8) | result.Metadata[8];
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }

            if (result.Metadata == null)
            {
                throw new Exception("Cannot find page meta data");
            }

            // 右侧：文件名条码、文件ID条码（#fileId#page 格式）
            var rightCodes = DetectBarcodes(image, isLeft: false, debug);
            if (rightCodes.Count >= 2)
            {
                var ordered = rightCodes.OrderByDescending(c => c.Length).ToList();
                var candidateFileId = ordered.FirstOrDefault(c => c.StartsWith("#"));
                if (candidateFileId != null)
                {
                    // 解析 #fileId#page 格式
                    var parts = candidateFileId.Split('#');
                    if (parts.Length >= 3 && !string.IsNullOrEmpty(parts[1]))
                    {
                        result.FileId = parts[1];
                        if (int.TryParse(parts[2], out int pageFromBarcode))
                        {
                            if (pageFromBarcode != result.CurrentPage)
                            {
                                throw new Exception(string.Format(
                                    "Page mismatch: barcode page {0} != metadata page {1}", pageFromBarcode,
                                    result.CurrentPage));
                            }
                        }
                    }
                    else
                    {
                        result.FileId = candidateFileId.TrimStart('#');
                    }

                    result.FileName = ordered.First(c => c != candidateFileId);
                }
                else
                {
                    result.FileId = ordered[0].TrimStart('#');
                    result.FileName = ordered[1];
                }
            }
            else
            {
                throw new Exception("Cannot find file meta data");
            }

            return result;
        }

        private static List<Rect> MergeVerticallyAlignedRects(List<Rect> rects, int yGapThreshold)
        {
            if (rects.Count == 0) return rects;

            var sorted = rects.OrderBy(r => r.Y).ToList();
            var merged = new List<Rect>();
            var current = sorted[0];

            for (int i = 1; i < sorted.Count; i++)
            {
                var next = sorted[i];
                int xOverlap = Math.Min(current.X + current.Width, next.X + next.Width) - Math.Max(current.X, next.X);
                int minWidth = Math.Min(current.Width, next.Width);
                int yGap = next.Y - (current.Y + current.Height);

                if (xOverlap > minWidth * 0.4 && yGap <= yGapThreshold)
                {
                    int newX = Math.Min(current.X, next.X);
                    int newY = Math.Min(current.Y, next.Y);
                    int newRight = Math.Max(current.X + current.Width, next.X + next.Width);
                    int newBottom = Math.Max(current.Y + current.Height, next.Y + next.Height);
                    current = new Rect(newX, newY, newRight - newX, newBottom - newY);
                }
                else
                {
                    merged.Add(current);
                    current = next;
                }
            }

            merged.Add(current);
            return merged;
        }

        /// <summary>
        /// 更高效的实现方式（适用于单通道二值图像）
        /// </summary>
        public static Mat RemoveBlackBorderFast(Mat binaryMat, byte threshold = 0, byte set = 255)
        {
            if (binaryMat.Empty() || binaryMat.Channels() != 1)
                return binaryMat.Clone();

            Mat result = binaryMat.Clone();
            int height = result.Rows;
            int width = result.Cols;

            bool[,] visited = new bool[height, width];
            var queue = new Queue<Point>();

            // 将所有边框上的黑色像素加入队列
            for (int x = 0; x < width; x++)
            {
                if (result.At<byte>(0, x) <= threshold && !visited[0, x])
                {
                    queue.Enqueue(new Point(x, 0));
                    visited[0, x] = true;
                }

                if (result.At<byte>(height - 1, x) <= threshold && !visited[height - 1, x])
                {
                    queue.Enqueue(new Point(x, height - 1));
                    visited[height - 1, x] = true;
                }
            }

            for (int y = 0; y < height; y++)
            {
                if (result.At<byte>(y, 0) <= threshold && !visited[y, 0])
                {
                    queue.Enqueue(new Point(0, y));
                    visited[y, 0] = true;
                }

                if (result.At<byte>(y, width - 1) <= threshold && !visited[y, width - 1])
                {
                    queue.Enqueue(new Point(width - 1, y));
                    visited[y, width - 1] = true;
                }
            }

            // 8方向邻域（包括对角线）
            Point[] directions = new Point[]
            {
                new Point(-1, -1), new Point(0, -1), new Point(1, -1),
                new Point(-1, 0), new Point(1, 0),
                new Point(-1, 1), new Point(0, 1), new Point(1, 1)
            };

            int fillCount = 0;
            while (queue.Count > 0)
            {
                Point current = queue.Dequeue();
                result.Set<byte>(current.Y, current.X, set);
                fillCount++;

                foreach (var dir in directions)
                {
                    int nx = current.X + dir.X;
                    int ny = current.Y + dir.Y;

                    if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                    {
                        if (result.At<byte>(ny, nx) <= threshold && !visited[ny, nx])
                        {
                            visited[ny, nx] = true;
                            queue.Enqueue(new Point(nx, ny));
                        }
                    }
                }
            }

            return result;
        }

        public static List<Rect> DetectBarcodesInRoi(Mat roi, string label, Rect roiOffset, bool debug)
        {
            var result = new List<Rect>();

            using (Mat gray = new Mat())
            {
                Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);
                Cv2.Threshold(gray, gray, 128, 255, ThresholdTypes.Binary); // 新增：过滤浅灰/白
                Cv2.MedianBlur(gray, gray, 5);
                Cv2.GaussianBlur(gray, gray, new Size(7, 7), 0);

                if (debug) Cv2.ImShow($"{label} - 1 Gray", gray);

                var noBorder = RemoveBlackBorderFast(gray, 224);
                if (debug)
                    Cv2.ImShow($"{label} - 3.1 RmBorder", noBorder);

                using (Mat gradX = new Mat())
                {
                    Cv2.Sobel(noBorder, gradX, MatType.CV_32F, 1, 0, ksize: 3);
                    Cv2.ConvertScaleAbs(gradX, gradX);

                    if (debug) Cv2.ImShow($"{label} - 2 Sobel X", gradX);

                    using (Mat binary = new Mat())
                    {
                        Cv2.Threshold(gradX, binary, 64, 255, ThresholdTypes.Binary);
                        if (debug)
                            Cv2.ImShow($"{label} - 3 Binary", binary);

                        using (Mat closed = new Mat())
                        using (Mat kernelClose = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(35, 1)))
                        {
                            Cv2.MorphologyEx(binary, closed, MorphTypes.Close, kernelClose);

                            if (debug) Cv2.ImShow($"{label} - 4 Close", closed);

                            using (Mat dilated = new Mat())
                            using (Mat eroded = new Mat())
                            using (Mat kernelSmall = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3)))
                            {
                                Cv2.Dilate(closed, dilated, kernelSmall, null, 1);
                                Cv2.Erode(dilated, eroded, kernelSmall, null, 1);
                                if (debug)
                                    Cv2.ImShow($"{label} - 5 OpenClose", eroded);

                                Cv2.FindContours(eroded, out Point[][] contours, out HierarchyIndex[] hierarchy,
                                    RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                                Console.WriteLine($"    {label} contours: {contours.Length}");

                                using (Mat debugRoi = roi.Clone())
                                {
                                    var rawRects = new List<Rect>();
                                    foreach (var c in contours)
                                    {
                                        Rect r = Cv2.BoundingRect(c);
                                        double ratio = r.Height / (double)r.Width;
                                        double area = r.Width * r.Height;

                                        Cv2.Rectangle(debugRoi, r, new Scalar(0, 255, 0), 1);

                                        if (area > 100)
                                        {
                                            Console.WriteLine(
                                                $"      rect [{r.X},{r.Y}] {r.Width}x{r.Height} ratio={ratio:F2} area={area}");
                                        }

                                        if (ratio < 0.5 && r.Height > 25 && area > 300)
                                        {
                                            rawRects.Add(r);
                                        }
                                    }

                                    //if (debug)
                                    //    Cv2.ImShow($"{label} - 6 All Rects", debugRoi);
                                    Console.WriteLine($"    {label} rawRects after filter: {rawRects.Count}");
                                    var merged = MergeVerticallyAlignedRects(rawRects, yGapThreshold: 5);
                                    Console.WriteLine($"    {label} merged count: {merged.Count}");
                                    result.AddRange(merged);
                                }
                            }
                        }
                    }
                }
            }

            return result;
        }
    }
}