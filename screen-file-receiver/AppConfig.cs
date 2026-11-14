using System.Collections.Generic;
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

        // 放大尝试的排序顺序（变体名，逗号分隔）
        public string UpscaleOrder { get; set; }

        // 各变体的历史统计（"名字" -> "成功/尝试"）
        public Dictionary<string, string> UpscaleStats { get; } = new Dictionary<string, string>();

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
                    UpscaleOrder = root.Element("UpscaleOrder")?.Value ?? string.Empty;

                    UpscaleStats.Clear();
                    var statsElem = root.Element("UpscaleStats");
                    if (statsElem != null)
                    {
                        foreach (var item in statsElem.Elements("Item"))
                        {
                            var key = item.Attribute("Name")?.Value;
                            var val = item.Value;
                            if (!string.IsNullOrEmpty(key)) UpscaleStats[key] = val;
                        }
                    }
                }
            }
            catch { }
        }

        public void Save()
        {
            try
            {
                var statsElem = new XElement("UpscaleStats");
                foreach (var kv in UpscaleStats)
                {
                    statsElem.Add(new XElement("Item", new XAttribute("Name", kv.Key), kv.Value));
                }

                var doc = new XDocument(
                    new XElement("Configuration",
                        new XElement("SaveDirectory", SaveDirectory ?? string.Empty),
                        new XElement("UpscaleOrder", UpscaleOrder ?? string.Empty),
                        statsElem
                    )
                );
                doc.Save(_filePath);
            }
            catch { }
        }
    }
}
