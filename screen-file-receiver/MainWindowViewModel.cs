using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace screen_file_receiver
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private ObservableCollection<FileItem> _fileItems = new ObservableCollection<FileItem>();
        private string _password;
        private string _statusText = "就绪";
        private double _progressValue;
        private double _progressMaximum = 100;
        private bool _isBusy;
        private BitmapImage _previewImage;
        private FileItem _selectedFileItem;
        private bool _isSyncingProperty;
        private string _outputFilePath;
        private readonly AppConfig _appConfig = new AppConfig();

        public ObservableCollection<FileItem> FileItems
        {
            get => _fileItems;
            set
            {
                _fileItems = value;
                OnPropertyChanged(nameof(FileItems));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string Password
        {
            get => _password;
            set
            {
                _password = value;
                OnPropertyChanged(nameof(Password));
            }
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value?.Trim();
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public double ProgressValue
        {
            get => _progressValue;
            set
            {
                _progressValue = value;
                OnPropertyChanged(nameof(ProgressValue));
            }
        }

        public double ProgressMaximum
        {
            get => _progressMaximum;
            set
            {
                _progressMaximum = value;
                OnPropertyChanged(nameof(ProgressMaximum));
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                _isBusy = value;
                OnPropertyChanged(nameof(IsBusy));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public BitmapImage PreviewImage
        {
            get => _previewImage;
            set
            {
                _previewImage = value;
                OnPropertyChanged(nameof(PreviewImage));
            }
        }

        public FileItem SelectedFileItem
        {
            get => _selectedFileItem;
            set
            {
                _selectedFileItem = value;
                OnPropertyChanged(nameof(SelectedFileItem));
                LoadPreview();
            }
        }

        public string OutputFilePath
        {
            get => _outputFilePath;
            set
            {
                _outputFilePath = value;
                OnPropertyChanged(nameof(OutputFilePath));
                CommandManager.InvalidateRequerySuggested();
                _appConfig.SaveDirectory = value;
                _appConfig.Save();
            }
        }

        public MainWindowViewModel()
        {
            _appConfig.Load();
            _outputFilePath = _appConfig.SaveDirectory;
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

        public ICommand AddFileCommand => new RelayCommand(_ => AddFiles(), _ => !IsBusy);
        public ICommand RemoveFileCommand => new RelayCommand(_ => RemoveFile(), _ => !IsBusy && SelectedFileItem != null && FileItems.Count > 0);
        public ICommand ClearFilesCommand => new RelayCommand(_ => ClearFiles(), _ => !IsBusy && FileItems.Count > 0);
        public ICommand BrowseOutputPathCommand => new RelayCommand(_ => BrowseOutputPath(), _ => !IsBusy);
        public ICommand OpenOutputPathCommand => new RelayCommand(_ => OpenOutputPath());
        public ICommand ConvertCommand => new RelayCommand(_ => StartConvert(), _ => !IsBusy && FileItems.Count > 0 && !string.IsNullOrWhiteSpace(OutputFilePath));

        private void AddFiles()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Multiselect = true;
            openFileDialog.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp|所有文件|*.*";
            if (openFileDialog.ShowDialog() ?? false)
            {
                AddFiles(openFileDialog.FileNames);
            }
        }

        public void AddFiles(IEnumerable<string> files)
        {
            foreach (var file in files)
            {
                if (FileItems.Any(f => f.FullPath == file))
                    continue;

                var item = CreateFileItem(file);
                if (item != null)
                {
                    item.PropertyChanged += FileItem_PropertyChanged;
                    FileItems.Add(item);
                }
            }
            if (SelectedFileItem == null && FileItems.Count > 0)
                SelectedFileItem = FileItems[0];
            CheckFileComplete();
        }

        private void CheckFileComplete()
        {
            foreach (var group in FileItems.GroupBy(c => new { c.FileId, c.SaveFileName }))
            {
                HashSet<int> pages = new HashSet<int>();
                foreach (var item in group)
                {
                    pages.Add(item.CurrentPage);
                }
                if (pages.Count == group.First().TotalPages)
                {
                    foreach (var item in group)
                    {
                        item.IsComplete = true;
                    }
                }
                else
                {
                    foreach (var item in group)
                    {
                        item.IsComplete = false;
                    }
                }
            }
        }

        private FileItem CreateFileItem(string filePath)
        {
            try
            {
                var meta = DataMatrixReader.ReadMetadata(filePath);
                var item = new FileItem
                {
                    FullPath = filePath,
                    ImageFileName = Path.GetFileName(filePath),
                    FileId = meta?.FileId ?? "",
                    SaveFileName = meta?.FileName ?? Path.GetFileNameWithoutExtension(filePath),
                    RawMetadata = meta?.Metadata,
                    MaxRows = meta?.MaxRows ?? 0,
                    MaxCols = meta?.MaxCols ?? 0,
                    Colorful = meta?.Colorful ?? false,
                    HasPassword = meta?.HasPassword ?? false,
                    HasErrorCorrection = meta?.HasErrorCorrection ?? false,
                    ErrorCorrectionPercent = meta?.ErrorCorrectionPercent ?? 0,
                    PageValidLength = meta?.PageValidLength ?? 0,
                    ColorDepth = meta?.ColorDepth ?? 0,
                    CurrentPage = meta?.CurrentPage ?? 0,
                    TotalPages = meta?.TotalPages ?? 0
                };
                //item.DeleteCommand = new RelayCommand(_ => DeleteItem(item), _ => !IsBusy);
                //item.RetryCommand = new RelayCommand(_ => RetryItem(item), _ => !IsBusy);
                item.MetadataInfo = $"{item.MaxRows}x{item.MaxCols} {(item.Colorful ? "彩色" : "黑白")} D={item.ColorDepth} P={item.CurrentPage}/{item.TotalPages}{(item.HasPassword ? " 有密码" : "")}{(item.HasErrorCorrection ? $" RS={item.ErrorCorrectionPercent}%" : "")}";

                return item;
            }
            catch (Exception ex)
            {
                var errorItem = new FileItem
                {
                    FullPath = filePath,
                    ImageFileName = Path.GetFileName(filePath),
                    SaveFileName = Path.GetFileNameWithoutExtension(filePath),
                    Status = $"读取失败: {ex.Message}"
                };
                //errorItem.DeleteCommand = new RelayCommand(_ => DeleteItem(errorItem), _ => !IsBusy);
                //errorItem.RetryCommand = new RelayCommand(_ => RetryItem(errorItem), _ => !IsBusy);
                return errorItem;
            }
        }

        private void FileItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FileItem.SaveFileName) && !_isSyncingProperty)
            {
                var changedItem = sender as FileItem;
                if (changedItem == null || string.IsNullOrEmpty(changedItem.FileId))
                    return;

                _isSyncingProperty = true;
                foreach (var item in FileItems)
                {
                    if (item != changedItem && item.FileId == changedItem.FileId)
                    {
                        item.SaveFileName = changedItem.SaveFileName;
                    }
                }
                _isSyncingProperty = false;
                CheckFileComplete();
            }
        }

        private void RemoveFile()
        {
            if (SelectedFileItem != null && FileItems.Contains(SelectedFileItem))
            {
                var index = FileItems.IndexOf(SelectedFileItem);
                SelectedFileItem.PropertyChanged -= FileItem_PropertyChanged;
                FileItems.Remove(SelectedFileItem);
                if (FileItems.Count > 0)
                    SelectedFileItem = FileItems[Math.Min(index, FileItems.Count - 1)];
                else
                    SelectedFileItem = null;
                CheckFileComplete();
            }
        }

        private void ClearFiles()
        {
            foreach (var item in FileItems)
                item.PropertyChanged -= FileItem_PropertyChanged;
            FileItems.Clear();
            SelectedFileItem = null;
            PreviewImage = null;
        }

        private void DeleteItem(FileItem item)
        {
            if (item == null) return;
            if (SelectedFileItem == item)
            {
                var index = FileItems.IndexOf(item);
                FileItems.Remove(item);
                if (FileItems.Count > 0)
                    SelectedFileItem = FileItems[Math.Min(index, FileItems.Count - 1)];
                else
                    SelectedFileItem = null;
            }
            else
            {
                FileItems.Remove(item);
            }
            item.PropertyChanged -= FileItem_PropertyChanged;
        }

        //private void RetryItem(FileItem item)
        //{
        //    if (item == null || string.IsNullOrEmpty(item.FullPath))
        //        return;

        //    item.Status = "重试中...";
        //    item.ProgressValue = 0;
        //    try
        //    {
        //        using (var tempStream = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose))
        //        {
        //            if (DataMatrixReader.ReadToFile(item.FullPath, tempStream, false))
        //            {
        //                item.Status = "就绪";
        //                if (!string.IsNullOrEmpty(extractedName) && string.IsNullOrEmpty(item.SaveFileName))
        //                    item.SaveFileName = extractedName;
        //            }
        //            else
        //            {
        //                item.Status = "解析失败";
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        item.Status = $"错误: {ex.Message}";
        //    }
        //}

        private void LoadPreview()
        {
            if (SelectedFileItem != null && !string.IsNullOrEmpty(SelectedFileItem.FullPath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(SelectedFileItem.FullPath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 128;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    PreviewImage = bitmap;
                }
                catch
                {
                    PreviewImage = null;
                }
            }
            else
            {
                PreviewImage = null;
            }
        }

        private void BrowseOutputPath()
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "选择保存文件夹";
                if (!string.IsNullOrWhiteSpace(OutputFilePath))
                {
                    dialog.SelectedPath = OutputFilePath;
                }
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    OutputFilePath = dialog.SelectedPath;
                }
            }
        }

        private void OpenOutputPath()
        {
            var path = OutputFilePath;
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                MessageBox.Show("保存路径不存在");
                return;
            }
            System.Diagnostics.Process.Start("explorer.exe", path);
        }

        private string GetUniqueFilePath(string basePath)
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

        private void StartConvert()
        {
            if (string.IsNullOrWhiteSpace(OutputFilePath))
            {
                MessageBox.Show("请先选择保存路径");
                return;
            }

            if (!Directory.Exists(OutputFilePath))
            {
                MessageBox.Show("保存路径不存在");
                return;
            }
            var completeItems = FileItems.Where(f => f.IsComplete).ToList();
            if (completeItems.Count == 0)
            {
                MessageBox.Show("没有可转换的完整文件");
                return;
            }

            if (completeItems.Any(f => f.HasPassword && string.IsNullOrEmpty(Password)))
            {
                MessageBox.Show("存在需要密码的文件，请先输入密码");
                return;
            }

            var incompleteItems = FileItems.Where(f => !f.IsComplete).ToList();
            if (incompleteItems.Count > 0)
            {
                var result = MessageBox.Show("存在未解析完成的文件，是否跳过这些文件继续转换？", "提示", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.No)
                    return;
            }

            var groups = completeItems.GroupBy(f => new { f.FileId, f.SaveFileName }).ToList();

            IsBusy = true;
            ProgressMaximum = completeItems.Count;
            ProgressValue = 0;

            try
            {
                bool anyFailed = false;
                int processedCount = 0;

                foreach (var group in groups)
                {
                    string outputFileName = group.Key.SaveFileName;
                    if (string.IsNullOrWhiteSpace(outputFileName))
                        outputFileName = "解码文件.bin";

                    string outputPath = Path.Combine(OutputFilePath, outputFileName);
                    outputPath = GetUniqueFilePath(outputPath);

                    using (var encryptedMs = new MemoryStream())
                    {
                        var sortedItems = group.OrderBy(f => f.CurrentPage).ToList();
                        foreach (var item in sortedItems)
                        {
                            processedCount++;
                            item.Status = $"正在解析 {processedCount}/{completeItems.Count}";
                            item.ProgressValue = 0;
                            ProgressValue = processedCount;

                            if (string.IsNullOrEmpty(item.FullPath))
                            {
                                item.Status = "路径为空";
                                anyFailed = true;
                                continue;
                            }

                            if (!DataMatrixReader.ReadToFile(item.FullPath, encryptedMs, false))
                            {
                                item.Status = "解析失败";
                                anyFailed = true;
                            }
                            else
                            {
                                item.Status = "完成";
                                item.ProgressValue = 100;
                            }
                        }

                        encryptedMs.Position = 0;
                        using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                        {
                            if (!string.IsNullOrEmpty(Password))
                            {
                                CryptoHelper.DecryptStream(encryptedMs, fs, Password);
                            }
                            else
                            {
                                encryptedMs.CopyTo(fs);
                            }
                        }
                    }
                }

                if (anyFailed)
                {
                    //MessageBox.Show("部分文件解析失败，已完成可解析部分");
                    StatusText = "部分文件解析失败，已完成可解析部分";
                    Console.Beep();
                }
                else
                {
                    //MessageBox.Show("解析成功");
                    StatusText = "解析成功";
                    Console.Beep();
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
                StatusText = $"错误: {e.Message}";
            }
            finally
            {
                IsBusy = false;
                //ProgressValue = 0;
            }
        }
    }
}