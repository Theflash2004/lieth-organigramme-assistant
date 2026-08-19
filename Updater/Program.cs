using System.Diagnostics;

if (args.SequenceEqual(["--self-check"], StringComparer.OrdinalIgnoreCase))
{
    SelfCheck();
    return;
}

var argsMap = args.Chunk(2).ToDictionary(pair => pair[0], pair => pair.Length > 1 ? pair[1] : "", StringComparer.OrdinalIgnoreCase);
if (!int.TryParse(argsMap.GetValueOrDefault("--pid"), out var pid) || string.IsNullOrWhiteSpace(argsMap.GetValueOrDefault("--installer")) || string.IsNullOrWhiteSpace(argsMap.GetValueOrDefault("--app")))
    return;

try { Process.GetProcessById(pid).WaitForExit(); } catch { }
var installer = argsMap["--installer"];
var app = argsMap["--app"];
// ponytail: v1.0.0 did not send --install-dir, so fall back to its executable folder.
var installDir = GetInstallDirectory(argsMap, app);
var backup = Path.Combine(Path.GetDirectoryName(installer)!, "backup");
var log = Path.Combine(Path.GetDirectoryName(installer)!, "update.log");
Log(log, $"Updater started. Install directory: {installDir}");

try
{
    // ponytail: back up only the per-user app folder; settings live elsewhere and are never touched.
    CopyDirectory(installDir, backup);
}
catch (Exception error)
{
    Log(log, "Backup failed: " + error);
    StartApp(app, log);
    return;
}

try
{
    var setup = Process.Start(new ProcessStartInfo(installer, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = true });
    setup?.WaitForExit();
    if (setup?.ExitCode == 0 && File.Exists(app))
    {
        try { Directory.Delete(backup, true); } catch (Exception error) { Log(log, "Backup cleanup failed: " + error.Message); }
        Log(log, "Update installed successfully.");
        // ponytail: the installer owns the successful restart; keeping it here opens two windows.
        return;
    }
    Log(log, $"Installer failed with exit code {setup?.ExitCode}.");
}
catch (Exception error) { Log(log, "Installer failed: " + error); }

try
{
    if (Directory.Exists(installDir)) Directory.Delete(installDir, true);
    CopyDirectory(backup, installDir);
    Log(log, "Previous version restored.");
    StartApp(app, log);
}
catch (Exception error) { Log(log, "Restore failed: " + error); }

static void CopyDirectory(string source, string destination)
{
    Directory.CreateDirectory(destination);
    foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), true);
}

static void SelfCheck()
{
    var root = Path.Combine(Path.GetTempPath(), "LiethUpdaterCheck-" + Guid.NewGuid());
    var source = Path.Combine(root, "source");
    var destination = Path.Combine(root, "destination");
    try
    {
        Directory.CreateDirectory(Path.Combine(source, "nested"));
        File.WriteAllText(Path.Combine(source, "nested", "check.txt"), "ok");
        CopyDirectory(source, destination);
        if (File.ReadAllText(Path.Combine(destination, "nested", "check.txt")) != "ok") throw new InvalidOperationException("Copy check failed.");
        if (GetInstallDirectory(new Dictionary<string, string>(), @"C:\app\LiethOrganigrammeAssistant.exe") != @"C:\app") throw new InvalidOperationException("Compatibility check failed.");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

static string GetInstallDirectory(IReadOnlyDictionary<string, string> arguments, string app) =>
    string.IsNullOrWhiteSpace(arguments.GetValueOrDefault("--install-dir")) ? Path.GetDirectoryName(app)! : arguments["--install-dir"];

static void StartApp(string app, string log)
{
    try
    {
        if (File.Exists(app)) Process.Start(new ProcessStartInfo(app) { UseShellExecute = true });
    }
    catch (Exception error) { Log(log, "App restart failed: " + error); }
}

static void Log(string path, string message)
{
    try { File.AppendAllText(path, $"{DateTime.Now:O} {message}{Environment.NewLine}"); } catch { }
}
