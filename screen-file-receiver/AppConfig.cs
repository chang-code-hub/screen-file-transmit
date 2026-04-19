using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace screen_file_transmit
{
    public class CameraCorrectionData
    {
        public double X0 { get; set; }
        public double Y0 { get; set; }
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public double X3 { get; set; }
        public double Y3 { get; set; }
    }

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
        public Dictionary<string, CameraCorrectionData> CameraCorrections { get; set; } = new Dictionary<string, CameraCorrectionData>();

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

                    var correctionsElem = root.Element("CameraCorrections");
                    if (correctionsElem != null)
                    {
                        foreach (var camElem in correctionsElem.Elements("Camera"))
                        {
                            string name = camElem.Attribute("Name")?.Value;
                            if (string.IsNullOrEmpty(name)) continue;

                            var points = camElem.Elements("Point").ToList();
                            if (points.Count >= 4)
                            {
                                CameraCorrections[name] = new CameraCorrectionData
                                {
                                    X0 = ParseDouble(points[0].Attribute("X")?.Value),
                                    Y0 = ParseDouble(points[0].Attribute("Y")?.Value),
                                    X1 = ParseDouble(points[1].Attribute("X")?.Value),
                                    Y1 = ParseDouble(points[1].Attribute("Y")?.Value),
                                    X2 = ParseDouble(points[2].Attribute("X")?.Value),
                                    Y2 = ParseDouble(points[2].Attribute("Y")?.Value),
                                    X3 = ParseDouble(points[3].Attribute("X")?.Value),
                                    Y3 = ParseDouble(points[3].Attribute("Y")?.Value)
                                };
                            }
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
                var correctionsElem = new XElement("CameraCorrections");
                foreach (var kvp in CameraCorrections)
                {
                    var data = kvp.Value;
                    correctionsElem.Add(new XElement("Camera",
                        new XAttribute("Name", kvp.Key),
                        new XElement("Point", new XAttribute("X", data.X0), new XAttribute("Y", data.Y0)),
                        new XElement("Point", new XAttribute("X", data.X1), new XAttribute("Y", data.Y1)),
                        new XElement("Point", new XAttribute("X", data.X2), new XAttribute("Y", data.Y2)),
                        new XElement("Point", new XAttribute("X", data.X3), new XAttribute("Y", data.Y3))
                    ));
                }

                var doc = new XDocument(
                    new XElement("Configuration",
                        new XElement("SaveDirectory", SaveDirectory ?? string.Empty),
                        correctionsElem
                    )
                );
                doc.Save(_filePath);
            }
            catch { }
        }

        private static double ParseDouble(string value)
        {
            if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double result))
                return result;
            return 0;
        }
    }
}
