using System.Text.Json;

namespace LiethOrganigrammeAssistant;

internal sealed record Mission(
    Guid Id,
    string ManagerRole,
    string RecipientName,
    string RecipientEmail,
    string Task,
    DateTime DueAt,
    DateTime CreatedAt);

internal static class MissionHistory
{
    private static readonly string Folder = Path.Combine(AppSettings.Folder, "diva-productivite");
    private static readonly string FilePath = Path.Combine(Folder, "missions.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static List<Mission> List()
    {
        Directory.CreateDirectory(Folder);
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<List<Mission>>(File.ReadAllText(FilePath), JsonOptions) ?? []
                : [];
        }
        // ponytail: an unreadable local history must not prevent creating a new mission.
        catch (JsonException)
        {
            try { File.Move(FilePath, Path.Combine(Folder, $"missions-corrompues-{DateTime.Now:yyyyMMdd-HHmmss}.json"), true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            return [];
        }
    }

    public static bool Add(Mission mission)
    {
        var missions = List();
        missions.Add(mission);
        return Save(missions);
    }

    private static bool Save(List<Mission> missions)
    {
        Directory.CreateDirectory(Folder);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(missions, JsonOptions));
        File.Move(temporary, FilePath, true);
        return VaultSession.TrySyncToVault();
    }

    internal static void SelfCheck()
    {
        var mission = new Mission(Guid.NewGuid(), "Directrice", "Camille", "camille@example.org", "Planifier la réunion", new DateTime(2026, 1, 2, 9, 30, 0), DateTime.UtcNow);
        var copy = JsonSerializer.Deserialize<Mission>(JsonSerializer.Serialize(mission, JsonOptions), JsonOptions)
            ?? throw new InvalidOperationException("Mission history check failed.");
        if (copy.RecipientEmail != mission.RecipientEmail || copy.DueAt != mission.DueAt)
            throw new InvalidOperationException("Mission history check failed.");
    }
}
