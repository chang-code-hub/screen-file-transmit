using System;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;

namespace screen_file_receiver
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        public string FilePath { get; set; }

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

        private void BrowseFile()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Multiselect = true;
            if (openFileDialog.ShowDialog() ?? false)
            {
                FilePath = string.Join(";", openFileDialog.FileNames);
            }
        }

        private void StartEncoding()
        {
            try
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog();

                if (saveFileDialog.ShowDialog() ?? false)
                {
                    using (var fs = new FileStream(saveFileDialog.FileName, FileMode.OpenOrCreate, FileAccess.Write))
                    {
                        var files = FilePath.Split(';');
                        foreach (var file in files)
                        {
                            if (!DataMatrixReader.ReadToFile(file, fs))
                            {
                                break;
                            }
                        }
                    }
                }

                MessageBox.Show("解析成功");
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
            }
        }
    }
}