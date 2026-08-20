using System.Diagnostics;
using System.Text.Json;
using AssistantArsef.Core;
using LiethOrganigrammeAssistant;

namespace AssistantArsef;

internal sealed record DocumentHistoryEntry(
    string Code,
    string Title,
    string DocxPath,
    string PdfPath,
    DateTime StartedAt,
    DateTime? FinishedAt,
    bool IncludedInManagement,
    string? OneDrivePath = null);

internal static class DocumentHistory
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static IReadOnlyList<DocumentHistoryEntry> Load()
    {
        try
        {
            if (!File.Exists(AppPaths.HistoryPath)) return [];
            if (new FileInfo(AppPaths.HistoryPath).Length > 5 * 1024 * 1024)
                throw new InvalidDataException("L’historique Cartouche est anormalement volumineux.");
            return JsonSerializer.Deserialize<List<DocumentHistoryEntry>>(File.ReadAllText(AppPaths.HistoryPath), JsonOptions) ?? [];
        }
        catch (Exception error) when (error is JsonException or NotSupportedException or InvalidDataException)
        {
            CrashReporter.Write(error);
            try
            {
                File.Move(AppPaths.HistoryPath, AppPaths.HistoryPath + $".corrompu-{DateTime.Now:yyyyMMdd-HHmmss}", false);
            }
            catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException) { }
            return [];
        }
    }

    public static void Started(DocumentHistoryEntry session)
    {
        try
        {
            var entries = Load().Where(x => !x.Code.Equals(session.Code, StringComparison.OrdinalIgnoreCase)).ToList();
            entries.Insert(0, new DocumentHistoryEntry(session.Code, session.Title, session.DocxPath, session.PdfPath, DateTime.Now, null, false));
            Save(entries);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            CrashReporter.Write(error);
        }
    }

    public static void Finished(string code, bool includedInManagement, string? oneDrivePath)
    {
        try
        {
            var entries = Load().ToList();
            var index = entries.FindIndex(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return;
            var entry = entries[index];
            entries[index] = entry with { FinishedAt = DateTime.Now, IncludedInManagement = includedInManagement, OneDrivePath = oneDrivePath };
            Save(entries);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            CrashReporter.Write(error);
        }
    }

    private static void Save(List<DocumentHistoryEntry> entries)
    {
        Directory.CreateDirectory(AppPaths.DataRoot);
        AtomicFile.WriteAllText(AppPaths.HistoryPath, JsonSerializer.Serialize(entries.Take(100), JsonOptions));
        VaultSession.TrySyncToVault();
    }

    internal static bool IsSafeDocumentPath(string path)
    {
        try
        {
            var root = Path.Combine(ArsefRules.DetectDesktopRoot(), ArsefRules.RootFolderName);
            var full = Path.GetFullPath(path);
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.Equals(fullRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
                   full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

internal sealed class DocumentHistoryDialog : Form
{
    private readonly ListBox list = new();
    private readonly IReadOnlyList<DocumentHistoryEntry> entries;

    public DocumentHistoryDialog(IReadOnlyList<DocumentHistoryEntry> entries)
    {
        this.entries = entries;
        Text = "Historique des documents Diva";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 420);
        Size = new Size(900, 520);
        Font = new Font("Segoe UI", 10F);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), RowCount = 2, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        list.Dock = DockStyle.Fill;
        list.HorizontalScrollbar = true;
        foreach (var entry in entries)
        {
            var state = entry.FinishedAt is null ? "En cours" : entry.OneDrivePath is null ? "Terminé" : "Terminé · OneDrive";
            list.Items.Add($"{state} — {entry.Code} — {entry.Title} — {entry.DocxPath}");
        }
        list.DoubleClick += (_, _) => OpenDocument();
        layout.Controls.Add(list, 0, 0);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        actions.Controls.Add(MakeButton("Fermer", (_, _) => Close()));
        actions.Controls.Add(MakeButton("Ouvrir le dossier", (_, _) => OpenFolder()));
        actions.Controls.Add(MakeButton("Ouvrir le document", (_, _) => OpenDocument()));
        layout.Controls.Add(actions, 0, 1);
    }

    private static Button MakeButton(string text, EventHandler click)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 34 };
        button.Click += click;
        return button;
    }

    private DocumentHistoryEntry? Selected() => list.SelectedIndex >= 0 && list.SelectedIndex < entries.Count ? entries[list.SelectedIndex] : null;

    private void OpenDocument()
    {
        var entry = Selected();
        if (entry is null) return;
        TryStart(entry.DocxPath);
    }

    private void OpenFolder()
    {
        var entry = Selected();
        if (entry is null) return;
        TryStart(Path.GetDirectoryName(entry.DocxPath)!);
    }

    private static void TryStart(string path)
    {
        if (!DocumentHistory.IsSafeDocumentPath(path)) return;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }

}
