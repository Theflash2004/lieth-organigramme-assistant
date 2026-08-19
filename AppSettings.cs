using System.Text.Json;

namespace LiethOrganigrammeAssistant;

internal static class AppSettings
{
    public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lieth Organigramme Assistant");
    public static readonly string FilePath = Path.Combine(Folder, "settings.json");

    public static void Ensure()
    {
        Directory.CreateDirectory(Folder);
        if (!File.Exists(FilePath))
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new Settings()));
    }

    private sealed record Settings(bool UpdateChecksEnabled = true);
}
