using System.ComponentModel;

namespace screen_file_transmit
{
    /// <summary>
    /// DataGrid 行模型
    /// </summary>
    public class FileItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private int _rowNumber;
        private string _imageFileName;
        private string _fileId;
        private string _saveFileName;
        private string _metadataInfo;
        private string _status = Properties.Resources.ResourceManager.GetString("Status_Ready");
        private double _progressValue;
        private double _progressMaximum = 100;
        private bool _isComplete;
        private bool _isUnderfull;
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

        /// <summary>
        /// 表格行号，加入表格时一次性确定，之后固定不变（不随排序变化）
        /// </summary>
        public int RowNumber
        {
            get => _rowNumber;
            set
            {
                if (_rowNumber != value)
                {
                    _rowNumber = value;
                    OnPropertyChanged(nameof(RowNumber));
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

        /// <summary>
        /// 是否「非末页不满载」：解码字节数小于本页满载容量。
        /// 末页天然不满属正常，不标记。仅用于提示，不影响解码/转换。
        /// </summary>
        public bool IsUnderfull
        {
            get => _isUnderfull;
            set
            {
                if (_isUnderfull != value)
                {
                    _isUnderfull = value;
                    OnPropertyChanged(nameof(IsUnderfull));
                }
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

        /// <summary>
        /// 原始文件ID（来自元数据，来自条码）。合并文件ID后不再改变，
        /// 组合/排序一律使用 <see cref="EffectiveFileId"/>。
        /// </summary>
        public string RawFileId => _fileId;

        private string _effectiveFileId;

        /// <summary>
        /// 参与分组的文件ID。未合并时与 <see cref="RawFileId"/> 相同；
        /// 合并后为用户指定的统一文件ID。
        /// </summary>
        public string EffectiveFileId
        {
            get => _effectiveFileId;
            set
            {
                if (_effectiveFileId != value)
                {
                    _effectiveFileId = value;
                    OnPropertyChanged(nameof(EffectiveFileId));
                }
            }
        }

        /// <summary>
        /// 元数据特征（除文件ID与页码外的全部元数据项），
        /// 用于判断若干不同文件ID是否属于同一个文件。
        /// </summary>
        public string MetaSignature
        {
            get
            {
                return $"{MaxRows}x{MaxCols}|{Colorful}|{ColorDepth}|{HasPassword}|{HasErrorCorrection}|{ErrorCorrectionPercent}|{TotalPages}|{TotalQrCodeCount}";
            }
        }

        //public ICommand DeleteCommand { get; set; }
        //public ICommand RetryCommand { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}