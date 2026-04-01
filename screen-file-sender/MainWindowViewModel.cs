using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Drawing;
using System.Windows.Input;
using System.IO;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;
using System.IO.Pipes;
using System.Drawing.Imaging;

namespace screen_file_transmit
{
    /// <summary>
    /// 分辨率选项
    /// </summary>
    public class Resolution
    {
        public string Name { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }

    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private Rectangle _screenSize;
        public string FilePath { get; set; }
        public long FileSize { get; set; }
        public string FileSizeStr { get; set; }
        public long FileOffset { get; set; }

        public string ColorMode { get; set; } = "Black";

        public int ColorDepth { get; set; } = 1;

        public int Scale { get; set; } = 2;

        // 分辨率选择
        public List<Resolution> ResolutionList { get; } = new List<Resolution>
        {
            new Resolution { Name = "当前屏幕", Width = 0, Height = 0 },
            new Resolution { Name = "1920 x 1080 (FHD)", Width = 1920, Height = 1080 },
            new Resolution { Name = "2560 x 1440 (QHD)", Width = 2560, Height = 1440 },
            new Resolution { Name = "3840 x 2160 (4K)", Width = 3840, Height = 2160 },
            new Resolution { Name = "1280 x 720 (HD)", Width = 1280, Height = 720 },
            new Resolution { Name = "1366 x 768 (笔记本)", Width = 1366, Height = 768 },
            new Resolution { Name = "1600 x 900", Width = 1600, Height = 900 },
            new Resolution { Name = "1440 x 900", Width = 1440, Height = 900 },
            new Resolution { Name = "1680 x 1050", Width = 1680, Height = 1050 },
            new Resolution { Name = "2560 x 1080 (超宽)", Width = 2560, Height = 1080 },
            new Resolution { Name = "3440 x 1440 (超宽)", Width = 3440, Height = 1440 },
            new Resolution { Name = "自定义", Width = -1, Height = -1 }
        };

        public Resolution SelectedResolution { get; set; }

        // 自定义分辨率
        public int CustomWidth { get; set; } = 1920;
        public int CustomHeight { get; set; } = 1080;
        public bool IsCustomResolution => SelectedResolution?.Width == -1;

        public List<string> ColorModeList => new List<string>() { "Black", "RGB" };

        public List<int> ColorDepthList => new List<int>() { 1, 2, 3, 4, 5, 6, 7, 8 };
        public List<int> ScaleList => new List<int>() { 1, 2, 3, 4, 5 };

        public Rectangle ScreenSize
        {
            get => _screenSize;
            set
            {
                _screenSize = value;
                ResolutionList[0].Name = "当前 " + _screenSize.Width + " x " + _screenSize.Height;
                ResolutionList[0].Width = _screenSize.Width;
                ResolutionList[0].Height = _screenSize.Height;
            }
        }

        /// <summary>
        /// 获取屏幕工作区尺寸（去掉任务栏）
        /// </summary>
        public static Rectangle GetWorkingArea()
        {
            // 获取主屏幕的工作区（去掉任务栏）
            var workingArea = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            return new Rectangle(workingArea.X, workingArea.Y, workingArea.Width, workingArea.Height);
        }

        public MainWindowViewModel()
        {
            // 默认选择"当前屏幕"
            SelectedResolution = ResolutionList[0];
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string GetFriendlyFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        // Command for browsing files
        public ICommand BrowseFileCommand => new RelayCommand((x) => BrowseFile());
        public ICommand StartCommand => new RelayCommand((x) => StartEncoding());
        public ICommand SaveToFileCommand => new RelayCommand((x) => SaveToFile());

        private void BrowseFile()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() ?? false)
            {
                FilePath = openFileDialog.FileName;
                FileSize = new FileInfo(FilePath).Length;
                FileSizeStr = GetFriendlyFileSize(FileSize);
            }
        }

