using System;
using System.Globalization;
using System.Windows.Data;

namespace screen_file_transmit
{
    /// <summary>
    /// 判断状态文本是否表示错误（包含"错误"/"失败"关键字），用于表格行高亮。
    /// </summary>
    public class StatusErrorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var status = value as string;
            if (string.IsNullOrEmpty(status)) return false;
            return status.Contains("错误") || status.Contains("失败");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
