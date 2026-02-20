using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CyberGemini.Models;

namespace CyberGemini.Services;

public sealed class SettingsService
{
    private static readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CyberGemini");

    private static readonly string SettingsPath = Path.Combine(SettingsFolder, "settings.json");

    public AppSettings? Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return null;
            }

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsFolder);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(SettingsPath, json);
    }
}