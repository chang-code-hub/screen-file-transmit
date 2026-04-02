using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;

namespace screen_file_receiver
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private ObservableCollection<string> _selectedFiles = new ObservableCollection<string>();
        private string _password;
        private string _statusText = "就绪";
        private double _progressValue;
        private double _progressMaximum = 100;
        private bool _isBusy;
        private BitmapImage _previewImage;
        private int _selectedFileIndex = -1;

        public ObservableCollection<string> SelectedFiles
        {
            get => _selectedFiles;
            set
            {
                _selectedFiles = value;
                OnPropertyChanged(nameof(SelectedFiles));
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
                _statusText = value;
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

        public int SelectedFileIndex
        {
            get => _selectedFileIndex;
            set
            {
                _selectedFileIndex = value;
                OnPropertyChanged(nameof(SelectedFileIndex));
                LoadPreview();
            }
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
        public ICommand RemoveFileCommand => new RelayCommand(_ => RemoveFile(), _ => !IsBusy && SelectedFileIndex >= 0 && SelectedFiles.Count > 0);
        public ICommand ClearFilesCommand => new RelayCommand(_ => ClearFiles(), _ => !IsBusy && SelectedFiles.Count > 0);
        public ICommand StartCommand => new RelayCommand(_ => StartEncoding(), _ => !IsBusy && SelectedFiles.Count > 0);

        private void AddFiles()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Multiselect = true;
            openFileDialog.Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp|All Files|*.*";
            if (openFileDialog.ShowDialog() ?? false)
            {
                foreach (var file in openFileDialog.FileNames)
                {
                    if (!SelectedFiles.Contains(file))
                        SelectedFiles.Add(file);
                }
                if (SelectedFileIndex < 0 && SelectedFiles.Count > 0)
                    SelectedFileIndex = 0;
            }
        }

        public void AddFiles(IEnumerable<string> files)
        {
            foreach (var file in files)
            {
                if (!SelectedFiles.Contains(file))
                    SelectedFiles.Add(file);
            }
            if (SelectedFileIndex < 0 && SelectedFiles.Count > 0)
                SelectedFileIndex = 0;
        }

        private void RemoveFile()
        {
            if (SelectedFileIndex >= 0 && SelectedFileIndex < SelectedFiles.Count)
            {
                SelectedFiles.RemoveAt(SelectedFileIndex);
                if (SelectedFiles.Count > 0)
                    SelectedFileIndex = Math.Min(SelectedFileIndex, SelectedFiles.Count - 1);
                else
                    SelectedFileIndex = -1;
            }
        }

        private void ClearFiles()
        {
            SelectedFiles.Clear();
            SelectedFileIndex = -1;
            PreviewImage = null;
        }

        private void LoadPreview()
        {
            if (SelectedFileIndex >= 0 && SelectedFileIndex < SelectedFiles.Count)
            {
                try
                {
                    var path = SelectedFiles[SelectedFileIndex];
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(path, UriKind.Absolute);
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

        private void StartEncoding()
        {
            if (SelectedFiles.Count == 0)
                return;

            IsBusy = true;
            ProgressMaximum = SelectedFiles.Count;
            ProgressValue = 0;

            try
            {
                // 先读取第一个文件获取文件名
                string suggestedFileName = null;
                var files = SelectedFiles.ToArray();
                if (files.Length > 0)
                {
                    try
                    {
                        StatusText = "正在提取文件名...";
                        using (var tempStream = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose))
                        {
                            if (DataMatrixReader.ReadToFile(files[0], tempStream, out var extractedName, out _, out _))
                            {
                                suggestedFileName = extractedName;
                            }
                            else
                            {
                                StatusText = "解析失败";
                                IsBusy = false;
                                return;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        StatusText = $"读取错误: {ex.Message}";
                        IsBusy = false;
                        return;
                    }
                }

                SaveFileDialog saveFileDialog = new SaveFileDialog();
                if (!string.IsNullOrEmpty(suggestedFileName))
                {
                    saveFileDialog.FileName = suggestedFileName;
                }

                if (saveFileDialog.ShowDialog() ?? false)
                {
                    bool anyFailed = false;
                    using (var encryptedMs = new MemoryStream())
                    {
                        for (int i = 0; i < files.Length; i++)
                        {
                            var file = files[i];
                            StatusText = $"正在解析 {i + 1}/{files.Length}: {Path.GetFileName(file)}";
                            ProgressValue = i + 1;

                            if (string.IsNullOrEmpty(file)) continue;

                            if (!DataMatrixReader.ReadToFile(file, encryptedMs, out _, out _, out _))
                            {
                                anyFailed = true;
                            }
                        }

                        encryptedMs.Position = 0;
                        using (var fs = new FileStream(saveFileDialog.FileName, FileMode.Create, FileAccess.Write))
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

                    if (anyFailed)
                    {
                        MessageBox.Show("部分文件解析失败，已完成可解析部分");
                        StatusText = "部分完成";
                    }
                    else
                    {
                        MessageBox.Show("解析成功");
                        StatusText = "解析成功";
                    }
                }
                else
                {
                    StatusText = "已取消";
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
                ProgressValue = 0;
            }
        }
    }
}
