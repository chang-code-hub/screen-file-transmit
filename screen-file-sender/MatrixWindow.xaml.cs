using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Mime;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents; 
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Image = System.Windows.Controls.Image;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Windows.Size;


namespace screen_file_transmit
{
    /// <summary>
    /// Matrixxaml 的交互逻辑
    /// </summary>
    public partial class MatrixWindow : Window
    {
        private readonly FileStream fileStream;
        private readonly int colorDepth;
        private readonly bool colorful;
        private readonly int scale;

        public MatrixWindow()
        {
            InitializeComponent();
        }

        public MatrixWindow(FileStream fileStream, int colorDepth, bool colorful, int scale)
        {
            this.fileStream = fileStream;
            this.colorDepth = colorDepth;
            this.colorful = colorful;
            this.scale = scale;
            InitializeComponent();
            this.Loaded += MatrixWindow_Loaded;
            this.Closed += MatrixWindow_Closed;
        }

        private void MatrixWindow_Closed(object sender, EventArgs e)
        {
            fileStream.Close();
            fileStream.Dispose();
        }

        private void MatrixWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ShowDataMatrix();
        }

        public Size DisplaySize => DisplayGrid.RenderSize;

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void UIElement_OnMouseDown(object sender, MouseButtonEventArgs e)
        { 
            this.DragMove();
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            ShowDataMatrix();
        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {

        }

        public void ShowDataMatrix()
        {
            DisplayGrid.Children.Clear();
            InfoImage.Source = null;
            var screenWidth = (int)DisplayGrid.ActualWidth;
            var screenHeight = (int)DisplayGrid.ActualHeight;
            int infoCodeHeight = 8;
            int infoCodeWidth = 32;
            int infoHeight = 0;// scale * (infoCodeHeight + 3);
            var matrix = DataMatrixEncoder.CalculateScreenDataMatrix(screenWidth, screenHeight - infoHeight, scale);
            //Trace.WriteLine("{ width: canvas.width, height: canvas.height, size: code.codeSize, scale: options.scale })
            var width = ((matrix.MaxCols * (matrix.CodeSize + 6))) * scale;
            var height = ((matrix.MaxRows * (matrix.CodeSize + 6))) * scale + infoHeight;
            Bitmap bitmap = new Bitmap(width, height);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White);
                //g.DrawLine(new Pen(Brushes.Black, scale * 2), new Point(0, height), new Point(width, height));
                //g.DrawLine(new Pen(Brushes.Black, scale * 2), new Point(width, infoHeight), new Point(width, height));
            }
            //
            var offset = fileStream.Position;

            var chuck = new byte[matrix.CodeByteCount];
            bool end = false;
            int count = 0;
            for (int row = 0; !end && row < matrix.MaxRows; row++)
            {
                var top = (((matrix.CodeSize + 6 )) * row) * scale + infoHeight; 

                for (int column = 0; !end && column < matrix.MaxCols; column++)
                {
                    var left = (((matrix.CodeSize + 6)) * column) * scale; 
                    var bitmapPart = DataMatrixEncoder.DrawDataMatrix(fileStream, row, column, scale, chuck, matrix, colorDepth, colorful);

                    if (bitmapPart != null)
                    {
                        count++;
                        using (Graphics g = Graphics.FromImage(bitmap))
                        {
                            g.CompositingMode = CompositingMode.SourceOver;
                            g.DrawRectangle(new Pen(Brushes.Black, scale), new Rectangle(left + scale, top + scale, (matrix.CodeSize + 3) * scale, (matrix.CodeSize + 3) * scale));
                            g.DrawImage(bitmapPart, new PointF( (float)(left + 2.5 * scale), (float)(top + 2.5 * scale)));
                        }
                    }
                }
            }

            BitmapSource bitmapSource = DataMatrixEncoder.ConvertBitmapToBitmapSource(bitmap);
            DisplayGrid.Children.Add(new Image()
            {
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true,
                Width = width,
                Height = height,
                Source = bitmapSource
            });

            var info = $"${matrix.MaxRows},{matrix.MaxCols},{(colorful ? "1" : "0")},{colorDepth},{offset},{fileStream.Position - offset},{fileStream.Length},{count}";
            var infoBitmap = DataMatrixEncoder.GenerateDataRectangleMatrix(info, infoCodeHeight, infoCodeWidth, 1, true);
            var infoBitmapSource = DataMatrixEncoder.ConvertBitmapToBitmapSource(infoBitmap);
            InfoImage.Source = infoBitmapSource;


            if (fileStream.Length > fileStream.Position)
            {
                //fileStream.Seek(-1, SeekOrigin.Current);
                NextPage.IsEnabled = true;
            }
            else
            {
                NextPage.IsEnabled = false;
            }
        }
    }
}
