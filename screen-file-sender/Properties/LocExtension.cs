using System;
using System.Windows.Markup;

namespace screen_file_transmit.Properties
{
    [MarkupExtensionReturnType(typeof(string))]
    public class LocExtension : MarkupExtension
    {
        public string Key { get; set; }

        public LocExtension(string key)
        {
            Key = key;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            if (string.IsNullOrEmpty(Key))
                return string.Empty;
            return Resources.ResourceManager.GetString(Key, Resources.Culture) ?? Key;
        }
    }
}
