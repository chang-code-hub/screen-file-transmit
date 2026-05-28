using System;
using System.IO;
using System.Reflection;
using System.Xml.Linq;

namespace about
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

        public string CompanyName { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string Version { get; set; } = "1.0.0";
        public string Description { get; set; } = "";

        public void Load()
        {
            if (!File.Exists(_filePath)) return;
            try
            {
                var doc = XDocument.Load(_filePath);
                var root = doc.Element("Configuration");
                if (root != null)
                {
                    CompanyName = root.Element("CompanyName")?.Value ?? "";
                    ProductName = root.Element("ProductName")?.Value ?? "";
                    Version = root.Element("Version")?.Value ?? "1.0.0";
                    Description = root.Element("Description")?.Value ?? "";
                }
            }
            catch
            {
            }
        }
    }
}