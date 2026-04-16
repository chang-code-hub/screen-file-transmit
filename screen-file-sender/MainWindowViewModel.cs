using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
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

        private string _colorMode = "黑白";
        public string ColorMode
        {
            get => _colorMode;
            set
            {
                _colorMode = value;
                _appConfig.ColorMode = value;
                _appConfig.Save();
            }
        }

        private int _colorDepth = 1;
        public int ColorDepth
        {
            get => _colorDepth;
            set
            {
                _colorDepth = value;
                _appConfig.ColorDepth = value;
                _appConfig.Save();
            }
        }

        public string Password { get; set; }

        private int _scale = 2;
        public int Scale
        {
            get => _scale;
            set
            {
                _scale = value;
                _appConfig.Scale = value;
                _appConfig.Save();
            }
        }

        private int _shrinkWidth = GetDefaultShrinkWidth();
        public int ShrinkWidth
        {
            get => _shrinkWidth;
            set
            {
                _shrinkWidth = value;
                _appConfig.ShrinkWidth = value;
                _appConfig.Save();
            }
        }

        private int _shrinkHeight = GetDefaultShrinkHeight();
        public int ShrinkHeight
        {
            get => _shrinkHeight;
            set
            {
                _shrinkHeight = value;
                _appConfig.ShrinkHeight = value;
                _appConfig.Save();
            }
        }

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

        private Resolution _selectedResolution;
        public Resolution SelectedResolution
        {
            get => _selectedResolution;
            set
            {
                _selectedResolution = value;
                if (value != null)
                {
                    _appConfig.ResolutionWidth = value.Width;
                    _appConfig.ResolutionHeight = value.Height;
                    _appConfig.Save();
                }
            }
        }

        // 自定义分辨率
        private int _customWidth = 1920;
        public int CustomWidth
        {
            get => _customWidth;
            set
            {
                _customWidth = value;
                _appConfig.CustomWidth = value;
                _appConfig.Save();
            }
        }

        private int _customHeight = 1080;
        public int CustomHeight
        {
            get => _customHeight;
            set
            {
                _customHeight = value;
                _appConfig.CustomHeight = value;
                _appConfig.Save();
            }
        }
        public bool IsCustomResolution => SelectedResolution?.Width == -1;

        public List<string> ColorModeList => new List<string>() { "黑白", "彩色（高质量传输）" };

        public List<int> ColorDepthList => new List<int>() { 1, 2, 3, 4, 5, 6, 7, 8 };
        public List<int> ScaleList => new List<int>() { 2, 3, 4, 5 };
        public List<int> ErrorCorrectionList => new List<int>() { 0, 5, 10, 15, 20, 25, 30  };

        private int _errorCorrectionPercent = 0;
        public int ErrorCorrectionPercent
        {
            get => _errorCorrectionPercent;
            set
            {
                _errorCorrectionPercent = value;
                _appConfig.ErrorCorrectionPercent = value;
                _appConfig.Save();
            }
        }

        private string _saveDirectory;
        public string SaveDirectory
        {
            get => _saveDirectory;
            set
            {
                _saveDirectory = value;
                _appConfig.SaveDirectory = value;
                _appConfig.Save();
            }
        }

        public bool IsConverting { get; set; }
        public int ConversionProgress { get; set; }
        public string ConversionStatus { get; set; }
        public bool IsConvertButtonEnabled { get; set; } = true;
        public bool IsPreviewButtonEnabled { get; set; } = true;
        private CancellationTokenSource _cts;
        private readonly AppConfig _appConfig = new AppConfig();

        public bool IsPreviewMode { get; set; }
        public bool IsPreviewLoading { get; set; }
        public BitmapSource PreviewImageSource { get; set; }
        public int PreviewCurrentPage { get; set; }
        public int PreviewTotalPages { get; set; }
        public string PreviewInfoText { get; set; }

        private Stream _previewStream;
        private FileStream _previewEncryptedTempStream;
        private string _previewSessionGuid;
        private int _previewTargetWidth;
        private int _previewTargetHeight;
        private CancellationTokenSource _previewCts;

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
            _appConfig.Load();
            _saveDirectory = _appConfig.SaveDirectory;

            _scale = _appConfig.Scale;
            _colorMode = _appConfig.ColorMode;
            _colorDepth = _appConfig.ColorDepth;
            _errorCorrectionPercent = _appConfig.ErrorCorrectionPercent;
            _shrinkWidth = _appConfig.ShrinkWidth > 0 ? _appConfig.ShrinkWidth : GetDefaultShrinkWidth();
            _shrinkHeight = _appConfig.ShrinkHeight > 0 ? _appConfig.ShrinkHeight : GetDefaultShrinkHeight();
            _customWidth = _appConfig.CustomWidth;
            _customHeight = _appConfig.CustomHeight;

            var savedRes = ResolutionList.Find(r => r.Width == _appConfig.ResolutionWidth && r.Height == _appConfig.ResolutionHeight);
            _selectedResolution = savedRes ?? ResolutionList[0];
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            if(propertyName == nameof(FilePath))
            { 
                ConversionProgress = 0;
                ConversionStatus = string.Empty;
            }
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
        public ICommand BrowseSaveDirectoryCommand => new RelayCommand((x) => BrowseSaveDirectory());
        public ICommand OpenSaveDirectoryCommand => new RelayCommand((x) => OpenSaveDirectory());

        public ICommand PreviewCommand => new RelayCommand((x) => StartPreview());
        public ICommand SaveToFileCommand => new RelayCommand(async (x) => await SaveToFileAsync());
        public ICommand CancelCommand => new RelayCommand((x) => _cts?.Cancel());
        public ICommand PreviewPreviousPageCommand => new RelayCommand((x) => PreviewPreviousPage(), (x) => PreviewCurrentPage > 1 && !IsPreviewLoading);
        public ICommand PreviewNextPageCommand => new RelayCommand((x) => PreviewNextPage(), (x) => PreviewCurrentPage < PreviewTotalPages && !IsPreviewLoading);
        public ICommand PreviewBackCommand => new RelayCommand((x) => ExitPreview());

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

        private void StartPreview()
        {
            if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
            {
                MessageBox.Show("请先选择要编码的文件");
                return;
            }

            try
            {
                ExitPreview();

                var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read);
                _previewEncryptedTempStream = null;

                // 如有密码则加密到临时流
                if (!string.IsNullOrEmpty(Password))
                {
                    _previewEncryptedTempStream = new FileStream(Path.GetTempFileName(), FileMode.Create,
                        FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
                    CryptoHelper.EncryptStream(fs, _previewEncryptedTempStream, Password);
                    fs.Dispose();
                    _previewEncryptedTempStream.Position = 0;
                    _previewStream = _previewEncryptedTempStream;
                }
                else
                {
                    _previewStream = fs;
                }

                GetTargetResolution(out _previewTargetWidth, out _previewTargetHeight);
                int usableWidth = Math.Max(1, _previewTargetWidth - ShrinkWidth);
                int usableHeight = Math.Max(1, _previewTargetHeight - ShrinkHeight);

                var matrix = DataMatrixEncoder.CalculateScreenDataMatrix(
                    usableWidth, usableHeight, Scale, ErrorCorrectionPercent);

                long totalBytes = _previewStream.Length;
                long bytesPerPage = matrix.PageByteCount * ColorDepth *
                                    (ColorMode != "黑白" ? 3 : 1);
                PreviewTotalPages = (int)Math.Ceiling((double)totalBytes / bytesPerPage);
                PreviewCurrentPage = 1;
                _previewSessionGuid = null;

                IsPreviewMode = true;
                ShowPreviewPage(1);
            }
            catch (Exception e)
            {
                MessageBox.Show($"预览失败: {e.Message}");
                ExitPreview();
            }
        }

        private async void ShowPreviewPage(int page)
        {
            if (_previewStream == null) return;

            _previewCts?.Cancel();
            _previewCts = new CancellationTokenSource();
            var token = _previewCts.Token;

            IsPreviewLoading = true;
            CommandManager.InvalidateRequerySuggested();

            try
            {
                long bytesPerPage = DataMatrixEncoder.CalculateScreenDataMatrix(
                        Math.Max(1, _previewTargetWidth - ShrinkWidth), Math.Max(1, _previewTargetHeight - ShrinkHeight), Scale, ErrorCorrectionPercent)
                    .PageByteCount * ColorDepth * (ColorMode != "黑白" ? 3 : 1);

                _previewStream.Seek((page - 1) * bytesPerPage, SeekOrigin.Begin);
                byte[] pageBuffer = new byte[bytesPerPage];
                int readBytes = _previewStream.Read(pageBuffer, 0, pageBuffer.Length);
                if (readBytes < pageBuffer.Length) Array.Resize(ref pageBuffer, readBytes);

                var sessionGuid = _previewSessionGuid;
                var bitmap = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    using (var ms = new MemoryStream(pageBuffer))
                    {
                        return GeneratePreviewBitmap(
                            ms, _previewTargetWidth, _previewTargetHeight, ColorDepth,
                            ColorMode != "黑白", Scale, Path.GetFileName(FilePath),
                            page, PreviewTotalPages, ref sessionGuid,
                            ShrinkWidth, ShrinkHeight, ErrorCorrectionPercent);
                    }
                }, token);

                _previewSessionGuid = sessionGuid;

                token.ThrowIfCancellationRequested();

                if (bitmap != null)
                {
                    PreviewImageSource = DataMatrixEncoder.ConvertBitmapToBitmapSource(bitmap);
                    bitmap.Dispose();
                }
                else
                {
                    PreviewImageSource = null;
                }

                PreviewCurrentPage = page;
                PreviewInfoText = $"{Path.GetFileName(FilePath)} - 第 {page}/{PreviewTotalPages} 页";
            }
            catch (OperationCanceledException)
            {
                // 忽略取消
            }
            catch (Exception ex)
            {
                MessageBox.Show($"生成预览失败: {ex.Message}");
            }
            finally
            {
                IsPreviewLoading = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void PreviewPreviousPage()
        {
            if (PreviewCurrentPage > 1)
            {
                ShowPreviewPage(PreviewCurrentPage - 1);
            }
        }

        private void PreviewNextPage()
        {
            if (PreviewCurrentPage < PreviewTotalPages)
            {
                ShowPreviewPage(PreviewCurrentPage + 1);
            }
        }

        public void ExitPreview()
        {
            _previewCts?.Cancel();
            _previewCts = null;
            IsPreviewLoading = false;
            IsPreviewMode = false;
            PreviewImageSource = null;
            _previewSessionGuid = null;
            PreviewCurrentPage = 0;
            PreviewTotalPages = 0;
            PreviewInfoText = string.Empty;

            _previewEncryptedTempStream?.Dispose();
            _previewEncryptedTempStream = null;

            _previewStream?.Dispose();
            _previewStream = null;
        }

        private void GetTargetResolution(out int targetWidth, out int targetHeight)
        {
            if (SelectedResolution.Width == 0)
            {
                var workingArea = GetWorkingArea();
                targetWidth = workingArea.Width > 0 ? workingArea.Width : 1920;
                targetHeight = workingArea.Height > 0 ? workingArea.Height : 1080;
            }
            else if (SelectedResolution.Width == -1)
            {
                targetWidth = CustomWidth;
                targetHeight = CustomHeight;
            }
            else
            {
                targetWidth = SelectedResolution.Width;
                targetHeight = SelectedResolution.Height;
            }
        }

        private void BrowseSaveDirectory()
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择保存图片的文件夹"
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SaveDirectory = dialog.SelectedPath;
            }
        }

        private void OpenSaveDirectory()
        {
            var path = SaveDirectory;
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                MessageBox.Show("保存路径不存在");
                return;
            }
            System.Diagnostics.Process.Start("explorer.exe", path);
        }

        private async Task SaveToFileAsync()
        {
            if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
            {
                MessageBox.Show("请先选择要编码的文件");
                return;
            }

            string saveDir = SaveDirectory;
            if (string.IsNullOrEmpty(saveDir))
            {
                var dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "选择保存图片的文件夹"
                };

                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;

                saveDir = dialog.SelectedPath;
                SaveDirectory = saveDir;
            }

            IsConverting = true;
            IsConvertButtonEnabled = false;
            IsPreviewButtonEnabled = false;
            ConversionProgress = 0;
            ConversionStatus = "准备中...";
            _cts = new CancellationTokenSource();

            var progress = new Progress<(int progress, string status)>(report =>
            {
                ConversionProgress = report.progress;
                ConversionStatus = report.status;
            });

            try
            {
                int totalPages = await Task.Run(() => SaveToFileCore(saveDir, progress, _cts.Token), _cts.Token);
                ConversionStatus = $"生成成功";
                Console.Beep();
                //MessageBox.Show($"成功生成 {totalPages} 张图片到:\n{saveDir}");
            }
            catch (OperationCanceledException)
            {
                //MessageBox.Show("转换已取消");
            }
            catch (Exception e)
            {
                Console.Beep();
                ConversionStatus = $"生成失败";
                Application.Current.MainWindow.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"保存失败: {e.Message}");
                });
            }
            finally
            {
                IsConverting = false;
                IsConvertButtonEnabled = true;
                IsPreviewButtonEnabled = true;
                //ConversionProgress = 0;
                //ConversionStatus = string.Empty;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private int SaveToFileCore(string saveDir, IProgress<(int progress, string status)> progress, CancellationToken token)
        {
            int totalPages = 0;
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
                    GetTargetResolution(out int targetWidth, out int targetHeight);

                    // 应用缩小设定
                    int usableWidth = Math.Max(1, targetWidth - ShrinkWidth);
                    int usableHeight = Math.Max(1, targetHeight - ShrinkHeight);

                    // 计算网格布局（扣除元数据区域高度）
                    var matrix = DataMatrixEncoder.CalculateScreenDataMatrix(
                        usableWidth, usableHeight, Scale, ErrorCorrectionPercent);
                    var pageInfo = DataMatrixEncoder.CalculatePageInfo(matrix, Scale);

                    // 计算生成多少页
                    long totalBytes = workStream.Length;
                    long bytesPerPage = matrix.PageByteCount * ColorDepth *
                                        (ColorMode != "黑白" ? 3 : 1);
                    totalPages = (int)Math.Ceiling((double)totalBytes / bytesPerPage);

                    var originalFileName = Path.GetFileNameWithoutExtension(FilePath);

                    // 生成会话GUID（去掉横线）
                    var sessionGuid = DataMatrixEncoder.GenerateFileId();
                    var timestamp = DateTime.Now.ToString("yyMMddHHmmss");

                    for (int page = 0; page < totalPages; page++)
                    {
                        token.ThrowIfCancellationRequested();

                        workStream.Seek(page * bytesPerPage, SeekOrigin.Begin);

                        // 生成图片
                        var bitmap = DataMatrixEncoder.GenerateDataMatrixBitmap(workStream, matrix,
                            pageInfo, ColorDepth, ColorMode != "黑白", Scale,
                            Path.GetFileName(FilePath), page == 0, page + 1, totalPages, "#" + sessionGuid,
                            !string.IsNullOrEmpty(Password), ErrorCorrectionPercent);
                        if (bitmap == null)
                            continue;

                        // 生成文件名：原文件名_yymmddhhmmss_4位串号.png（下划线分割）
                        var serial = GenerateSerialNumber(page, 4);
                        var fileName = $"{originalFileName}_{timestamp}_{sessionGuid}_{serial}.png";
                        var fullPath = Path.Combine(saveDir, fileName);

                        bitmap.Save(fullPath, ImageFormat.Png);
                        bitmap.Dispose();

                        progress?.Report(((int)((page + 1) * 100.0 / totalPages), $"正在生成第 {page + 1}/{totalPages} 页..."));
                    }
                }
                finally
                {
                    encryptedTempStream?.Dispose();
                }
            }

            return totalPages;
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
            Stream stream,
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
            int shrinkHeight = 0,
            int errorCorrectionPercent = 0)
        {
            // 第一次生成时创建 GUID
            if (string.IsNullOrEmpty(sessionGuid))
            {
                sessionGuid = DataMatrixEncoder.GenerateFileId();
            }

            int usableWidth = Math.Max(1, screenWidth - shrinkWidth);
            int usableHeight = Math.Max(1, screenHeight - shrinkHeight);
            var matrix = DataMatrixEncoder.CalculateScreenDataMatrix(usableWidth, usableHeight, scale, errorCorrectionPercent);
            var pageInfo = DataMatrixEncoder.CalculatePageInfo(matrix, scale);

            // 使用 GenerateDataMatrixBitmap 方法生成图片
            var bitmap = DataMatrixEncoder.GenerateDataMatrixBitmap(
                stream, matrix, pageInfo, colorDepth, colorful, scale,
                fileName, currentPage == 1, currentPage, totalPage, sessionGuid,
                false, errorCorrectionPercent);

            return bitmap;
        }

        /// <summary>
        /// 检查是否还有更多数据需要显示
        /// </summary>
        public static bool HasMoreData(Stream stream)
        {
            return stream.Length > stream.Position;
        }
    }
}