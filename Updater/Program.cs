using System.Diagnostics;

if (args.SequenceEqual(["--self-check"], StringComparer.OrdinalIgnoreCase))
{
    SelfCheck();
    return;
}

var argsMap = args.Chunk(2).ToDictionary(pair => pair[0], pair => pair.Length > 1 ? pair[1] : "", StringComparer.OrdinalIgnoreCase);
if (!int.TryParse(argsMap.GetValueOrDefault("--pid"), out var pid) || string.IsNullOrWhiteSpace(argsMap.GetValueOrDefault("--installer")) || string.IsNullOrWhiteSpace(argsMap.GetValueOrDefault("--app")) || string.IsNullOrWhiteSpace(argsMap.GetValueOrDefault("--install-dir")))
    return;

try { Process.GetProcessById(pid).WaitForExit(); } catch { }
var installer = argsMap["--installer"];
var app = argsMap["--app"];
var installDir = argsMap["--install-dir"];
var backup = Path.Combine(Path.GetDirectoryName(installer)!, "backup");

try
{
    // ponytail: back up only the per-user app folder; settings live elsewhere and are never touched.
    CopyDirectory(installDir, backup);
}
catch
{
    return;
}

var setup = Process.Start(new ProcessStartInfo(installer, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = true });
setup?.WaitForExit();
if (setup?.ExitCode == 0 && File.Exists(app))
{
    Directory.Delete(backup, true);
    Process.Start(new ProcessStartInfo(app) { UseShellExecute = true });
    return;
}

try
{
    if (Directory.Exists(installDir)) Directory.Delete(installDir, true);
    CopyDirectory(backup, installDir);
    if (File.Exists(app)) Process.Start(new ProcessStartInfo(app) { UseShellExecute = true });
}
catch { }

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
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
