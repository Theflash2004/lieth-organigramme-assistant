namespace LiethOrganigrammeAssistant;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        AppSettings.Ensure();
        Application.Run(new FlowchartForm());
    }
}
