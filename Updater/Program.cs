using System.Diagnostics;
using System.Globalization;

if (args.SequenceEqual(["--self-check"], StringComparer.OrdinalIgnoreCase))
{
    SelfCheck();
    return;
}

var arguments = ParseArguments(args);
if (!int.TryParse(arguments.GetValueOrDefault("--pid"), NumberStyles.None, CultureInfo.InvariantCulture, out var parentPid)) return;

string installer;
string app;
string installDir;
string marker;
try { (installer, app, installDir, marker) = ValidatePaths(arguments); }
catch { return; }

var updateFolder = Path.GetDirectoryName(marker)!;
var backup = Path.Combine(updateFolder, "install-backup");
var log = Path.Combine(updateFolder, "update.log");
try
{
    if (!WaitForExit(parentPid, TimeSpan.FromMinutes(2)))
        throw new InvalidOperationException("Diva Assistant did not close before the update timeout.");
    CopyDirectory(installDir, backup);
    var setup = Process.Start(new ProcessStartInfo(installer, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = true });
    setup?.WaitForExit();
    if (setup?.ExitCode != 0) throw new InvalidOperationException($"Installer exit code: {setup?.ExitCode}");

    var updated = Process.Start(new ProcessStartInfo(app) { UseShellExecute = true, ArgumentList = { "--post-update", marker } })
                  ?? throw new InvalidOperationException("The updated application did not start.");
    if (!WaitForMarker(marker, updated, TimeSpan.FromSeconds(60)))
    {
        try { updated.Kill(true); } catch { }
        throw new InvalidOperationException("The updated application did not confirm a healthy startup.");
    }

    Directory.Delete(backup, true);
    Log(log, "Update installed and verified.");
}
catch (Exception error)
{
    Log(log, "Update failed; restoring the previous installation. " + error);
    try
    {
        if (Directory.Exists(installDir)) Directory.Delete(installDir, true);
        CopyDirectory(backup, installDir);
        Process.Start(new ProcessStartInfo(app) { UseShellExecute = true });
        Log(log, "Previous installation restored.");
    }
    catch (Exception restoreError) { Log(log, "Restore failed. " + restoreError); }
}

static Dictionary<string, string> ParseArguments(string[] values)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index + 1 < values.Length; index += 2)
        if (values[index].StartsWith("--", StringComparison.Ordinal) && !result.ContainsKey(values[index]))
            result[values[index]] = values[index + 1];
    return result;
}

static (string Installer, string App, string InstallDir, string Marker) ValidatePaths(IReadOnlyDictionary<string, string> values)
{
    var installer = Path.GetFullPath(values["--installer"]);
    var app = Path.GetFullPath(values["--app"]);
    var installDir = Path.GetFullPath(values["--install-dir"]).TrimEnd(Path.DirectorySeparatorChar);
    var marker = Path.GetFullPath(values["--marker"]);
    var updatesRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Diva Assistant", "updates");
    var programsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");

    if (!IsUnder(installer, updatesRoot) || !IsUnder(marker, updatesRoot) ||
        !Path.GetExtension(installer).Equals(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(installer))
        throw new InvalidOperationException("Invalid update files.");
    if (!IsUnder(installDir, programsRoot) || !Directory.Exists(installDir) ||
        !Path.GetDirectoryName(app)!.Equals(installDir, StringComparison.OrdinalIgnoreCase) ||
        !Path.GetFileName(app).Equals("DivaAssistant.exe", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Invalid installation directory.");
    if ((File.GetAttributes(installDir) & FileAttributes.ReparsePoint) != 0)
        throw new InvalidOperationException("Reparse-point installations cannot be updated safely.");
    return (installer, app, installDir, marker);
}

static bool IsUnder(string path, string root)
{
    var fullPath = Path.GetFullPath(path);
    var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
}

static bool WaitForExit(int pid, TimeSpan timeout)
{
    try { return Process.GetProcessById(pid).WaitForExit(timeout); }
    catch (ArgumentException) { return true; }
}

static bool WaitForMarker(string marker, Process process, TimeSpan timeout)
{
    var timer = Stopwatch.StartNew();
    while (timer.Elapsed < timeout)
    {
        if (File.Exists(marker)) return true;
        if (process.HasExited) return false;
        Thread.Sleep(250);
    }
    return false;
}

static void CopyDirectory(string source, string destination)
{
    if (Directory.Exists(destination)) Directory.Delete(destination, true);
    Directory.CreateDirectory(destination);
    foreach (var entry in Directory.EnumerateFileSystemEntries(source, "*", SearchOption.AllDirectories))
    {
        if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) continue;
        var target = Path.Combine(destination, Path.GetRelativePath(source, entry));
        if (Directory.Exists(entry)) Directory.CreateDirectory(target);
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(entry, target, true);
        }
    }
}

static void SelfCheck()
{
    var root = Path.Combine(Path.GetTempPath(), "DivaUpdaterCheck-" + Guid.NewGuid().ToString("N"));
    try
    {
        var source = Path.Combine(root, "source");
        var destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(Path.Combine(source, "nested"));
        File.WriteAllText(Path.Combine(source, "nested", "check.txt"), "ok");
        CopyDirectory(source, destination);
        if (File.ReadAllText(Path.Combine(destination, "nested", "check.txt")) != "ok" || !IsUnder(Path.Combine(root, "x"), root))
            throw new InvalidOperationException("Updater self-check failed.");
        if (!WaitForExit(int.MaxValue, TimeSpan.Zero))
            throw new InvalidOperationException("Exited-process check failed.");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
}

static void Log(string path, string message)
{
    try { File.AppendAllText(path, $"{DateTime.UtcNow:O} {message}{Environment.NewLine}"); } catch { }
}
