using System;
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
        public int Scale { get; set; } = 2;
        public int ResolutionWidth { get; set; } = 0;
        public int ResolutionHeight { get; set; } = 0;
        public int CustomWidth { get; set; } = 1920;
        public int CustomHeight { get; set; } = 1080;
        public int ShrinkWidth { get; set; } = 0;
        public int ShrinkHeight { get; set; } = 0;
        public int ErrorCorrectionPercent { get; set; } = 0;
        public string ColorMode { get; set; } = "黑白";
        public int ColorDepth { get; set; } = 1;

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

                    var scaleEl = root.Element("Scale");
                    if (scaleEl != null && int.TryParse(scaleEl.Value, out var scale)) Scale = scale;

                    var rwEl = root.Element("ResolutionWidth");
                    if (rwEl != null && int.TryParse(rwEl.Value, out var rw)) ResolutionWidth = rw;

                    var rhEl = root.Element("ResolutionHeight");
                    if (rhEl != null && int.TryParse(rhEl.Value, out var rh)) ResolutionHeight = rh;

                    var cwEl = root.Element("CustomWidth");
                    if (cwEl != null && int.TryParse(cwEl.Value, out var cw)) CustomWidth = cw;

                    var chEl = root.Element("CustomHeight");
                    if (chEl != null && int.TryParse(chEl.Value, out var ch)) CustomHeight = ch;

                    var swEl = root.Element("ShrinkWidth");
                    if (swEl != null && int.TryParse(swEl.Value, out var sw)) ShrinkWidth = sw;

                    var shEl = root.Element("ShrinkHeight");
                    if (shEl != null && int.TryParse(shEl.Value, out var sh)) ShrinkHeight = sh;

                    var ecEl = root.Element("ErrorCorrectionPercent");
                    if (ecEl != null && int.TryParse(ecEl.Value, out var ec)) ErrorCorrectionPercent = ec;

                    ColorMode = root.Element("ColorMode")?.Value ?? "黑白";

                    var cdEl = root.Element("ColorDepth");
                    if (cdEl != null && int.TryParse(cdEl.Value, out var cd)) ColorDepth = cd;
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
                        new XElement("SaveDirectory", SaveDirectory ?? string.Empty),
                        new XElement("Scale", Scale),
                        new XElement("ResolutionWidth", ResolutionWidth),
                        new XElement("ResolutionHeight", ResolutionHeight),
                        new XElement("CustomWidth", CustomWidth),
                        new XElement("CustomHeight", CustomHeight),
                        new XElement("ShrinkWidth", ShrinkWidth),
                        new XElement("ShrinkHeight", ShrinkHeight),
                        new XElement("ErrorCorrectionPercent", ErrorCorrectionPercent),
                        new XElement("ColorMode", ColorMode ?? "黑白"),
                        new XElement("ColorDepth", ColorDepth)
                    )
                );
                doc.Save(_filePath);
            }
            catch { }
        }
    }
}
