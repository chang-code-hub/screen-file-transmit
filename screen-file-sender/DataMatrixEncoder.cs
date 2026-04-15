using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media.Imaging;
using ZXing;
using Brushes = System.Drawing.Brushes;
using Color = System.Drawing.Color;
using Pen = System.Drawing.Pen;
using Point = System.Drawing.Point;

namespace screen_file_transmit
{
    public class DataMatrixEncoder
    {
        private static string rcString = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        private static readonly int META_BARCODE_HEIGHT = 25;
        private static readonly int MARGIN = 5;
        private static readonly int FONT_SIZE = 12;

        private static readonly int META_INFO_WIDTH =
            MARGIN + META_BARCODE_HEIGHT + MARGIN + META_BARCODE_HEIGHT + MARGIN + MARGIN +
            META_BARCODE_HEIGHT + MARGIN + META_BARCODE_HEIGHT + MARGIN;

        private static readonly int META_INFO_WIDTH_LEFT = MARGIN +
                                                           META_BARCODE_HEIGHT + MARGIN + META_BARCODE_HEIGHT + MARGIN;

        private static readonly Dictionary<string, DataMatrixVersion> dataMatrixVersions =
            new Dictionary<string, DataMatrixVersion>
            {
                { "10x10", new DataMatrixVersion { Size = 10, Capacity = 3 } },
                { "12x12", new DataMatrixVersion { Size = 12, Capacity = 5 } },
                { "14x14", new DataMatrixVersion { Size = 14, Capacity = 8 } },
                { "16x16", new DataMatrixVersion { Size = 16, Capacity = 12 } },
                { "18x18", new DataMatrixVersion { Size = 18, Capacity = 18 } },
                { "20x20", new DataMatrixVersion { Size = 20, Capacity = 22 } },
                { "22x22", new DataMatrixVersion { Size = 22, Capacity = 30 } },
                { "24x24", new DataMatrixVersion { Size = 24, Capacity = 36 } },
                { "26x26", new DataMatrixVersion { Size = 26, Capacity = 44 } },
                { "32x32", new DataMatrixVersion { Size = 32, Capacity = 62 } },
                { "36x36", new DataMatrixVersion { Size = 36, Capacity = 86 } },
                { "40x40", new DataMatrixVersion { Size = 40, Capacity = 114 } },
                { "44x44", new DataMatrixVersion { Size = 44, Capacity = 144 } },
                { "48x48", new DataMatrixVersion { Size = 48, Capacity = 174 } },
                { "52x52", new DataMatrixVersion { Size = 52, Capacity = 204 } },
                { "64x64", new DataMatrixVersion { Size = 64, Capacity = 280 } },
                { "72x72", new DataMatrixVersion { Size = 72, Capacity = 368 } },
                { "80x80", new DataMatrixVersion { Size = 80, Capacity = 456 } },
                { "88x88", new DataMatrixVersion { Size = 88, Capacity = 560 } },
                { "96x96", new DataMatrixVersion { Size = 96, Capacity = 644 } },
                { "104x104", new DataMatrixVersion { Size = 104, Capacity = 793 } },
                { "120x120", new DataMatrixVersion { Size = 120, Capacity = 1050 } },
                { "132x132", new DataMatrixVersion { Size = 132, Capacity = 1304 } },
                { "144x144", new DataMatrixVersion { Size = 144, Capacity = 1558 } }
            };

        // Base128 encoding using ASCII characters (0-127)
        private static char[] Base128Chars = new char[128];

        static DataMatrixEncoder()
        {
            for (int i = 0; i < 128; i++)
            {
                Base128Chars[i] = (char)i; // Map each byte value to its corresponding ASCII character
            }
        }

        private static int CalcBase256ByteLength(int capacity)
        {
            // Base256 模式下每个字节占 1 个码字，直接返回容量
            return capacity;
        }

