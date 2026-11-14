using System.Collections.Generic;
using System.ComponentModel;

namespace screen_file_transmit
{
    /// <summary>
    /// 合并文件ID对话框中的一组候选：
    /// 保存文件名相同、元数据除页码外全部相同、但分属不同文件ID。
    /// </summary>
    public class MergeCandidateGroup : INotifyPropertyChanged
    {
        private bool _isSelected;

        /// <summary>是否勾选合并该组</summary>
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

        /// <summary>该组统一后的保存文件名</summary>
        public string SaveFileName { get; set; }

        /// <summary>元数据特征（不含页码与文件ID）</summary>
        public string MetaSignature { get; set; }

        /// <summary>总页数</summary>
        public int TotalPages { get; set; }

        /// <summary>合并后建议使用的文件ID</summary>
        public string PreviewId { get; set; }

        /// <summary>重复页码列表（逗号分隔），为空表示合并后页码齐全且不重复</summary>
        public string DuplicatePages { get; set; }

        /// <summary>是否存在页码重复</summary>
        public bool HasPageConflict { get; set; }

        /// <summary>组内全部图片条目</summary>
        public List<FileItem> Items { get; set; } = new List<FileItem>();

        /// <summary>显示用：不同文件ID个数</summary>
        public int DistinctIdCount { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
