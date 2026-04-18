using System.IO;
using System.Reflection;
using System.Xml.Linq;

namespace screen_file_transmit
{
    public class AppConfig
    {
        private readonly string _filePath;

        public AppConfig()
        {
            var assembly = Assembly.GetEntryAssembly();
            var location = assembly?.Location ?? Assembly.GetExecutingAssembly().Location;
            var baseName = Path.Combine(Path.GetDirectoryName(location), Path.GetFileNameWithoutExtension(location));
            _filePath = baseName + ".conf";
        }

        public string SaveDirectory { get; set; }

        public void Load()
        {
            if (!File.Exists(_filePath)) return;
            try
            {
                var doc = XDocument.Load(_filePath);
                var root = doc.Element("Configuration");
                if (root != null)
                {
                    SaveDirectory = root.Element("SaveDirectory")?.Value ?? string.Empty;
                }
            }
            catch { }
        }

        public void Save()
        {
            try
            {
                var doc = new XDocument(
                    new XElement("Configuration",
                        new XElement("SaveDirectory", SaveDirectory ?? string.Empty)
                    )
                );
                doc.Save(_filePath);
            }
            catch { }
        }
    }
}
