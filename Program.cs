namespace LiethOrganigrammeAssistant;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.SequenceEqual(["--self-check"], StringComparer.OrdinalIgnoreCase))
        {
            DiagramHistory.SelfCheck();
            return;
        }
        ApplicationConfiguration.Initialize();
        AppSettings.Ensure();
        Application.Run(new FlowchartForm());
    }
}
