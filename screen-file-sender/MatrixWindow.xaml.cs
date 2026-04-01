using System; 
using System.IO;
using System.Windows;  
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Image = System.Windows.Controls.Image;
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
        private readonly string fileName;
        private readonly int shrinkWidth;
        private readonly int shrinkHeight;
        private int currentPage = 1;
        private int totalPage = 1;
        private string sessionGuid;
        private long fileStreamPos;
        private int physicalWidth = 1;
        private int physicalHeight = 1;

        public MatrixWindow()
        {
            InitializeComponent();
        }

        public MatrixWindow(FileStream fileStream, int colorDepth, bool colorful, int scale, string fileName = null, int shrinkWidth = 0, int shrinkHeight = 0)
        {
            this.fileStream = fileStream;
            this.colorDepth = colorDepth;
            this.colorful = colorful;
            this.scale = scale;
            this.fileName = fileName;
            this.shrinkWidth = shrinkWidth;
            this.shrinkHeight = shrinkHeight;
            InitializeComponent();
            this.Loaded += MatrixWindow_Loaded;
            this.Closed += MatrixWindow_Closed;
            this.SizeChanged += MatrixWindow_SizeChanged;
        }


        private void MatrixWindow_Closed(object sender, EventArgs e)
        {
            fileStream.Close();
            fileStream.Dispose();
        }

        private void MatrixWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 获取 DPI
            var presentationSource = PresentationSource.FromVisual(this);
            double dpiX = 96, dpiY = 96;

            if (presentationSource?.CompositionTarget != null)
            {
                dpiX = presentationSource.CompositionTarget.TransformToDevice.M11 * 96;
                dpiY = presentationSource.CompositionTarget.TransformToDevice.M22 * 96;
            }

            // 获取 DIP 尺寸
            double dipWidth = DisplayGrid.ActualWidth;
            double dipHeight = DisplayGrid.ActualHeight;

            // 转换为物理像素
            physicalWidth = (int)(dipWidth * dpiX / 96);
            physicalHeight = (int)(dipHeight * dpiY / 96);

            //var screenWidth = (int)DisplayGrid.ActualWidth;
            //var screenHeight = (int)DisplayGrid.ActualHeight;
            var matrix = DataMatrixEncoder.CalculateScreenDataMatrix(
                physicalWidth, physicalHeight, scale);

            // 计算生成多少页
            long totalBytes = fileStream.Length;
            long bytesPerPage = matrix.PageByteCount * colorDepth *
                                (colorful ? 3 : 1);
              this.totalPage = (int)Math.Ceiling((double)totalBytes / bytesPerPage);

            ShowDataMatrix();
        }

        //public Size DisplaySize => DisplayGrid.RenderSize;

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
            if (currentPage == totalPage)
                return;
            
            currentPage++;
            // 没有数据可显示
               
            ShowDataMatrix();
        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
        }
        private void MatrixWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        { 
            //fileStream.Seek(fileStreamPos, SeekOrigin.Begin);
            //ShowDataMatrix();
        }


        public void ShowDataMatrix()
        {
            DisplayGrid.Content = null;
             
            fileStreamPos = fileStream.Position;
            // 使用 MainWindowViewModel 的方法生成预览图片
            var bitmap = MainWindowViewModel.GeneratePreviewBitmap(
                fileStream, physicalWidth, physicalHeight, colorDepth, colorful, scale,
                fileName,   currentPage, totalPage, ref sessionGuid, shrinkWidth, shrinkHeight);


            this.Title = $"{fileName ?? "MatrixWindow"} - 第 {currentPage - 1} 页";

            BitmapSource bitmapSource = DataMatrixEncoder.ConvertBitmapToBitmapSource(bitmap);
            DisplayGrid.Content = (new Image()
            {
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true,
                Width = bitmap.Width,
                Stretch = System.Windows.Media.Stretch.None,
                Height = bitmap.Height,
                Source = bitmapSource
            });

            // 使用 MainWindowViewModel 的方法检查是否还有更多数据
            NextPage.IsEnabled = MainWindowViewModel.HasMoreData(fileStream);
        }
    }
}