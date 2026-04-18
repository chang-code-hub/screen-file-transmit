using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace screen_file_transmit
{
    internal static class ScreenCaptureHelper
    {
        public static System.Windows.Media.Matrix GetDpiScale(System.Windows.Media.Visual visual)
        {
            var source = PresentationSource.FromVisual(visual);
            if (source?.CompositionTarget != null)
                return source.CompositionTarget.TransformToDevice;
            return System.Windows.Media.Matrix.Identity;
        }

        public static System.Windows.Point LogicalToPhysical(System.Windows.Media.Visual visual, System.Windows.Point logicalPoint)
        {
            var scale = GetDpiScale(visual);
            return new System.Windows.Point(logicalPoint.X * scale.M11, logicalPoint.Y * scale.M22);
        }

        public static System.Windows.Point PhysicalToLogical(System.Windows.Media.Visual visual, System.Windows.Point physicalPoint)
        {
            var scale = GetDpiScale(visual);
            return new System.Windows.Point(physicalPoint.X / scale.M11, physicalPoint.Y / scale.M22);
        }
        public static Bitmap CaptureWindow(IntPtr hwnd)
        {
            NativeMethods.GetWindowRect(hwnd, out var rc);
            int width = rc.Right - rc.Left;
            int height = rc.Bottom - rc.Top;

            if (width <= 0 || height <= 0)
                return null;

            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var gfx = Graphics.FromImage(bmp))
            {
                IntPtr hdcDest = gfx.GetHdc();
                IntPtr hdcSrc = NativeMethods.GetWindowDC(hwnd);

                bool ok = NativeMethods.PrintWindow(hwnd, hdcDest, NativeMethods.PW_RENDERFULLCONTENT);
                if (!ok)
                {
                    NativeMethods.BitBlt(hdcDest, 0, 0, width, height, hdcSrc, 0, 0, NativeMethods.SRCCOPY);
                }

                NativeMethods.ReleaseDC(hwnd, hdcSrc);
                gfx.ReleaseHdc(hdcDest);
            }
            return bmp;
        }

        public static Bitmap CaptureRegion(Rectangle rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                return null;

            var bmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
            using (var gfx = Graphics.FromImage(bmp))
            {
                gfx.CopyFromScreen(rect.Location, System.Drawing.Point.Empty, rect.Size);
            }
            return bmp;
        }

        public static BitmapSource ToBitmapSource(Bitmap bitmap)
        {
            if (bitmap == null)
                return null;

            var hBitmap = bitmap.GetHbitmap();
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                NativeMethods.DeleteObject(hBitmap);
            }
        }

        public static void SavePng(BitmapSource source, string filePath)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                encoder.Save(fs);
            }
        }

        public static string GetUniqueFilePath(string basePath)
        {
            if (!File.Exists(basePath))
                return basePath;

            string directory = Path.GetDirectoryName(basePath);
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(basePath);
            string extension = Path.GetExtension(basePath);
            int index = 1;
            string newPath;
            do
            {
                newPath = Path.Combine(directory, $"{fileNameWithoutExt}({index}){extension}");
                index++;
            } while (File.Exists(newPath));
            return newPath;
        }
    }
}
