using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ChronicleLauncher
{
    public class LauncherSettings
    {
        private readonly string _filePath;

        public LauncherSettings(string filePath)
        {
            _filePath = filePath;
        }

        public void SaveSettings(Settings settings)
        {
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }

        public Settings LoadSettings()
        {
            if (File.Exists(_filePath))
            {
                string json = File.ReadAllText(_filePath);
                var settings = JsonSerializer.Deserialize<Settings>(json);
                if (settings != null)
                {
                    return settings;
                }
            }
             return new Settings(); // Return default settings if file doesn't exist
        }
    }

    public class Settings
    {
        public bool IsTesting { get; set; }
    }
}