        public static DataMatrixResult CalculateScreenDataMatrix(int screenWidth, int screenHeight, int codeScale)
        {
            int scale = codeScale;

            string bestVersion = null;
            int maxRows = 0;
            int maxCols = 0;
            int maxByteCount = 0;
            int codeByteCount = 0;
            int codeCapacity = 0;
            int codeSize = 0;

            foreach (var kvp in dataMatrixVersions)
            {
                var version = kvp.Key;
                var versionData = kvp.Value;

                int cellStep = (versionData.Size + 7) * scale;

                // 扣除左右信息区后计算行列
                int cols = (screenWidth - META_INFO_WIDTH) / cellStep;
                int rows = screenHeight / cellStep;

                if (cols <= 0 || rows <= 0) continue;

                int byteCount = CalcBase256ByteLength(versionData.Capacity - 5);
                int totalCapacity = rows * cols * byteCount;

                if (totalCapacity > maxByteCount)
                {
                    maxByteCount = totalCapacity;
                    maxRows = rows;
                    maxCols = cols;
                    codeSize = versionData.Size;
                    bestVersion = version;
                    codeByteCount = byteCount;
                    codeCapacity = versionData.Capacity;
                }
            }

            return new DataMatrixResult
            {
                BestVersion = bestVersion,
                MaxRows = maxRows,
                MaxCols = maxCols,
                CodeSize = codeSize,
                CodeByteCount = codeByteCount,
                CodeCapacity = codeCapacity,
                PageByteCount = codeByteCount * maxRows * maxCols,
            };
        }

        public static PageInfo CalculatePageInfo(DataMatrixResult matrix, int scale)
        {
            int cellStep = (matrix.CodeSize + 7) * scale;

            return new PageInfo
            {
                CellStep = cellStep,
                BitmapWidth = matrix.MaxCols * cellStep + META_INFO_WIDTH,
                BitmapHeight = matrix.MaxRows * cellStep
            };
        }

        public static Bitmap DrawDataMatrix(FileStream fileStream, int row, int column, int scale, byte[] chuck,
            DataMatrixResult matrix,
            int depth, bool colorful)
        {
            List<Bitmap> bitmaps = new List<Bitmap>();
            for (int i = 0; i < depth * (colorful ? 3 : 1); i++)
            {
                byte[] buffer;

                var readLength = fileStream.Read(chuck, 0, chuck.Length);
                if (readLength <= 0)
                    break;
                buffer = new byte[readLength];
                Array.Copy(chuck, buffer, buffer.Length);
                using (var ms = new MemoryStream())
                {
                    byte[] bytes = Encoding.ASCII.GetBytes($"{rcString[row]}{rcString[column]}");
                    ms.Write(bytes, 0, bytes.Length);
                    ms.Write(buffer, 0, buffer.Length);
                    var chuckBitmap = GenerateDataMatrix(ms.ToArray(), matrix.CodeSize, scale);
                    //var base64 = $"{rcString[row]}{rcString[column]}{Convert.ToBase64String(buffer)}";
                    //var chuckBitmap = GenerateDataMatrix(base64, matrix.CodeSize, scale);
                    bitmaps.Add((chuckBitmap));
                }
            }

            if (bitmaps.Count == 0) return null;
            var mixedBitmap = new Bitmap(matrix.CodeSize * scale, matrix.CodeSize * scale);
            if (colorful)
            {
                var redImages = bitmaps.Skip(0).Take(depth).ToList();
                var greenImages = bitmaps.Skip(depth).Take(depth).ToList();
                var blueImages = bitmaps.Skip(depth * 2).Take(depth).ToList();
                for (var x = 0; x < mixedBitmap.Width; x++)
                {
                    for (var y = 0; y < mixedBitmap.Height; y++)
                    {
                        var red = MixColor(redImages, x, y, depth);
                        var green = MixColor(greenImages, x, y, depth);
                        var blue = MixColor(blueImages, x, y, depth);
                        mixedBitmap.SetPixel(x, y, Color.FromArgb(red, green, blue));
                    }
                }
            }
            else
            {
                for (var x = 0; x < mixedBitmap.Width; x++)
                {
                    for (var y = 0; y < mixedBitmap.Height; y++)
                    {
                        var gray = MixColor(bitmaps, x, y, depth);
                        mixedBitmap.SetPixel(x, y, Color.FromArgb(gray, gray, gray));
                    }
                }
            }

            return mixedBitmap;
        }

        private static int MixColor(List<Bitmap> redImages, int x, int y, int depth)
        {
            if (redImages.Count == 0) return 0xff;
            var color = 0xff; // & (0xff<<(9- depth) - 1);
            for (int ri = 0; ri < redImages.Count; ri++)
            {
                var img = redImages[ri];
                var pixel = img.GetPixel(x, y);
                if (pixel.R == 0)
                {
                    color &= (~(1 << (7 - ri)));
                }
            }

            if (color != 0xff)
            {
                return color & (0xff << (8 - depth)) & 0xff;
            }

            return color;
        }

