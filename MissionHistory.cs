using System.Text.Json;

namespace LiethOrganigrammeAssistant;

internal sealed record Mission(
    Guid Id,
    string ManagerRole,
    string RecipientName,
    string RecipientEmail,
    string Task,
    DateTime DueAt,
    DateTime CreatedAt,
    string RecipientFunction = "");

internal sealed record DivaContact(string Function, string Name, string Email)
{
    public override string ToString() => $"{Function} — {Name} — {Email}";
}

internal static class MissionHistory
{
    private static readonly object Gate = new();
    private static string Folder => Path.Combine(AppSettings.Folder, "diva-productivite");
    private static string FilePath => Path.Combine(Folder, "missions.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static List<Mission> List()
    {
        lock (Gate)
        {
            Directory.CreateDirectory(Folder);
            try
            {
                return File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<List<Mission>>(File.ReadAllText(FilePath), JsonOptions) ?? []
                    : [];
            }
            catch (Exception error) when (error is JsonException or NotSupportedException)
            {
                try { File.Move(FilePath, Path.Combine(Folder, $"missions-corrompues-{DateTime.Now:yyyyMMdd-HHmmss}.json"), false); }
                catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException) { }
                CrashReporter.Write(error);
                return [];
            }
        }
    }

    public static bool Add(Mission mission)
    {
        lock (Gate)
        {
            var missions = List();
            missions.Add(mission);
            return Save(missions);
        }
    }

    private static bool Save(List<Mission> missions)
    {
        Directory.CreateDirectory(Folder);
        AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(missions, JsonOptions));
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

internal static class ContactDirectory
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static string FilePath => Path.Combine(AppSettings.Folder, "diva-productivite", "contacts.json");

    public static IReadOnlyList<DivaContact> List()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(FilePath)) return [];
                if (new FileInfo(FilePath).Length > 2 * 1024 * 1024) throw new InvalidDataException("Le carnet de contacts Diva est trop volumineux.");
                return (JsonSerializer.Deserialize<List<DivaContact>>(File.ReadAllText(FilePath), JsonOptions) ?? [])
                    .Where(IsValid).Take(500).OrderBy(contact => contact.Function, StringComparer.CurrentCultureIgnoreCase).ToArray();
            }
            catch (Exception error) when (error is JsonException or InvalidDataException or NotSupportedException)
            {
                CrashReporter.Write(error);
                return [];
            }
        }
    }

    public static void Upsert(string function, string name, string email)
    {
        var contact = new DivaContact(function.Trim(), name.Trim(), email.Trim());
        if (!IsValid(contact)) throw new InvalidOperationException("Indiquez une fonction, un nom et une adresse e-mail valides.");
        lock (Gate)
        {
            var contacts = List().Where(item => !item.Function.Equals(contact.Function, StringComparison.OrdinalIgnoreCase)).ToList();
            contacts.Add(contact);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(contacts.OrderBy(item => item.Function, StringComparer.CurrentCultureIgnoreCase), JsonOptions));
            VaultSession.TrySyncToVault();
        }
    }

    private static bool IsValid(DivaContact contact) =>
        contact.Function.Length is > 1 and <= 100 && contact.Name.Length is > 0 and <= 200 &&
        contact.Email.Length <= 254 && System.Net.Mail.MailAddress.TryCreate(contact.Email, out _);
}
