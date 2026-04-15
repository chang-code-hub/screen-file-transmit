using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using MessageBox = System.Windows.MessageBox;

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

        public string ColorMode { get; set; } = "黑白";

        public int ColorDepth { get; set; } = 1;

        public string Password { get; set; }

        public int Scale { get; set; } = 2;

        public int ShrinkWidth { get; set; } = GetDefaultShrinkWidth();
        public int ShrinkHeight { get; set; } = GetDefaultShrinkHeight();

        // 分辨率选择
        public List<Resolution> ResolutionList { get; } = new List<Resolution>
        {
            new Resolution { Name = "当前屏幕", Width = 0, Height = 0 },
            new Resolution { Name = "1920 x 1080 (全高清)", Width = 1920, Height = 1080 },
            new Resolution { Name = "2560 x 1440 (2K)", Width = 2560, Height = 1440 },
            new Resolution { Name = "3840 x 2160 (4K)", Width = 3840, Height = 2160 },
            new Resolution { Name = "1280 x 720 (高清)", Width = 1280, Height = 720 },
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

        public List<string> ColorModeList => new List<string>() { "黑白", "彩色（高质量传输）" };

        public List<int> ColorDepthList => new List<int>() { 1, 2, 3, 4, 5, 6, 7, 8 };
        public List<int> ScaleList => new List<int>() { 2, 3, 4, 5 };

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

        public static int GetDefaultShrinkWidth()
        {
            return (int)(SystemParameters.WindowResizeBorderThickness.Left +
                         SystemParameters.WindowResizeBorderThickness.Right) + 10;
        }

        public static int GetDefaultShrinkHeight()
        {
            var taskBarHeight = SystemParameters.PrimaryScreenHeight - SystemParameters.WorkArea.Height;
            return (int)(SystemParameters.WindowCaptionHeight + taskBarHeight +
                         SystemParameters.WindowResizeBorderThickness.Top +
                         SystemParameters.WindowResizeBorderThickness.Bottom) + 10;
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

                // 如有密码则加密到临时流
                if (!string.IsNullOrEmpty(Password))
                {
                    var tempFs = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite,
                        FileShare.None, 4096, FileOptions.DeleteOnClose);
                    CryptoHelper.EncryptStream(fs, tempFs, Password);
                    fs.Dispose();
                    tempFs.Position = 0;

                    var window = new MatrixWindow(tempFs, ColorDepth, ColorMode != "黑白", Scale,
                        Path.GetFileName(FilePath), ShrinkWidth, ShrinkHeight);
                    window.Show();
                }
                else
                {
                    var window = new MatrixWindow(fs, ColorDepth, ColorMode != "黑白", Scale, Path.GetFileName(FilePath),
                        ShrinkWidth, ShrinkHeight);
                    window.Show();
                }
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
                    Stream workStream = fs;
                    FileStream encryptedTempStream = null;

                    // 如有密码则加密到临时流
                    if (!string.IsNullOrEmpty(Password))
                    {
                        encryptedTempStream = new FileStream(Path.GetTempFileName(), FileMode.Create,
                            FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
                        CryptoHelper.EncryptStream(fs, encryptedTempStream, Password);
                        encryptedTempStream.Position = 0;
                        workStream = encryptedTempStream;
                    }

                    try
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

                        // 应用缩小设定
                        int usableWidth = Math.Max(1, targetWidth - ShrinkWidth);
                        int usableHeight = Math.Max(1, targetHeight - ShrinkHeight);

                        // 计算网格布局（扣除元数据区域高度）
                        var matrix = DataMatrixEncoder.CalculateScreenDataMatrix(
                            usableWidth, usableHeight, Scale);
                        var pageInfo = DataMatrixEncoder.CalculatePageInfo(matrix, Scale);

                        // 计算生成多少页
                        long totalBytes = workStream.Length;
                        long bytesPerPage = matrix.PageByteCount * ColorDepth *
                                            (ColorMode != "黑白" ? 3 : 1);
                        int totalPages = (int)Math.Ceiling((double)totalBytes / bytesPerPage);

                        var originalFileName = Path.GetFileNameWithoutExtension(FilePath);
                        var saveDir = dialog.SelectedPath;

                        // 生成会话GUID（去掉横线）
                        var sessionGuid = DataMatrixEncoder.GenerateFileId();

                        for (int page = 0; page < totalPages; page++)
                        {
                            workStream.Seek(page * bytesPerPage, SeekOrigin.Begin);

                            // 生成图片
                            var bitmap = DataMatrixEncoder.GenerateDataMatrixBitmap((FileStream)workStream, matrix,
                                pageInfo, ColorDepth, ColorMode != "黑白", Scale,
                                Path.GetFileName(FilePath), page == 0, page + 1, totalPages, sessionGuid);
                            if (bitmap == null)
                                continue;

                            // 生成文件名：原文件名_yymmddhhmm_4位串号.png（下划线分割）
                            var timestamp = DateTime.Now.ToString("yyMMddHHmm");
                            var serial = GenerateSerialNumber(page, 4);
                            var fileName = $"{originalFileName}_{timestamp}_{serial}.png";
                            var fullPath = Path.Combine(saveDir, fileName);

                            bitmap.Save(fullPath, ImageFormat.Png);
                            bitmap.Dispose();
                        }

                        MessageBox.Show($"成功生成 {totalPages} 张图片到:\n{saveDir}");
                    }
                    finally
                    {
                        encryptedTempStream?.Dispose();
                    }
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
        /// 生成二维码矩阵图片（用于预览）
        /// </summary>
        public static Bitmap GeneratePreviewBitmap(
            FileStream fileStream,
            int screenWidth,
            int screenHeight,
            int colorDepth,
            bool colorful,
            int scale,
            string fileName,
            int currentPage,
            int totalPage,
            ref string sessionGuid,
            int shrinkWidth = 0,
            int shrinkHeight = 0)
        {
            // 第一次生成时创建 GUID
            if (string.IsNullOrEmpty(sessionGuid))
            {
                sessionGuid = DataMatrixEncoder.GenerateFileId();
            }

            int usableWidth = Math.Max(1, screenWidth - shrinkWidth);
            int usableHeight = Math.Max(1, screenHeight - shrinkHeight);
            var matrix = DataMatrixEncoder.CalculateScreenDataMatrix(usableWidth, usableHeight, scale);
            var pageInfo = DataMatrixEncoder.CalculatePageInfo(matrix, scale);

            // 使用 GenerateDataMatrixBitmap 方法生成图片
            var bitmap = DataMatrixEncoder.GenerateDataMatrixBitmap(
                fileStream, matrix, pageInfo, colorDepth, colorful, scale,
                fileName, currentPage == 1, currentPage, totalPage, sessionGuid);

            return bitmap;
        }

        /// <summary>
        /// 检查是否还有更多数据需要显示
        /// </summary>
        public static bool HasMoreData(FileStream fileStream)
        {
            return fileStream.Length > fileStream.Position;
        }
    }
}