        private void StartEncoding()
        {
            try
            {
                var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read);

                fs.Seek(FileOffset, SeekOrigin.Begin);

                var window = new MatrixWindow(fs, ColorDepth, ColorMode == "RGB", Scale, Path.GetFileName(FilePath));
                window.Show();
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
            }
        }

        private void SaveToFile()
        {
            if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
            {
                MessageBox.Show("请先选择要编码的文件");
                return;
            }

            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择保存图片的文件夹"
            };

            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            try
            {
                using (var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read))
                {
                    // 获取选择的分辨率
                    int targetWidth, targetHeight;
                    if (SelectedResolution.Width == 0)
                    {
                        // 使用当前屏幕工作区（去掉任务栏）
                        var workingArea = GetWorkingArea();
                        targetWidth = workingArea.Width > 0 ? workingArea.Width : 1920;
                        targetHeight = workingArea.Height > 0 ? workingArea.Height : 1080;
                    }
                    else if (SelectedResolution.Width == -1)
                    {
                        // 使用自定义分辨率
                        targetWidth = CustomWidth;
                        targetHeight = CustomHeight;
                    }
                    else
                    {
                        // 使用预设分辨率
                        targetWidth = SelectedResolution.Width;
                        targetHeight = SelectedResolution.Height;
                    }

                    var matrix = DataMatrixEncoder.CalculateScreenDataMatrix(
                        targetWidth, targetHeight, Scale);

                    // 计算生成多少页
                    long totalBytes = fs.Length;
                    long bytesPerPage = matrix.MaxRows * matrix.MaxCols * matrix.CodeByteCount * ColorDepth *
                                        (ColorMode == "RGB" ? 3 : 1);
                    int totalPages = (int)Math.Ceiling((double)totalBytes / bytesPerPage);

                    var originalFileName = Path.GetFileNameWithoutExtension(FilePath);
                    var saveDir = dialog.SelectedPath;

                    for (int page = 0; page < totalPages; page++)
                    {
                        fs.Seek(page * bytesPerPage, SeekOrigin.Begin);

                        // 生成图片
                        var bitmap = GenerateDataMatrixBitmap(fs, matrix, ColorDepth, ColorMode == "RGB", Scale,
                            Path.GetFileName(FilePath), page == 0, page + 1, totalPages);
                        if (bitmap == null)
                            continue;

                        // 生成文件名：原文件名_yymmddhhmm_4位串号.bmp（下划线分割）
                        var timestamp = DateTime.Now.ToString("yyMMddHHmm");
                        var serial = GenerateSerialNumber(page, 4);
                        var fileName = $"{originalFileName}_{timestamp}_{serial}.bmp";
                        var fullPath = Path.Combine(saveDir, fileName);

                        bitmap.Save(fullPath, ImageFormat.Bmp);
                        bitmap.Dispose();
                    }

                    MessageBox.Show($"成功生成 {totalPages} 张图片到:\n{saveDir}");
                }
            }
            catch (Exception e)
            {
                MessageBox.Show($"保存失败: {e.Message}");
            }
        }

        /// <summary>
        /// 生成指定长度的串号，保持固定长度
        /// </summary>
        private string GenerateSerialNumber(int index, int length)
        {
            const string chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            var result = new StringBuilder(length);
            int value = index;

            for (int i = 0; i < length; i++)
            {
                result.Insert(0, chars[value % chars.Length]);
                value /= chars.Length;
            }

            return result.ToString().PadLeft(length, '0');
        }

        /// <summary>
        /// 生成 DataMatrix 图片
        /// </summary>
        private Bitmap GenerateDataMatrixBitmap(FileStream fileStream, DataMatrixResult matrix,
            int colorDepth, bool colorful, int scale, string fileName = null, bool includeFileName = false,
            int currentPage = 1, int totalPages = 1)
        {
            // 紧凑的元数据区域高度
            int infoAreaHeight = Math.Max(40 * scale, 50);

            var width = ((matrix.MaxCols * (matrix.CodeSize + 6))) * scale;
            var height = ((matrix.MaxRows * (matrix.CodeSize + 6))) * scale + infoAreaHeight;

            var bitmap = new Bitmap(width, height);
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
                var top = (((matrix.CodeSize + 6)) * row) * scale + infoAreaHeight;

                for (int column = 0; !end && column < matrix.MaxCols; column++)
                {
                    var left = (((matrix.CodeSize + 6)) * column) * scale;
                    // 只在第一个二维码且需要包含文件名时传递文件名
                    var currentFileName = (row == 0 && column == 0 && includeFileName) ? fileName : null;
                    var bitmapPart = DataMatrixEncoder.DrawDataMatrix(fileStream, row, column, scale, chuck, matrix,
                        colorDepth, colorful, currentFileName);

                    if (bitmapPart != null)
                    {
                        count++;
                        using (Graphics g = Graphics.FromImage(bitmap))
                        {
                            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                            g.DrawRectangle(new System.Drawing.Pen(System.Drawing.Brushes.Black, scale),
                                new System.Drawing.Rectangle(left + scale, top + scale, (matrix.CodeSize + 3) * scale,
                                    (matrix.CodeSize + 3) * scale));
                            g.DrawImage(bitmapPart,
                                new System.Drawing.PointF((float)(left + 2.5 * scale), (float)(top + 2.5 * scale)));
                        }
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
            DrawInfoArea(bitmap, matrix, colorful, colorDepth, offset, fileStream.Position - offset, fileStream.Length,
                count, fileName, currentPage, totalPages, scale);

            return bitmap;
        }

        /// <summary>
        /// 绘制顶部信息区域（文件名、页码、元数据二维码）
        /// </summary>
        private void DrawInfoArea(Bitmap bitmap, DataMatrixResult matrix, bool colorful, int colorDepth,
            long offset, long length, long totalLength, int count, string fileName, int currentPage, int totalPages,
            int scale)
        {
            // 使用长方形二维码节省高度
            int qrHeight = 8; // 行数（较小）
            int qrWidth = 48; // 列数（较长）
            int qrScale = scale; // 与正文保持一致

            // 计算二维码实际高度
            var testInfo = new string('A', 50);
            var testBitmap = DataMatrixEncoder.GenerateDataRectangleMatrix(testInfo, qrHeight, qrWidth, qrScale, true);
            int qrActualHeight = testBitmap.Height;
            testBitmap.Dispose();

            // 紧凑的信息区高度（根据二维码高度自适应）
            int infoAreaHeight = qrActualHeight + 8 * scale;
            int margin = 4 * scale;

            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                // 绘制白色背景
                g.FillRectangle(System.Drawing.Brushes.White,
                    new System.Drawing.Rectangle(0, 0, bitmap.Width, infoAreaHeight));

                // 左上角：元数据二维码（长方形，节省高度）
                var info =
                    $"{matrix.MaxRows},{matrix.MaxCols},{(colorful ? "1" : "0")},{colorDepth},{offset},{length},{totalLength},{count}";
                var infoBitmap = DataMatrixEncoder.GenerateDataRectangleMatrix(info, qrHeight, qrWidth, qrScale, false);

                int qrX = margin;
                int qrY = (infoAreaHeight - infoBitmap.Height) / 2;
                qrY = Math.Max(margin / 2, qrY);

                g.DrawImage(infoBitmap, new System.Drawing.Point(qrX, qrY));
                int qrRightEdge = qrX + infoBitmap.Width;
                infoBitmap.Dispose();

                // 右上角：页码
                var pageInfo = $"{currentPage} / {totalPages}";
                using (var pageFont = new System.Drawing.Font("Arial", 9 * scale, System.Drawing.FontStyle.Bold))
                {
                    var pageSize = g.MeasureString(pageInfo, pageFont);
                    int pageX = bitmap.Width - (int)pageSize.Width - margin;
                    int pageY = (infoAreaHeight - (int)pageSize.Height) / 2;
                    g.DrawString(pageInfo, pageFont, System.Drawing.Brushes.DarkBlue, pageX, pageY);
                }

                // 中间：文件名（自动截断以适应可用空间）
                var displayName = string.IsNullOrEmpty(fileName) ? "Unknown" : fileName;
                using (var nameFont =
                       new System.Drawing.Font("Microsoft YaHei", 10 * scale, System.Drawing.FontStyle.Regular))
                {
                    var nameSize = g.MeasureString(displayName, nameFont);
                    int availableWidth = bitmap.Width - qrRightEdge - margin * 3 - 100; // 预留页码空间

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
    }
}