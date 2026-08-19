namespace LiethOrganigrammeAssistant;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.SequenceEqual(["--self-check"], StringComparer.OrdinalIgnoreCase))
        {
            DiagramHistory.SelfCheck();
            MissionHistory.SelfCheck();
            ProductivityForm.SelfCheck();
            VaultSession.SelfCheck();
            UpdateService.SelfCheck();
            SingleInstance.SelfCheck();
            return;
        }
        using var instance = SingleInstance.TryAcquire();
        if (instance is null) return;
        ApplicationConfiguration.Initialize();
        using var login = new LoginForm();
        if (login.ShowDialog() != DialogResult.OK) return;
        AppSettings.Ensure();
        VaultSession.TrySyncToVault();
        Application.Run(new FlowchartForm());
    }
}

internal static class SingleInstance
{
    public static Mutex? TryAcquire(string name = "Local\\LiethOrganigrammeAssistant")
    {
        var mutex = new Mutex(true, name, out var created);
        if (created) return mutex;
        mutex.Dispose();
        return null;
    }

    internal static void SelfCheck()
    {
        var name = "Local\\LiethOrganigrammeAssistant-" + Guid.NewGuid();
        using var first = TryAcquire(name);
        using var second = TryAcquire(name);
        if (first is null || second is not null) throw new InvalidOperationException("Single-instance check failed.");
    }
}