        public static Bitmap GenerateDataMatrix(string content, int size, int scale)
        {
            // Create a BarcodeWriter instance for DataMatrixEncoder encoding
            var writer = new BarcodeWriter
            {
                Format = BarcodeFormat.DATA_MATRIX,
                Options = new ZXing.Common.EncodingOptions
                {
                    Width = size * scale,
                    Height = size * scale,
                    PureBarcode = true,
                    Margin = 0 // No margin for tight fit
                }
            };

            // Encode the content into a DataMatrixEncoder Bitmap
            return writer.Write(content);
        }

        public static Bitmap GenerateDataMatrix(byte[] content, int size, int scale)
        {
            var writer = new BarcodeWriter
            {
                Format = BarcodeFormat.DATA_MATRIX,
                Options = new ZXing.Common.EncodingOptions
                {
                    Width = size * scale,
                    Height = size * scale,
                    PureBarcode = true,
                    Margin = 0
                }
            };
            var iso88591 = Encoding.GetEncoding("ISO-8859-1");
            string text = iso88591.GetString(content);
            return writer.Write(text);
        }

        public static Bitmap GenerateDataRectangleMatrix(string content, int height, int width, int scale,
            bool border = false)
        {
            // 计算目标尺寸（像素）
            int targetWidth = width * scale;
            int targetHeight = height * scale;

            // Create a BarcodeWriter instance for DataMatrixEncoder encoding
            var writer = new BarcodeWriter
            {
                Format = BarcodeFormat.DATA_MATRIX,
                Options = new ZXing.Common.EncodingOptions
                {
                    Width = targetWidth,
                    Height = targetHeight,
                    PureBarcode = true,
                    Margin = 0 // No margin for tight fit
                }
            };

            // Encode the content into a DataMatrixEncoder Bitmap
            var map = writer.Write(content);

            // 计算总尺寸：DataMatrix + 1倍留白 + 1倍方框 = 每边加 2倍scale
            int unitSize = Math.Max(map.Width, map.Height);
            int totalSize = unitSize + 4 * scale; // 每边 2*scale（1倍留白 + 1倍方框）

            var img = new Bitmap(totalSize, totalSize);

            using (Graphics g = Graphics.FromImage(img))
            {
                g.Clear(Color.White);

                if (border)
                {
                    // 绘制 1倍 scale 线宽的方框，距离边缘 1倍 scale（留白）
                    g.DrawRectangle(new Pen(Brushes.Black, scale),
                        new Rectangle(scale, scale, totalSize - 2 * scale, totalSize - 2 * scale));
                }

                // 将 DataMatrix 绘制在中心（偏移 2*scale：1倍留白 + 1倍方框线宽）
                int offsetX = (totalSize - map.Width) / 2;
                int offsetY = (totalSize - map.Height) / 2;
                g.DrawImage(map, new Point(offsetX, offsetY));
            }

            map.Dispose();
            return img;
        }

        public static Bitmap GenerateCode128(string content, int height, int scale)
        {
            var writer = new BarcodeWriter
            {
                Format = BarcodeFormat.CODE_128,
                Options = new ZXing.Common.EncodingOptions
                {
                    Height = height,
                    Width = 0,
                    PureBarcode = true,
                    Margin = 0
                }
            };

            // 先生成原始条码（scale=1 时的基准宽度）
            var original = writer.Write(content);

            // 按 scale 缩放，使最小条宽为 scale 像素
            int targetWidth = original.Width * scale;
            int targetHeight = height;
            var scaled = new Bitmap(targetWidth, targetHeight);

            using (var g = Graphics.FromImage(scaled))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(original, 0, 0, targetWidth, targetHeight);
            }

            original.Dispose();
            return scaled;
        }

