namespace AssistantArsef.Core;

internal static class AppPaths
{
    public static string DataRoot => Path.Combine(
        LiethOrganigrammeAssistant.VaultSession.LocalFolder
            ?? throw new InvalidOperationException("Connectez-vous à Diva avant d’ouvrir le module Cartouche."),
        "cartouche");

    public static string SettingsPath => Path.Combine(DataRoot, "settings.json");
    public static string ActiveSessionPath => Path.Combine(DataRoot, "active-session.json");
    public static string HistoryPath => Path.Combine(DataRoot, "history.json");
    public static string SchemaPath => Path.Combine(DataRoot, "private-schema.json");
    public static string TemplatesRoot => Path.Combine(DataRoot, "Templates");
    public static string UpdatesRoot => Path.Combine(LiethOrganigrammeAssistant.AppSettings.RootFolder, "updates");
}
