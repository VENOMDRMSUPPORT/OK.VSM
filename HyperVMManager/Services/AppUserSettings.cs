using System;
using System.IO;
using System.Text.Json;

namespace HyperVMManager.Services;

public sealed class AppUserSettings
{
    private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions { WriteIndented = true };

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppBrand.InternalAppDataFolder,
        "user-settings.json");

    public bool HasSeenFirstRunTips { get; set; }

    /// <summary>
    /// المسار الذي اختاره المستخدم آخر مرة كمجلد أساسي لـ VM disks (Auto أو Custom).
    /// يُستخدم لتذكر التفضيل بين الجلسات.
    /// </summary>
    public string? LastUsedVmPath { get; set; }

    public static AppUserSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new AppUserSettings();
            }

            return JsonSerializer.Deserialize<AppUserSettings>(File.ReadAllText(FilePath), JsonOpts) ?? new AppUserSettings();
        }
        catch
        {
            return new AppUserSettings();
        }
    }

    public void Save()
    {
        string? dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
    }
}