        public static String GenerateFileId()
        {
            return "#" + Guid.NewGuid().ToString().Split('-').Last();
            //long date =  DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            //Byte[] bytes = BitConverter.GetBytes(date);
            //return Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// 生成完整的 DataMatrix 图片（包含网格和元数据区域）
        /// </summary>
        public static Bitmap GenerateDataMatrixBitmap(FileStream fileStream, DataMatrixResult matrix, PageInfo pageInfo,
            int colorDepth, bool colorful, int scale, string fileName = null, bool includeFileName = false,
            int currentPage = 1, int totalPages = 1, string sessionGuid = null, bool hasPassword = false)
        {
            var bitmap = new Bitmap(pageInfo.BitmapWidth, pageInfo.BitmapHeight);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(System.Drawing.Color.White);
            }

            var offset = fileStream.Position;
            var chuck = new byte[matrix.CodeByteCount];
            bool end = false;
            int count = 0;

            for (int row = 0; !end && row < matrix.MaxRows; row++)
            {
                var top = pageInfo.CellStep * row;

                for (int column = 0; !end && column < matrix.MaxCols; column++)
                {
                    var left = pageInfo.CellStep * column + META_INFO_WIDTH_LEFT;
                    var bitmapPart = DrawDataMatrix(fileStream, row, column, scale, chuck, matrix,
                        colorDepth, colorful);

                    if (bitmapPart != null)
                    {
                        count++;
                        using (Graphics g = Graphics.FromImage(bitmap))
                        {
                            g.CompositingMode = CompositingMode.SourceOver;
                            g.DrawRectangle(new Pen(Brushes.Black, scale),
                                new Rectangle(left + scale, top + scale, (matrix.CodeSize + 4) * scale,
                                    (matrix.CodeSize + 4) * scale));
                            g.DrawImage(bitmapPart,
                                new PointF((float)(left + 3 * scale), (float)(top + 3 * scale)));
                        }

                        bitmapPart.Dispose();
                    }
                    else
                    {
                        end = true;
                    }
                }
            }

            if (count == 0)
                return null;

            // 在左右两侧绘制元数据信息
            DrawInfoArea(bitmap, matrix, pageInfo, colorful, colorDepth, offset, fileStream.Position - offset,
                fileStream.Length,
                count, fileName, currentPage, totalPages, scale, sessionGuid, hasPassword);

            return bitmap;
        }

