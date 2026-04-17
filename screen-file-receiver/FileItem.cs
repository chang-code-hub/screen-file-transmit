using System.ComponentModel;

namespace screen_file_receiver
{
    /// <summary>
    /// DataGrid 行模型
    /// </summary>
    public class FileItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _imageFileName;
        private string _fileId;
        private string _saveFileName;
        private string _metadataInfo;
        private string _status = "就绪";
        private double _progressValue;
        private double _progressMaximum = 100;
        private bool _isComplete;
        private string _fullPath;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public string ImageFileName
        {
            get => _imageFileName;
            set
            {
                _imageFileName = value;
                OnPropertyChanged(nameof(ImageFileName));
            }
        }

        public string FileId
        {
            get => _fileId;
            set
            {
                _fileId = value;
                OnPropertyChanged(nameof(FileId));
            }
        }

        public string SaveFileName
        {
            get => _saveFileName;
            set
            {
                _saveFileName = value;
                OnPropertyChanged(nameof(SaveFileName));
            }
        }

        public string MetadataInfo
        {
            get => _metadataInfo;
            set
            {
                _metadataInfo = value;
                OnPropertyChanged(nameof(MetadataInfo));
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged(nameof(Status));
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

        public bool IsComplete
        {
            get => _isComplete;
            set
            {
                _isComplete = value;
                OnPropertyChanged(nameof(IsComplete));
            }
        }

        public string FullPath
        {
            get => _fullPath;
            set
            {
                _fullPath = value;
                OnPropertyChanged(nameof(FullPath));
            }
        }

        public byte[] RawMetadata { get; set; }

        public int MaxRows { get; set; }
        public int MaxCols { get; set; }
        public bool Colorful { get; set; }
        public bool HasPassword { get; set; }
        public bool HasErrorCorrection { get; set; }
        public int ErrorCorrectionPercent { get; set; }
        public int ColorDepth { get; set; }
        public int CurrentPage { get; set; }
        public int TotalPages { get; set; }
        public int TotalQrCodeCount { get; set; }

        //public ICommand DeleteCommand { get; set; }
        //public ICommand RetryCommand { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}