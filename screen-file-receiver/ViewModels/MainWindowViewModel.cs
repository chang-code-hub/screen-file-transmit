using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;

namespace screen_file_transmit
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private ObservableCollection<FileItem> _fileItems = new ObservableCollection<FileItem>();
        private string _password;
        private string _statusText = Properties.Resources.ResourceManager.GetString("Status_Ready");
        private double _progressValue;
        private double _progressMaximum = 100;
        private bool _isBusy;
        private BitmapImage _previewImage;
        private FileItem _selectedFileItem;
        private List<FileItem> _selectedFileItems = new List<FileItem>();
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

        public List<FileItem> SelectedFileItems
        {
            get => _selectedFileItems;
            set
            {
                _selectedFileItems = value;
                OnPropertyChanged(nameof(SelectedFileItems));
                CommandManager.InvalidateRequerySuggested();
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
            string[] sizes = {
                Properties.Resources.ResourceManager.GetString("Unit_B"),
                Properties.Resources.ResourceManager.GetString("Unit_KB"),
                Properties.Resources.ResourceManager.GetString("Unit_MB"),
                Properties.Resources.ResourceManager.GetString("Unit_GB"),
                Properties.Resources.ResourceManager.GetString("Unit_TB")
            };
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
        public ICommand RemoveFileCommand => new RelayCommand(_ => RemoveFile(), _ => !IsBusy && SelectedFileItems != null && SelectedFileItems.Count > 0 && FileItems.Count > 0);
        public ICommand ClearFilesCommand => new RelayCommand(_ => ClearFiles(), _ => !IsBusy && FileItems.Count > 0);
        public ICommand BrowseOutputPathCommand => new RelayCommand(_ => BrowseOutputPath(), _ => !IsBusy);
        public ICommand OpenOutputPathCommand => new RelayCommand(_ => OpenOutputPath());
        public ICommand ConvertCommand => new RelayCommand(_ => StartConvert(), _ => !IsBusy && FileItems.Count > 0 && !string.IsNullOrWhiteSpace(OutputFilePath));
        public ICommand OpenScreenshotToolCommand => new RelayCommand(_ => OpenScreenshotTool(), _ => !IsBusy);

        private void AddFiles()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Multiselect = true;
            openFileDialog.Filter = $"{Properties.Resources.ResourceManager.GetString("FileFilter_ImageFiles")}|*.png;*.jpg;*.jpeg;*.bmp|{Properties.Resources.ResourceManager.GetString("FileFilter_AllFiles")}|*.*";
            if (openFileDialog.ShowDialog() ?? false)
            {
                AddFiles(openFileDialog.FileNames);
            }
        }

        public void AddFiles(IEnumerable<string> files)
        {
            foreach (var file in files.OrderBy(c=>c))
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
                var first = group.FirstOrDefault();
                if (first == null || first.TotalPages <= 0)
                {
                    foreach (var item in group)
                        item.IsComplete = false;
                    continue;
                }

                var pages = group.Select(c => c.CurrentPage).ToList();
                bool hasDuplicate = pages.Count != pages.Distinct().Count();
                bool allPagesPresent = Enumerable.Range(1, first.TotalPages).All(p => pages.Contains(p));
                bool isComplete = !hasDuplicate && allPagesPresent;

                foreach (var item in group)
                {
                    item.IsComplete = isComplete;
                }
            }
        }

        private FileItem CreateFileItem(string filePath)
        {
            try
            {
                var meta = ImageDecoder.ReadMetadata(filePath);
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
                    ColorDepth = meta?.ColorDepth ?? 0,
                    CurrentPage = meta?.CurrentPage ?? 0,
                    TotalPages = meta?.TotalPages ?? 0,
                    TotalQrCodeCount = meta?.TotalQrCodeCount ?? 0
                };
                if (meta.TotalPages > 0)
                {
                    //item.DeleteCommand = new RelayCommand(_ => DeleteItem(item), _ => !IsBusy);
                    //item.RetryCommand = new RelayCommand(_ => RetryItem(item), _ => !IsBusy);
                    item.MetadataInfo = $"{item.MaxRows}x{item.MaxCols} {(item.Colorful ? Properties.Resources.ResourceManager.GetString("Meta_Colorful") : Properties.Resources.ResourceManager.GetString("Meta_BlackWhite"))} D={item.ColorDepth} DM={item.TotalQrCodeCount} P={item.CurrentPage}/{item.TotalPages}{(item.HasPassword ? " " + Properties.Resources.ResourceManager.GetString("Meta_HasPassword") : "")}{(item.HasErrorCorrection ? " " + string.Format(Properties.Resources.ResourceManager.GetString("Meta_ErrorCorrection"), item.ErrorCorrectionPercent) : "")}";
                }
                else
                {
                    item.Status = Properties.Resources.ResourceManager.GetString("Status_MetaParseFailed");
                }
                return item;
            }
            catch (Exception ex)
            {
                var errorItem = new FileItem
                {
                    FullPath = filePath,
                    ImageFileName = Path.GetFileName(filePath),
                    SaveFileName = Path.GetFileNameWithoutExtension(filePath),
                    Status = string.Format(Properties.Resources.ResourceManager.GetString("Status_ReadFailed"), ex.Message)
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
            if (SelectedFileItems == null || SelectedFileItems.Count == 0)
                return;

            var itemsToRemove = SelectedFileItems.ToList();
            foreach (var item in itemsToRemove)
            {
                if (FileItems.Contains(item))
                {
                    item.PropertyChanged -= FileItem_PropertyChanged;
                    FileItems.Remove(item);
                }
            }
            CheckFileComplete();
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
            var dialog = new FolderPicker
            {
                Title = Properties.Resources.ResourceManager.GetString("Dialog_SelectSaveFolder"),
                SelectedPath = OutputFilePath
            };

            if (dialog.ShowDialog(Application.Current.MainWindow) == true)
            {
                OutputFilePath = dialog.SelectedPath;
            }
        }

        private void OpenOutputPath()
        {
            var path = OutputFilePath;
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_SavePathNotExist"));
                return;
            }
            System.Diagnostics.Process.Start("explorer.exe", path);
        }
         
        private ScreenshotToolWindow screenshotToolWindow;
        private void OpenScreenshotTool()
        {
            if (string.IsNullOrWhiteSpace(OutputFilePath) || !Directory.Exists(OutputFilePath))
            {
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_SetSavePathFirst"), Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (screenshotToolWindow!=null)
            {
                screenshotToolWindow.Close();
                screenshotToolWindow = null;
            }
            else
            {
                screenshotToolWindow = new ScreenshotToolWindow(this);
                screenshotToolWindow.Show();
            }
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

        private async void StartConvert()
        {
            if (string.IsNullOrWhiteSpace(OutputFilePath))
            {
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_SelectSavePathFirst"));
                return;
            }

            if (!Directory.Exists(OutputFilePath))
            {
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_SavePathNotExist"));
                return;
            }
            var completeItems = FileItems.Where(f => f.IsComplete).ToList();
            if (completeItems.Count == 0)
            {
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_NoCompleteFiles"));
                return;
            }

            if (completeItems.Any(f => f.HasPassword && string.IsNullOrEmpty(Password)))
            {
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_PasswordRequired"));
                return;
            }

            var incompleteItems = FileItems.Where(f => !f.IsComplete).ToList();
            if (incompleteItems.Count > 0)
            {
                var result = MessageBox.Show(Properties.Resources.ResourceManager.GetString("MsgBox_SkipIncompleteFiles"), Properties.Resources.ResourceManager.GetString("MsgBox_Title_Prompt"), MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.No)
                    return;
            }

            var groups = completeItems.GroupBy(f => new { f.FileId, f.SaveFileName }).ToList();

            IsBusy = true;
            ProgressMaximum = completeItems.Count;
            ProgressValue = 0;
            StatusText = "正在解析...";

            try
            {
                bool anyFailed = await Task.Run(() =>
                {
                    bool failed = false;
                    int processedCount = 0;
                    var dispatcher = Application.Current.Dispatcher;

                    foreach (var group in groups)
                    {
                        string outputFileName = group.Key.SaveFileName;
                        if (string.IsNullOrWhiteSpace(outputFileName))
                            outputFileName = Properties.Resources.ResourceManager.GetString("Default_DecodeFileName");

                        string outputPath = Path.Combine(OutputFilePath, outputFileName);
                        outputPath = GetUniqueFilePath(outputPath);

                        using (var encryptedMs = new MemoryStream())
                        {
                            var sortedItems = group.OrderBy(f => f.CurrentPage).ToList();
                            foreach (var item in sortedItems)
                            {
                                processedCount++;

                                dispatcher.Invoke(() =>
                                {
                                    item.Status = string.Format(Properties.Resources.ResourceManager.GetString("Status_ParsingFormat"), processedCount, completeItems.Count);
                                    item.ProgressValue = 0;
                                    ProgressValue = processedCount;
                                });

                                if (string.IsNullOrEmpty(item.FullPath))
                                {
                                    dispatcher.Invoke(() => item.Status = Properties.Resources.ResourceManager.GetString("Status_EmptyPath"));
                                    failed = true;
                                    continue;
                                }

                                try
                                {
                                    if (!ImageDecoder.ReadToFile(item.FullPath, encryptedMs, false))
                                    {
                                        dispatcher.Invoke(() => item.Status = Properties.Resources.ResourceManager.GetString("Status_ParseFailed"));
                                        failed = true;
                                    }
                                    else
                                    {
                                        dispatcher.Invoke(() =>
                                        {
                                            item.Status = Properties.Resources.ResourceManager.GetString("Status_Complete");
                                            item.ProgressValue = 100;
                                        });
                                    }
                                }
                                catch (Exception ex)
                                {
                                    dispatcher.Invoke(() => item.Status = string.Format(Properties.Resources.ResourceManager.GetString("Error_ParseFailed"), ex.Message));
                                    failed = true;
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

                    return failed;
                });

                if (anyFailed)
                {
                    StatusText = Properties.Resources.ResourceManager.GetString("Status_PartialFailed");
                    SystemSounds.Exclamation.Play();
                }
                else
                {
                    StatusText = Properties.Resources.ResourceManager.GetString("Status_ParseSuccess");
                    SystemSounds.Asterisk.Play();
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
                StatusText = string.Format(Properties.Resources.ResourceManager.GetString("Error_Error"), e.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}