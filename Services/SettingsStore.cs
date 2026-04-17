using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using TempestData.Models;

namespace TempestData.Services
{
    public static class SettingsStore
    {
        private const string SettingsFileName = "TempestSettings.json";

        private static string GetSettingsPath()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var folder = Path.Combine(localAppData, "TempestData");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, SettingsFileName);
        }

        public static async Task<TempestSettings> LoadAsync()
        {
            var path = GetSettingsPath();
            if (!File.Exists(path))
            {
                return new TempestSettings();
            }

            var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            return JsonSerializer.Deserialize<TempestSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            }) ?? new TempestSettings();
        }

        public static async Task SaveAsync(TempestSettings settings)
        {
            var path = GetSettingsPath();
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
        }
    }
}
