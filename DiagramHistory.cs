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
    private static readonly string Folder = Path.Combine(AppSettings.Folder, "organigrammes");
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

    public static void Save(Guid id, string name, DiagramModel model)
    {
        Directory.CreateDirectory(Folder);
        var saved = new SavedDiagram(id, name, DateTime.Now,
            model.Nodes.Select(node => new SavedNode(node.Id, node.Text, node.Kind, node.Bounds.X, node.Bounds.Y, node.Bounds.Width, node.Bounds.Height)).ToList(),
            model.Arrows.ToList());
        var target = PathFor(id);
        var temporary = target + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(saved, JsonOptions));
        File.Move(temporary, target, true);
        VaultSession.SyncToVault();
    }

    public static void Delete(Guid id)
    {
        var path = PathFor(id);
        if (File.Exists(path))
        {
            File.Delete(path);
            VaultSession.SyncToVault();
        }
    }

    // ponytail: corrupt local files stay out of the history instead of blocking the editor.
    private static SavedDiagram? Read(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<SavedDiagram>(File.ReadAllText(path), JsonOptions) : null; }
        catch (JsonException) { return null; }
    }

    private static string PathFor(Guid id) => Path.Combine(Folder, id + ".json");

    internal static void SelfCheck()
    {
        var text = JsonSerializer.Serialize(new SavedDiagram(Guid.NewGuid(), "Check", DateTime.Now, [new SavedNode(Guid.NewGuid(), "Étape", DiagramNodeKind.Process, 1, 2, 3, 4)], []), JsonOptions);
        var check = JsonSerializer.Deserialize<SavedDiagram>(text, JsonOptions) ?? throw new InvalidOperationException("History check failed.");
        if (check.Name != "Check" || check.Nodes.Single().Text != "Étape") throw new InvalidOperationException("History check failed.");
    }
}
