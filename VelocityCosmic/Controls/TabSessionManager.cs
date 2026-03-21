using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace VelocityCosmic
{
    public static class TabSessionManager
    {
        private static readonly string SessionPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Velocity Ui",
            "Tab.json"
        );

        public static async Task<List<MainWindow.SavedTab>> LoadAsync()
        {
            try
            {
                if (!File.Exists(SessionPath))
                    return new List<MainWindow.SavedTab>();

                var json = await File.ReadAllTextAsync(SessionPath);
                return JsonSerializer.Deserialize<List<MainWindow.SavedTab>>(json)
                       ?? new List<MainWindow.SavedTab>();
            }
            catch
            {
                return new List<MainWindow.SavedTab>();
            }
        }

        public static async Task SaveAsync(IEnumerable<MainWindow.SavedTab> tabs)
        {
            try
            {
                var directory = Path.GetDirectoryName(SessionPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(tabs, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(SessionPath, json);
            }
            catch
            {
                // Silently fail
            }
        }
    }
}
