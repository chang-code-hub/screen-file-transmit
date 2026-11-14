using System;
using System.Globalization;
using System.Windows.Data;

namespace screen_file_transmit
{
    /// <summary>
    /// 把「非末页不满载」标记转成文件列表显示文本：true → 未满，false → 空。
    /// </summary>
    public class UnderfullTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool underfull = value is bool b && b;
            return underfull
                ? Properties.Resources.ResourceManager.GetString("Mark_Underfull")
                : string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
