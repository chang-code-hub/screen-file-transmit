using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace screen_file_transmit
{
    /// <summary>
    /// 合并文件ID对话框：列出「保存文件名相同、元数据除页码外全部相同、但文件ID不同」的候选组，
    /// 由用户勾选后统一成同一个文件ID。
    /// </summary>
    public partial class MergeFileIdDialog : Window, INotifyPropertyChanged
    {
        /// <summary>用户确认后被合并的候选组</summary>
        public List<MergeCandidateGroup> SelectedGroups { get; } = new List<MergeCandidateGroup>();

        public ObservableCollection<MergeCandidateGroup> Groups { get; } =
            new ObservableCollection<MergeCandidateGroup>();

        private List<FileItem> _selectedGroupItems = new List<FileItem>();

        /// <summary>当前选中组内的图片条目（明细区）</summary>
        public List<FileItem> SelectedGroupItems
        {
            get => _selectedGroupItems;
            set
            {
                _selectedGroupItems = value;
                OnPropertyChanged(nameof(SelectedGroupItems));
            }
        }

        private bool _allSelected;

        /// <summary>表头全选复选框</summary>
        public bool AllSelected
        {
            get => _allSelected;
            set
            {
                if (_allSelected == value) return;
                _allSelected = value;
                OnPropertyChanged(nameof(AllSelected));

                foreach (var g in Groups)
                    g.IsSelected = value;
                UpdateSummary();
            }
        }

        private string _summaryText = string.Empty;

        public string SummaryText
        {
            get => _summaryText;
            set
            {
                _summaryText = value;
                OnPropertyChanged(nameof(SummaryText));
            }
        }

        public string TitleText { get; private set; } = "合并文件ID";
        public string InstructionText { get; private set; } =
            "以下文件因文件ID不同被拆成了多份，缺少部分页面。经识别，这些文件ID实际是同一个文件，合并后页面正好齐全。请确认是否合并：";
        public string SelectAllText { get; private set; } = "全选";
        public string SelectNoneText { get; private set; } = "全不选";
        public string CancelText { get; private set; } = "取消";
        public string DetailHeaderText { get; private set; } = "明细";

        public string ColumnSaveNameText { get; private set; } = "保存文件名";
        public string ColumnIdCountText { get; private set; } = "文件ID数";
        public string ColumnTargetIdText { get; private set; } = "合并后ID";
        public string ColumnTotalPagesText { get; private set; } = "总页数";
        public string ColumnDuplicatePagesText { get; private set; } = "重复页码";
        public string ColumnPageNumberText { get; private set; } = "页码";
        public string ColumnRawIdText { get; private set; } = "原文件ID";
        public string ColumnImageFileNameText { get; private set; } = "图片文件名";
        public string ColumnMetadataText { get; private set; } = "元数据";

        public MergeFileIdDialog(IEnumerable<MergeCandidateGroup> candidates)
        {
            InitializeComponent();
            DataContext = this;
            LoadLocalizedStrings();

            foreach (var c in candidates)
            {
                // 这些方案都是「合并后恰好补齐全部页码」的，全部预先勾选，由用户决定是否采用
                c.IsSelected = true;
                c.PropertyChanged += Candidate_PropertyChanged;
                Groups.Add(c);
            }

            _allSelected = Groups.Count > 0;
            OnPropertyChanged(nameof(AllSelected));
            UpdateSummary();
            GroupGrid.SelectedIndex = Groups.Count > 0 ? 0 : -1;
        }

        private void LoadLocalizedStrings()
        {
            var rm = Properties.Resources.ResourceManager;
            TitleText = rm.GetString("MergeDialog_Title") ?? TitleText;
            InstructionText = rm.GetString("MergeDialog_Instruction") ?? InstructionText;
            SelectAllText = rm.GetString("Button_SelectAll") ?? SelectAllText;
            SelectNoneText = rm.GetString("Button_SelectNone") ?? SelectNoneText;
            CancelText = rm.GetString("Button_Cancel") ?? CancelText;
            DetailHeaderText = rm.GetString("MergeDialog_Detail") ?? DetailHeaderText;
            ColumnSaveNameText = rm.GetString("Column_SaveFileName") ?? ColumnSaveNameText;
            ColumnIdCountText = rm.GetString("MergeDialog_ColumnIdCount") ?? ColumnIdCountText;
            ColumnTargetIdText = rm.GetString("MergeDialog_ColumnTargetId") ?? ColumnTargetIdText;
            ColumnTotalPagesText = rm.GetString("MergeDialog_ColumnTotalPages") ?? ColumnTotalPagesText;
            ColumnDuplicatePagesText = rm.GetString("MergeDialog_ColumnDuplicatePages") ?? ColumnDuplicatePagesText;
            ColumnPageNumberText = rm.GetString("Column_PageNumber") ?? ColumnPageNumberText;
            ColumnRawIdText = rm.GetString("MergeDialog_ColumnRawId") ?? ColumnRawIdText;
            ColumnImageFileNameText = rm.GetString("Column_ImageFileName") ?? ColumnImageFileNameText;
            ColumnMetadataText = rm.GetString("Column_Metadata") ?? ColumnMetadataText;

            OnPropertyChanged(nameof(TitleText));
            OnPropertyChanged(nameof(InstructionText));
            OnPropertyChanged(nameof(SelectAllText));
            OnPropertyChanged(nameof(SelectNoneText));
            OnPropertyChanged(nameof(CancelText));
            OnPropertyChanged(nameof(DetailHeaderText));
            OnPropertyChanged(nameof(ColumnSaveNameText));
            OnPropertyChanged(nameof(ColumnIdCountText));
            OnPropertyChanged(nameof(ColumnTargetIdText));
            OnPropertyChanged(nameof(ColumnTotalPagesText));
            OnPropertyChanged(nameof(ColumnDuplicatePagesText));
            OnPropertyChanged(nameof(ColumnPageNumberText));
            OnPropertyChanged(nameof(ColumnRawIdText));
            OnPropertyChanged(nameof(ColumnImageFileNameText));
            OnPropertyChanged(nameof(ColumnMetadataText));
        }

        private void Candidate_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MergeCandidateGroup.IsSelected))
                UpdateSummary();
        }

        private void UpdateSummary()
        {
            var selected = Groups.Where(g => g.IsSelected).ToList();
            SummaryText = string.Format(
                Properties.Resources.ResourceManager.GetString("MergeDialog_SelectedFormat") ?? "已选 {0} 组",
                selected.Count, Groups.Count);
        }

        private void GroupGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var group = GroupGrid.SelectedItem as MergeCandidateGroup;
            SelectedGroupItems = group?.Items ?? new List<FileItem>();
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var g in Groups)
                g.IsSelected = true;
            UpdateSummary();
        }

        private void BtnSelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var g in Groups)
                g.IsSelected = false;
            UpdateSummary();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            SelectedGroups.Clear();
            SelectedGroups.AddRange(Groups.Where(g => g.IsSelected));
            CloseWithResult(SelectedGroups.Count > 0);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            CloseWithResult(false);
        }

        /// <summary>
        /// DialogResult 只能在 ShowDialog 期间设置；否则抛 InvalidOperationException。
        /// </summary>
        private void CloseWithResult(bool result)
        {
            if (System.Windows.Interop.ComponentDispatcher.IsThreadModal)
                DialogResult = result;
            else
                Close();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
