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
        private static readonly Dictionary<string, DataMatrixVersion> dataMatrixVersions = new Dictionary<string, DataMatrixVersion>
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
            int pixelPerPoint = codeScale;
            int spacing = codeScale * 6;

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

                int qrWidth = versionData.Size * pixelPerPoint;
                int qrHeight = versionData.Size * pixelPerPoint;
                int totalWidth = qrWidth + spacing  ;
                int totalHeight = qrHeight + spacing  ;

                int cols = (screenWidth ) / totalWidth;
                int rows = (screenHeight ) / totalHeight;

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
                CodeCapacity = codeCapacity
            };
        }

        private static string rcString = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

        public static Bitmap DrawDataMatrix(FileStream fileStream,int row, int column, int scale, byte[] chuck, DataMatrixResult matrix,
            int depth,  bool colorful)
        {
            List<Bitmap> bitmaps = new List<Bitmap>();
            for (int i = 0; i < depth * (colorful? 3:1); i++)
            {
                var readLength = fileStream.Read(chuck, 0, chuck.Length);
                if (readLength <= 0) 
                    break;
                byte[] buffer = new byte[readLength];
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
                        mixedBitmap.SetPixel(x,y, Color.FromArgb(red, green, blue));
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

        private static int MixColor(List<Bitmap> redImages, int x, int y, int depth )
        {
            if (redImages.Count == 0) return 0xff;
            var color = 0xff;// & (0xff<<(9- depth) - 1);
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
                return color & (0xff << (9 - depth) - 1) & 0xff;
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

        public static Bitmap GenerateDataRectangleMatrix(string content, int height, int width, int scale, bool border = false)
        {
            // Create a BarcodeWriter instance for DataMatrixEncoder encoding
            var writer = new BarcodeWriter
            {
                Format = BarcodeFormat.DATA_MATRIX,
                Options = new ZXing.Common.EncodingOptions
                {
                    Width = scale,
                    Height = scale,
                    PureBarcode = true,
                    Margin = 0 // No margin for tight fit
                    
                }
            };

            // Encode the content into a DataMatrixEncoder Bitmap
            var map = writer.Write(content);
            if (!border)
            {
                return map;
            }
            else
            {
                var img = new Bitmap(map.Width + 6 * scale, map.Height + 6 * scale);

                using (Graphics g = Graphics.FromImage(img))
                {
                    g.Clear(Color.White);
                    g.DrawRectangle(new Pen(Brushes.Black, scale ), new Rectangle(scale, scale, map.Width + 4 * scale, map.Height + 4* scale));
                    g.DrawImage(map, new Point(scale * 3, scale* 3 ));
                }
                return img;
            }
        }

        public static BitmapSource ConvertBitmapToBitmapSource(Bitmap bitmap)
        {
            using (MemoryStream memoryStream = new MemoryStream())
            {
                // Save the Bitmap to a memory stream in PNG format
                bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
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
    }
    public class DataMatrixVersion
    {
        public int Size { get; set; }
        public int Capacity { get; set; }
    }
}
