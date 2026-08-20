using System.Text.Json;

namespace LiethOrganigrammeAssistant;

internal static class AppSettings
{
    public static readonly string RootFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Diva Assistant");

    public static readonly string LegacyRootFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Lieth Organigramme Assistant");

    public static string Folder => VaultSession.LocalFolder ?? RootFolder;
    public static string FilePath => Path.Combine(Folder, "settings.json");

    public static void Ensure()
    {
        Directory.CreateDirectory(Folder);
        if (!File.Exists(FilePath))
            AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(new Settings()));
    }

    private sealed record Settings(bool UpdateChecksEnabled = true);
}
