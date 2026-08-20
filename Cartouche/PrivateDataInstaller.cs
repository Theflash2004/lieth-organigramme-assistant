using System.IO.Compression;
using AssistantArsef.Core;

namespace AssistantArsef;

internal static class PrivateDataInstaller
{
    private const int MaximumEntries = 20_000;
    private const long MaximumBytes = 2L * 1024 * 1024 * 1024;

    public static void Install(string archivePath)
    {
        ExtractArchive(archivePath, Path.Combine(ArsefRules.DetectDesktopRoot(), ArsefRules.RootFolderName));
        var oneDrive = FindOneDriveRoot();
        if (oneDrive is not null)
            ExtractArchive(archivePath, Path.Combine(oneDrive, "ARSEF", "Desktop", ArsefRules.RootFolderName));
    }

    private static void ExtractArchive(string archivePath, string destinationRoot)
    {
        if (!File.Exists(archivePath)) throw new FileNotFoundException("L’archive ARSEF est introuvable.", archivePath);
        var root = Path.GetFullPath(destinationRoot);
        var boundary = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);

        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaximumEntries || archive.Entries.Sum(entry => entry.Length) > MaximumBytes)
            throw new InvalidDataException("L’archive ARSEF dépasse les limites de sécurité.");

        foreach (var entry in archive.Entries)
        {
            var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var destination = Path.GetFullPath(Path.Combine(root, relative));
            if (!destination.StartsWith(boundary, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("L’archive ARSEF contient un chemin non autorisé.");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }
            if (File.Exists(destination)) continue;

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporary = destination + ".diva-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var source = entry.Open())
                using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    source.CopyTo(target);
                File.Move(temporary, destination, false);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private static string? FindOneDriveRoot()
    {
        foreach (var name in new[] { "OneDriveCommercial", "OneDrive", "OneDriveConsumer" })
        {
            var path = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) return Path.GetFullPath(path);
        }
        return null;
    }

    internal static void SelfCheck()
    {
        var root = Path.Combine(Path.GetTempPath(), "DivaPrivateDataCheck-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var archivePath = Path.Combine(root, "data.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("long-folder/document.txt");
                using (var writer = new StreamWriter(entry.Open())) writer.Write("new");
                var longEntry = archive.CreateEntry(string.Join('/', Enumerable.Repeat("long-folder-name", 16)) + "/long-document.txt");
                using (var longWriter = new StreamWriter(longEntry.Open())) longWriter.Write("long");
            }
            var destination = Path.Combine(root, "destination");
            var existing = Path.Combine(destination, "long-folder", "document.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
            File.WriteAllText(existing, "existing");
            ExtractArchive(archivePath, destination);
            if (File.ReadAllText(existing) != "existing") throw new InvalidOperationException("Private data overwrite check failed.");
            var longPath = Path.Combine(destination, Path.Combine(Enumerable.Repeat("long-folder-name", 16).ToArray()), "long-document.txt");
            if (longPath.Length <= 260 || File.ReadAllText(longPath) != "long") throw new InvalidOperationException("Private data long-path check failed.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
