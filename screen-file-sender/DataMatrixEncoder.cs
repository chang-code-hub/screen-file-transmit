using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.QrCode.Internal;
using Brushes = System.Drawing.Brushes;
using Color = System.Drawing.Color;
using Image = System.Windows.Controls.Image;
using Pen = System.Drawing.Pen;
using Point = System.Drawing.Point;

namespace screen_file_transmit
{
    public class DataMatrixEncoder
    {
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

        private static int CalcBase64ByteLength(int capacity)
        {
            int originalBytes = (capacity * 3) / 4; // Calculate the original byte count
            int byteCount = originalBytes / 1; // Assume each byte[] is 1 byte
            return byteCount;
        }

        public static DataMatrixResult CalculateScreenDataMatrix(int screenWidth, int screenHeight, int codeScale)
        {
            int scale = codeScale;


            int qrHeight = 20;
            int infoAreaHeight = qrHeight + 12;

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
                 

                int cols = screenWidth / cellStep;
                int rows = (screenHeight - infoAreaHeight) / cellStep;

                if (cols <= 0 || rows <= 0) continue;

                int byteCount = CalcBase64ByteLength(versionData.Capacity - 2);
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
                PageByteCount  = codeByteCount * maxRows * maxCols
            };
        }

        public static PageInfo CalculatePageInfo(DataMatrixResult matrix, int scale)
        {
            int qrHeight = 20;
            int infoAreaHeight = qrHeight + 12;
            int cellStep = (matrix.CodeSize + 7) * scale;

            return new PageInfo
            {
                QrHeight = qrHeight,
                InfoAreaHeight = infoAreaHeight,
                CellStep = cellStep,
                BitmapWidth = matrix.MaxCols * cellStep,
                BitmapHeight = matrix.MaxRows * cellStep + infoAreaHeight
            };
        }

        private static string rcString = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

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

                var base64 = $"{rcString[row]}{rcString[column]}{Convert.ToBase64String(buffer)}";
                var chuckBitmap = GenerateDataMatrix(base64, matrix.CodeSize, scale);
                bitmaps.Add((chuckBitmap));
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
            int targetHeight = height ;
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

        /// <summary>
        /// 生成完整的 DataMatrix 图片（包含网格和元数据区域）
        /// </summary>
        public static Bitmap GenerateDataMatrixBitmap(FileStream fileStream, DataMatrixResult matrix, PageInfo pageInfo,
            int colorDepth, bool colorful, int scale, string fileName = null, bool includeFileName = false,
            int currentPage = 1, int totalPages = 1, string sessionGuid = null)
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

            // 绘制二维码网格（从 infoAreaHeight 开始，避开顶部信息区）
            for (int row = 0; !end && row < matrix.MaxRows; row++)
            {
                var top = pageInfo.CellStep * row + pageInfo.InfoAreaHeight;

                for (int column = 0; !end && column < matrix.MaxCols; column++)
                {
                    var left = pageInfo.CellStep * column;
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

            // 在顶部绘制元数据信息
            DrawInfoArea(bitmap, matrix, pageInfo, colorful, colorDepth, offset, fileStream.Position - offset, fileStream.Length,
                count, fileName, currentPage, totalPages, scale, sessionGuid);

            return bitmap;
        }

        /// <summary>
        /// 绘制顶部信息区域（文件名带页码、元数据二维码、GUID条码）
        /// </summary>
        public static void DrawInfoArea(Bitmap bitmap, DataMatrixResult matrix, PageInfo pageInfo, bool colorful, int colorDepth,
            long offset, long length, long totalLength, int count, string fileName, int currentPage, int totalPages,
            int scale, string sessionGuid)
        {
            // 元数据条码使用 Code 128，高度固定 20
            int qrHeight = pageInfo.QrHeight;

            // 紧凑的信息区高度
            int infoAreaHeight = pageInfo.InfoAreaHeight;
            int margin = 8;

            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                // 绘制白色背景
                g.FillRectangle(System.Drawing.Brushes.White,
                    new System.Drawing.Rectangle(0, 0, bitmap.Width, infoAreaHeight));

                // 左上角：元数据条码（Code 128）
                var info =
                    $"{matrix.MaxRows},{matrix.MaxCols},{(colorful ? "1" : "0")},{colorDepth},{offset},{length},{totalLength},{count}";
                var infoBitmap = GenerateCode128(info, qrHeight, scale);

                int qrX = margin;
                int qrY = (infoAreaHeight - infoBitmap.Height) / 2;
                qrY = Math.Max(margin / 2, qrY);

                g.DrawImage(infoBitmap, new System.Drawing.Point(qrX, qrY));
                int qrRightEdge = qrX + infoBitmap.Width;
                infoBitmap.Dispose();

                // 右上角：GUID 的 Code 128 条码
                int guidRightEdge = bitmap.Width - margin;
                if (!string.IsNullOrEmpty(sessionGuid))
                {
                    var guidBitmap = GenerateCode128(sessionGuid, qrHeight, scale);
                    int guidX = bitmap.Width - margin - guidBitmap.Width;
                    int guidY = (infoAreaHeight - guidBitmap.Height) / 2;
                    guidY = Math.Max(margin / 2, guidY);
                    g.DrawImage(guidBitmap, new System.Drawing.Point(guidX, guidY));
                    guidRightEdge = guidX - margin;
                    guidBitmap.Dispose();
                }

                // 中间：文件名 + 页码（自动截断以适应可用空间）
                var displayName = string.IsNullOrEmpty(fileName) ? "Unknown" : $"{fileName} ({currentPage}/{totalPages})";
                using (var nameFont =
                       new Font("Microsoft YaHei", 12, System.Drawing.FontStyle.Regular))
                {
                    var nameSize = g.MeasureString(displayName, nameFont);
                    int availableWidth = guidRightEdge - qrRightEdge - margin * 2;

                    // 截断文件名
                    if (nameSize.Width > availableWidth && displayName.Length > 5)
                    {
                        while (displayName.Length > 5)
                        {
                            var testName = displayName.Substring(0, displayName.Length - 1) + "...";
                            if (g.MeasureString(testName, nameFont).Width <= availableWidth)
                            {
                                displayName = testName;
                                break;
                            }

                            displayName = displayName.Substring(0, displayName.Length - 1);
                        }

                        if (!displayName.EndsWith("..."))
                            displayName += "...";
                    }

                    // 重新测量并居中绘制
                    nameSize = g.MeasureString(displayName, nameFont);
                    int nameX = qrRightEdge + margin + (availableWidth - (int)nameSize.Width) / 2;
                    int nameY = (infoAreaHeight - (int)nameSize.Height) / 2;

                    g.DrawString(displayName, nameFont, System.Drawing.Brushes.Black, nameX, nameY);
                }
            }
        }

        public static BitmapSource ConvertBitmapToBitmapSource(Bitmap bitmap)
        {
            using (MemoryStream memoryStream = new MemoryStream())
            {
                // Save the Bitmap to a memory stream in BMP format
                bitmap.Save(memoryStream, ImageFormat.Bmp);
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
        public int QrHeight { get; set; }
        public int InfoAreaHeight { get; set; }
        public int CellStep { get; set; }
        public int BitmapWidth { get; set; }
        public int BitmapHeight { get; set; }
    }
}