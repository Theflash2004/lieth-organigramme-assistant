using AssistantArsef;
using AssistantArsef.Core;

namespace LiethOrganigrammeAssistant;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.SequenceEqual(["--self-check"], StringComparer.OrdinalIgnoreCase))
        {
            RunSelfChecks();
            return;
        }

        using var instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            MessageBox.Show(
                "Diva Assistant est déjà ouvert. Retrouvez-le dans la barre des tâches.",
                "Diva Assistant", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        CrashReporter.Initialize();

        try
        {
            LegacyMigration.PrepareGlobalStorage();
            using var login = new LoginForm();
            if (login.ShowDialog() != DialogResult.OK) return;

            LegacyMigration.MigrateCartoucheProfile();
            AppSettings.Ensure();

            var schema = DivaSchema.Load(AssistantArsef.Core.AppPaths.SchemaPath);
            ArsefRules.Configure(schema);
            TemplateCatalog.Configure(schema);
            VaultSession.TrySyncToVault();

            var postUpdateMarker = CommandLine.ValueAfter(args, "--post-update");
            using var main = new MainForm(postUpdateMarker);
            Application.Run(main);
        }
        catch (Exception error)
        {
            CrashReporter.ReportFatal(error);
        }
        finally
        {
            VaultSession.EndSession();
        }
    }

    private static void RunSelfChecks()
    {
        DiagramHistory.SelfCheck();
        MissionHistory.SelfCheck();
        ProductivityForm.SelfCheck();
        VaultSession.SelfCheck();
        UpdateService.SelfCheck();
        SingleInstance.SelfCheck();
        LegacyMigration.SelfCheck();
        ArsefRulesSelfCheck.Run();
        ExcelDocumentService.SelfCheck();
        OneDriveArsefCopy.SelfCheck();
    }
}

internal static class CommandLine
{
    public static string? ValueAfter(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index + 1 < args.Count; index++)
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        return null;
    }
}

internal static class SingleInstance
{
    public static Mutex? TryAcquire(string name = "Local\\DivaAssistant")
    {
        var mutex = new Mutex(true, name, out var created);
        if (created) return mutex;
        mutex.Dispose();
        return null;
    }

    internal static void SelfCheck()
    {
        var name = "Local\\DivaAssistant-" + Guid.NewGuid();
        using var first = TryAcquire(name);
        using var second = TryAcquire(name);
        if (first is null || second is not null)
            throw new InvalidOperationException("Single-instance check failed.");
    }
}
