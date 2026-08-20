using System.Text;

namespace LiethOrganigrammeAssistant;

internal static class AtomicFile
{
    public static void WriteAllText(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            File.Move(temporary, path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    public static void WriteAllBytes(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}

internal static class LegacyMigration
{
    private static readonly string LegacyCartoucheRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DivaCartoucheAssistant");

    public static void PrepareGlobalStorage()
    {
        Directory.CreateDirectory(AppSettings.RootFolder);
        CopyIfMissing(
            Path.Combine(AppSettings.LegacyRootFolder, "vault-location.txt"),
            Path.Combine(AppSettings.RootFolder, "vault-location.txt"));
        CopyTreeMissing(
            Path.Combine(AppSettings.LegacyRootFolder, "profiles"),
            Path.Combine(AppSettings.RootFolder, "profiles"));
    }

    public static void MigrateCartoucheProfile()
    {
        if (VaultSession.LocalFolder is null || !Directory.Exists(LegacyCartoucheRoot)) return;

        var target = AssistantArsef.Core.AppPaths.DataRoot;
        Directory.CreateDirectory(target);
        foreach (var file in new[]
                 {
                     "settings.json",
                     "active-session.json",
                     "history.json",
                     "private-schema.json"
                 })
            CopyIfMissing(Path.Combine(LegacyCartoucheRoot, file), Path.Combine(target, file));

        CopyTreeMissing(
            Path.Combine(LegacyCartoucheRoot, "Templates"),
            AssistantArsef.Core.AppPaths.TemplatesRoot);

        AtomicFile.WriteAllText(Path.Combine(target, "migration-v2.completed"), DateTime.UtcNow.ToString("O"));
        VaultSession.TrySyncToVault();
    }

    private static void CopyTreeMissing(string source, string destination)
    {
        if (!Directory.Exists(source)) return;
        var sourceRoot = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
            var relative = Path.GetRelativePath(source, directory);
            var target = SafeTarget(destination, relative);
            Directory.CreateDirectory(target);
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var full = Path.GetFullPath(file);
            if (!full.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase)) continue;
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) continue;
            CopyIfMissing(full, SafeTarget(destination, Path.GetRelativePath(source, full)));
        }
    }

    private static string SafeTarget(string destination, string relative)
    {
        var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(root, relative));
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Le chemin de migration est invalide.");
        return target;
    }

    private static void CopyIfMissing(string source, string destination)
    {
        if (!File.Exists(source) || File.Exists(destination)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, false);
    }

    internal static void SelfCheck()
    {
        var root = Path.Combine(Path.GetTempPath(), "DivaMigrationCheck-" + Guid.NewGuid().ToString("N"));
        try
        {
            var source = Path.Combine(root, "source");
            var target = Path.Combine(root, "target");
            Directory.CreateDirectory(Path.Combine(source, "nested"));
            File.WriteAllText(Path.Combine(source, "nested", "value.txt"), "original");
            CopyTreeMissing(source, target);
            if (File.ReadAllText(Path.Combine(target, "nested", "value.txt")) != "original")
                throw new InvalidOperationException("Migration copy check failed.");
            File.WriteAllText(Path.Combine(source, "nested", "value.txt"), "changed");
            CopyTreeMissing(source, target);
            if (File.ReadAllText(Path.Combine(target, "nested", "value.txt")) != "original")
                throw new InvalidOperationException("Migration overwrite protection failed.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}

internal static class CrashReporter
{
    private static int reporting;

    public static void Initialize()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) => ReportFatal(eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception error) Write(error);
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            Write(eventArgs.Exception);
            eventArgs.SetObserved();
        };
    }

    public static void ReportFatal(Exception error)
    {
        Write(error);
        if (Interlocked.Exchange(ref reporting, 1) != 0) return;
        try
        {
            MessageBox.Show(
                "Diva a rencontré un problème inattendu. Vos fichiers existants n’ont pas été supprimés. " +
                "Fermez puis rouvrez l’application ; si le problème revient, transmettez le journal local au support.",
                "Diva Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Interlocked.Exchange(ref reporting, 0);
        }
    }

    public static void Write(Exception error)
    {
        try
        {
            var folder = Path.Combine(AppSettings.RootFolder, "logs");
            Directory.CreateDirectory(folder);
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var details = $"{DateTime.UtcNow:O}{Environment.NewLine}{error}";
            if (!string.IsNullOrWhiteSpace(profile))
                details = details.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
            File.AppendAllText(Path.Combine(folder, "diva-error.log"), details + Environment.NewLine + Environment.NewLine, new UTF8Encoding(false));
            Trim(folder);
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }
    }

    private static void Trim(string folder)
    {
        var path = Path.Combine(folder, "diva-error.log");
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 1_000_000) return;
        var archive = Path.Combine(folder, "diva-error-previous.log");
        File.Move(path, archive, true);
    }
}

internal static class StaTask
{
    public static Task<T> Run<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(action()); }
            catch (Exception error) { completion.SetException(error); }
        })
        {
            IsBackground = true,
            Name = "Diva Office worker"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    public static Task Run(Action action) => Run(() =>
    {
        action();
        return true;
    });
}