        /// <summary>
        /// 绘制左右侧边信息区域（元数据条码在左旋转90度，文件名在左旋转90度，GUID条码在右旋转90度）
        /// </summary>
        public static void DrawInfoArea(Bitmap bitmap, DataMatrixResult matrix, PageInfo pageInfo, bool colorful,
            int colorDepth,
            long offset, long length, long totalLength, int count, string fileName, int currentPage, int totalPages,
            int scale, string sessionGuid, bool hasPassword = false)
        {
            // 条码原始高度
            int maxBarcodeHeight = bitmap.Height - MARGIN * 2; // 条码最大高度（旋转后）

            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.CompositingMode = CompositingMode.SourceOver;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                // ===== 左侧：文件名（旋转90度）=====
                var displayName = string.IsNullOrEmpty(fileName)
                    ? "未知"
                    : $"{fileName} ({currentPage}/{totalPages})";
                using (var nameFont = new Font("Microsoft YaHei", FONT_SIZE, FontStyle.Regular))
                {
                    var nameSize = g.MeasureString(displayName, nameFont);
                    int availableHeight = bitmap.Height - MARGIN * 2;

                    // 截断文件名以适应可用高度
                    if (nameSize.Width > availableHeight && displayName.Length > 5)
                    {
                        while (displayName.Length > 5)
                        {
                            var testName = displayName.Substring(0, displayName.Length - 1) + "...";
                            if (g.MeasureString(testName, nameFont).Width <= availableHeight)
                            {
                                displayName = testName;
                                break;
                            }

                            displayName = displayName.Substring(0, displayName.Length - 1);
                        }

                        if (!displayName.EndsWith("..."))
                            displayName += "...";
                    }

                    // 创建文字位图并旋转
                    nameSize = g.MeasureString(displayName, nameFont);
                    int textBitmapWidth = (int)Math.Ceiling(nameSize.Width);
                    int textBitmapHeight = (int)Math.Ceiling(nameSize.Height);

                    using (var textBitmap = new Bitmap(textBitmapWidth, textBitmapHeight))
                    using (var textG = Graphics.FromImage(textBitmap))
                    {
                        textG.Clear(System.Drawing.Color.White);
                        textG.DrawString(displayName, nameFont, System.Drawing.Brushes.Black, 0, 0);

                        // 旋转文字90度
                        var rotatedTextBitmap = RotateBitmap90Clockwise(textBitmap);

                        int textX = MARGIN; // 放在条码右侧
                        int textY = MARGIN;
                        g.DrawImage(rotatedTextBitmap, new Point(textX, textY));
                        rotatedTextBitmap.Dispose();
                    }
                }

                // ===== 左侧：元数据条码（旋转90度）=====
                List<byte> meta = new List<byte>();
                meta.Add((byte)(matrix.MaxRows << 4 | matrix.MaxCols));
                meta.Add((byte)((colorful ? 0x80 : 0x00) | (hasPassword ? 0x40 : 0x00) | colorDepth));
                meta.Add((byte)(currentPage));
                meta.Add((byte)(totalPages));
                var info = "$" + Convert.ToBase64String(meta.ToArray());

                var infoBitmap = GenerateCode128(info, META_BARCODE_HEIGHT - MARGIN, scale); // 高度20，然后旋转

                // 缩放条码以适应边界（旋转后的高度 = 原始宽度）
                infoBitmap = ScaleBarcodeToFit(infoBitmap, maxBarcodeHeight);

                // 旋转条码90度（顺时针）
                var rotatedInfoBitmap = RotateBitmap90Clockwise(infoBitmap);
                infoBitmap.Dispose();

                int infoX = MARGIN + META_BARCODE_HEIGHT + MARGIN + MARGIN;
                int infoY = MARGIN;
                g.DrawImage(rotatedInfoBitmap, new Point(infoX, infoY));
                rotatedInfoBitmap.Dispose();

                // ===== 右侧：文件名条码（旋转90度，中文转拼音首字母，保留扩展名）=====
                if (!string.IsNullOrEmpty(fileName))
                {
                    var displayFileName = Path.GetFileName(fileName);
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(displayFileName);
                    var ext = Path.GetExtension(displayFileName);
                    var pinyinInitials = ChineseToPinyinInitials(nameWithoutExt);

                    // 如果转换后为空，使用原始文件名中的字母数字
                    if (string.IsNullOrEmpty(pinyinInitials))
                    {
                        foreach (char c in nameWithoutExt)
                        {
                            if (char.IsLetterOrDigit(c))
                                pinyinInitials += char.ToUpper(c);
                        }
                    }

                    // 保留扩展名
                    if (!string.IsNullOrEmpty(ext))
                    {
                        pinyinInitials += ext;
                    }

                    // 限制长度，避免条码过长
                    if (pinyinInitials.Length > 25)
                        pinyinInitials = pinyinInitials.Substring(0, 25);

                    if (!string.IsNullOrEmpty(pinyinInitials))
                    {
                        var fileNameBarcode = GenerateCode128(pinyinInitials, META_BARCODE_HEIGHT, scale);
                        int remainingHeight = bitmap.Height - MARGIN;

                        fileNameBarcode = ScaleBarcodeToFit(fileNameBarcode, remainingHeight);
                        var rotatedFileNameBarcode = RotateBitmap90Clockwise(fileNameBarcode, 90);
                        fileNameBarcode.Dispose();

                        int fileNameX = bitmap.Width - MARGIN - META_BARCODE_HEIGHT - MARGIN - META_BARCODE_HEIGHT;
                        int fileNameY = MARGIN;
                        g.DrawImage(rotatedFileNameBarcode, new Point(fileNameX, fileNameY));
                        rotatedFileNameBarcode.Dispose();

                        fileNameBarcode.Dispose();
                    }
                }

                // ===== 右侧：GUID条码（旋转90度）=====
                if (!string.IsNullOrEmpty(sessionGuid))
                {
                    var guidBitmap = GenerateCode128(sessionGuid, META_BARCODE_HEIGHT, scale);

                    // 缩放条码以适应边界（旋转后的高度 = 原始宽度）
                    guidBitmap = ScaleBarcodeToFit(guidBitmap, maxBarcodeHeight);

                    var rotatedGuidBitmap = RotateBitmap90Clockwise(guidBitmap, 90);
                    guidBitmap.Dispose();

                    int guidX = bitmap.Width - MARGIN - META_BARCODE_HEIGHT;
                    int guidY = MARGIN;
                    g.DrawImage(rotatedGuidBitmap, new System.Drawing.Point(guidX, guidY));
                    rotatedGuidBitmap.Dispose();
                }
            }
        }

        /// <summary>
        /// 缩放权图以适应最大高度（保持宽高比）
        /// </summary>
        private static Bitmap ScaleBarcodeToFit(Bitmap barcode, int maxHeight)
        {
            if (barcode.Width <= maxHeight)
                return barcode;

            // 需要缩放，旋转后的高度是原始宽度
            double scale = (double)maxHeight / barcode.Width;
            int newWidth = maxHeight;
            int newHeight = (int)(barcode.Height * scale);

            var scaled = new Bitmap(newWidth, newHeight);
            using (var g = Graphics.FromImage(scaled))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(barcode, 0, 0, newWidth, newHeight);
            }

            barcode.Dispose();
            return scaled;
        }

        /// <summary>
        /// 将中文字符串转换为拼音首字母大写
        /// </summary>
        public static string ChineseToPinyinInitials(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var result = new StringBuilder();
            foreach (char c in input)
            {
                if (c >= 0x4E00 && c <= 0x9FA5) // 中文字符范围
                {
                    result.Append(GetPinyinInitial(c));
                }
                else if (char.IsLetter(c))
                {
                    result.Append(char.ToUpper(c));
                }
                else if (char.IsDigit(c))
                {
                    result.Append(c);
                }
                else
                {
                    result.Append("_");
                }
                // 其他字符（如符号、空格）跳过
            }

            return result.ToString();
        }

        /// <summary>
        /// 获取单个中文汉字的拼音首字母
        /// </summary>
        private static char GetPinyinInitial(char chineseChar)
        {
            // 使用GB2312编码获取区码和位码
            byte[] bytes = Encoding.Default.GetBytes(chineseChar.ToString());
            if (bytes.Length < 2)
                return chineseChar;

            int area = bytes[0] - 160;
            int pos = bytes[1] - 160;
            int code = area * 100 + pos;

            // 根据GB2312编码的拼音排序表判断首字母
            if (code >= 1601 && code < 1637) return 'A';
            if (code >= 1637 && code < 1833) return 'B';
            if (code >= 1833 && code < 2078) return 'C';
            if (code >= 2078 && code < 2274) return 'D';
            if (code >= 2274 && code < 2302) return 'E';
            if (code >= 2302 && code < 2433) return 'F';
            if (code >= 2433 && code < 2594) return 'G';
            if (code >= 2594 && code < 2787) return 'H';
            if (code >= 2787 && code < 3106) return 'J';
            if (code >= 3106 && code < 3212) return 'K';
            if (code >= 3212 && code < 3472) return 'L';
            if (code >= 3472 && code < 3635) return 'M';
            if (code >= 3635 && code < 3722) return 'N';
            if (code >= 3722 && code < 3730) return 'O';
            if (code >= 3730 && code < 3858) return 'P';
            if (code >= 3858 && code < 4027) return 'Q';
            if (code >= 4027 && code < 4086) return 'R';
            if (code >= 4086 && code < 4390) return 'S';
            if (code >= 4390 && code < 4558) return 'T';
            if (code >= 4558 && code < 4684) return 'W';
            if (code >= 4684 && code < 4925) return 'X';
            if (code >= 4925 && code < 5249) return 'Y';
            if (code >= 5249 && code < 5590) return 'Z';

            return '?'; // 未知字符
        }

        /// <summary>
        /// 将位图顺时针旋转90度
        /// </summary>
        private static Bitmap RotateBitmap90Clockwise(Bitmap original, float angle = 90)
        {
            int newWidth = original.Height;
            int newHeight = original.Width;
            var rotated = new Bitmap(newWidth, newHeight);

            using (Graphics g = Graphics.FromImage(rotated))
            {
                g.TranslateTransform(newWidth, 0);
                g.RotateTransform(angle);
                g.DrawImage(original, 0, 0);
            }

            return rotated;
        }

        public static BitmapSource ConvertBitmapToBitmapSource(Bitmap bitmap)
        {
            using (MemoryStream memoryStream = new MemoryStream())
            {
                // Save the Bitmap to a memory stream in BMP format
                bitmap.Save(memoryStream, ImageFormat.Png);
                memoryStream.Position = 0;

                // Create a BitmapImage from the memory stream
                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memoryStream;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad; // Load the image into memory
                bitmapImage.EndInit();
                bitmapImage.Freeze(); // Freeze the image to make it cross-thread accessible

                return bitmapImage;
            }
        }
    }

    public class DataMatrixResult
    {
        public string BestVersion { get; set; }
        public int MaxRows { get; set; }
        public int MaxCols { get; set; }
        public int CodeSize { get; set; }
        public int CodeByteCount { get; set; }
        public int CodeCapacity { get; set; }
        public int PageByteCount { get; set; }
    }

    public class DataMatrixVersion
    {
        public int Size { get; set; }
        public int Capacity { get; set; }
    }

    public class PageInfo
    {
        public int CellStep { get; set; }
        public int BitmapWidth { get; set; }
        public int BitmapHeight { get; set; }
    }
}