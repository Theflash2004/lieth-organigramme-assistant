using System.Text.Json;

namespace LiethOrganigrammeAssistant;

internal sealed record DiagramHistoryItem(Guid Id, string Name, DateTime SavedAt)
{
    public string DisplayName => $"{Name} — {SavedAt:dd/MM/yyyy HH:mm}";
}

internal sealed record SavedNode(Guid Id, string Text, DiagramNodeKind Kind, float X, float Y, float Width, float Height);
internal sealed record SavedDiagram(Guid Id, string Name, DateTime SavedAt, List<SavedNode> Nodes, List<DiagramArrow> Arrows);

internal static class DiagramHistory
{
    private const long MaximumFileBytes = 10 * 1024 * 1024;
    private const int MaximumNodes = 1_000;
    private const int MaximumArrows = 5_000;
    private static string Folder => Path.Combine(AppSettings.Folder, "organigrammes");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static List<DiagramHistoryItem> List()
    {
        Directory.CreateDirectory(Folder);
        return Directory.EnumerateFiles(Folder, "*.json")
            .Select(Read)
            .Where(saved => saved is not null)
            .Select(saved => new DiagramHistoryItem(saved!.Id, saved.Name, saved.SavedAt))
            .OrderByDescending(item => item.SavedAt)
            .ToList();
    }

    public static SavedDiagram? Load(Guid id) => Read(PathFor(id));

    public static bool Save(Guid id, string name, DiagramModel model)
    {
        if (model.Nodes.Count > MaximumNodes || model.Arrows.Count > MaximumArrows)
            throw new InvalidOperationException("Ce logigramme est trop volumineux pour être enregistré en toute sécurité.");
        Directory.CreateDirectory(Folder);
        var saved = new SavedDiagram(id, name, DateTime.Now,
            model.Nodes.Select(node => new SavedNode(node.Id, node.Text, node.Kind, node.Bounds.X, node.Bounds.Y, node.Bounds.Width, node.Bounds.Height)).ToList(),
            model.Arrows.ToList());
        var target = PathFor(id);
        AtomicFile.WriteAllText(target, JsonSerializer.Serialize(saved, JsonOptions));
        return VaultSession.TrySyncToVault();
    }

    public static bool Delete(Guid id)
    {
        var path = PathFor(id);
        if (File.Exists(path))
        {
            File.Delete(path);
            return VaultSession.TrySyncToVault();
        }
        return true;
    }

    private static SavedDiagram? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > MaximumFileBytes)
                throw new InvalidDataException("Le fichier de logigramme dépasse la taille autorisée.");
            var saved = JsonSerializer.Deserialize<SavedDiagram>(File.ReadAllText(path), JsonOptions);
            Validate(saved);
            return saved;
        }
        catch (Exception error) when (error is JsonException or NotSupportedException or InvalidDataException)
        {
            PreserveCorrupt(path);
            CrashReporter.Write(error);
            return null;
        }
    }

    private static void Validate(SavedDiagram? saved)
    {
        if (saved is null) throw new InvalidDataException("Logigramme vide.");
        if (saved.Nodes.Count > MaximumNodes || saved.Arrows.Count > MaximumArrows)
            throw new InvalidDataException("Logigramme trop volumineux.");
        var ids = new HashSet<Guid>();
        foreach (var node in saved.Nodes)
        {
            if (!ids.Add(node.Id) || node.Text.Length > 2_000 ||
                !float.IsFinite(node.X) || !float.IsFinite(node.Y) ||
                !float.IsFinite(node.Width) || !float.IsFinite(node.Height) ||
                node.Width is < 20 or > DiagramModel.Width || node.Height is < 20 or > DiagramModel.Height)
                throw new InvalidDataException("Un nœud du logigramme est invalide.");
        }

        if (saved.Arrows.Any(arrow => arrow.From == arrow.To || !ids.Contains(arrow.From) || !ids.Contains(arrow.To)))
            throw new InvalidDataException("Une flèche du logigramme est invalide.");
    }

    private static void PreserveCorrupt(string path)
    {
        try
        {
            var preserved = Path.Combine(
                Path.GetDirectoryName(path)!,
                Path.GetFileNameWithoutExtension(path) + $".corrompu-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Move(path, preserved, false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static string PathFor(Guid id) => Path.Combine(Folder, id + ".json");

    internal static void SelfCheck()
    {
        var text = JsonSerializer.Serialize(new SavedDiagram(Guid.NewGuid(), "Check", DateTime.Now, [new SavedNode(Guid.NewGuid(), "Étape", DiagramNodeKind.Process, 1, 2, 3, 4)], []), JsonOptions);
        var check = JsonSerializer.Deserialize<SavedDiagram>(text, JsonOptions) ?? throw new InvalidOperationException("History check failed.");
        if (check.Name != "Check" || check.Nodes.Single().Text != "Étape") throw new InvalidOperationException("History check failed.");
    }
}
