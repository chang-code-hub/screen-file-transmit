using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows;
using System.Drawing;
using System.Windows.Input;
using System.IO;
using Microsoft.Win32;
using screen_file_receiver;
using MessageBox = System.Windows.MessageBox;

namespace screen_file_transmit
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {

        public string FilePath { get; set; }
        public long FileSize { get; set; }
        public string FileSizeStr { get; set; }
        public long FileOffset { get; set; }

        public string ColorMode { get; set; } = "RGB";

        public int ColorDepth { get; set; } = 1;

        public int Scale { get; set; } = 2;
        public string CodeVersion { get; set; } = "104x104";
        public List<string> ColorModeList => new List<string>() { "Black", "RGB" };

        public List<int> ColorDepthList => new List<int>() { 1, 2, 3, 4, 5, 6, 7, 8 };
        public List<int> ScaleList => new List<int>() { 1, 2, 3, 4, 5 };

        public List<string> CodeVersionList => new List<string>()
        {
            "10x10",
            "12x12",
            "14x14",
            "16x16",
            "18x18",
            "20x20",
            "22x22",
            "24x24",
            "26x26",
            "32x32",
            "36x36",
            "40x40",
            "44x44",
            "48x48",
            "52x52",
            "64x64",
            "72x72",
            "80x80",
            "88x88",
            "96x96",
            "104x104",
            "120x120",
            "132x132",
            "144x144"
        };
        public int ScreenWidth { get; set; }
        public int ScreenHeight { get; set; }
        public Rectangle ScreenSize { get; set; }

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
                var codeSize = int.Parse(CodeVersion.Split('x')[0]);
                DataMatrixReader.Read(FilePath);
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
            }
        }
    }
}
