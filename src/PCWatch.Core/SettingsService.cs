using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace PCWatch.Core
{
    public sealed class SettingsService
    {
        public SettingsService()
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCWatch");
            FilePath = Path.Combine(folder, "settings.json");
        }

        public SettingsService(string filePath)
        {
            FilePath = filePath;
        }

        public string FilePath { get; private set; }

        public AppSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new AppSettings();
                using (FileStream stream = File.OpenRead(FilePath))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppSettings));
                    AppSettings settings = (AppSettings)serializer.ReadObject(stream);
                    if (settings == null) return new AppSettings();
                    settings.SampleIntervalMs = Math.Max(250, Math.Min(10000, settings.SampleIntervalMs));
                    if (settings.Alerts == null || settings.Alerts.Count == 0) settings.Alerts = AppSettings.DefaultAlerts();
                    if (settings.HiddenRecommendations == null) settings.HiddenRecommendations = new List<string>();
                    if (String.IsNullOrWhiteSpace(settings.EmergencyHotkey)) settings.EmergencyHotkey = "Ctrl+Alt+F12";
                    return settings;
                }
            }
            catch
            {
                return new AppSettings();
            }
        }

        public void Save(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            string folder = Path.GetDirectoryName(FilePath);
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            string temporary = FilePath + ".tmp";
            using (FileStream stream = File.Create(temporary))
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppSettings));
                serializer.WriteObject(stream, settings);
            }
            File.Copy(temporary, FilePath, true);
            File.Delete(temporary);
        }
    }
}
