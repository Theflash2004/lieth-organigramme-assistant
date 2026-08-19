using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace LiethOrganigrammeAssistant;

internal static class UpdateService
{
    private const string Owner = "Theflash2004";
    private const string Repository = "lieth-organigramme-assistant";
    private const string SetupName = "LiethOrganigrammeAssistant-Setup.exe";

    public static async Task CheckForUpdateAsync(Form owner)
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LiethOrganigrammeAssistant", "1.0"));
            var json = await client.GetStringAsync($"https://api.github.com/repos/{Owner}/{Repository}/releases/latest");
            using var document = JsonDocument.Parse(json);
            var release = document.RootElement;
            var tag = release.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "0.0.0";
            if (!Version.TryParse(tag, out var latest) || latest <= CurrentVersion()) return;

            var assets = release.GetProperty("assets").EnumerateArray().ToList();
            var setup = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == SetupName);
            var checksum = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == SetupName + ".sha256");
            if (setup.ValueKind == JsonValueKind.Undefined || checksum.ValueKind == JsonValueKind.Undefined) return;

            var answer = MessageBox.Show(owner, $"La version {latest} est disponible. Installer la mise à jour maintenant ?", "Mise à jour disponible", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer != DialogResult.Yes) return;

            var updates = Path.Combine(AppSettings.Folder, "updates", latest.ToString());
            Directory.CreateDirectory(updates);
            var installer = Path.Combine(updates, SetupName);
            await DownloadAsync(client, setup.GetProperty("browser_download_url").GetString()!, installer);
            var expected = (await client.GetStringAsync(checksum.GetProperty("browser_download_url").GetString()!)).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            await using var installerStream = File.OpenRead(installer);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(installerStream)).ToLowerInvariant();
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(installer);
                throw new InvalidOperationException("La vérification de sécurité de la mise à jour a échoué.");
            }

            var helper = Path.Combine(AppContext.BaseDirectory, "LiethUpdater.exe");
            if (!File.Exists(helper)) throw new FileNotFoundException("Programme de mise à jour introuvable.", helper);
            var helperCopy = Path.Combine(updates, "LiethUpdater.exe");
            File.Copy(helper, helperCopy, true);
            Process.Start(new ProcessStartInfo(helperCopy, $"--pid {Environment.ProcessId} --installer \"{installer}\" --app \"{Application.ExecutablePath}\" --install-dir \"{AppContext.BaseDirectory}\"") { UseShellExecute = true });
            Application.Exit();
        }
        catch (HttpRequestException)
        {
            // No telemetry and no noisy offline error: a later startup retries.
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, "Mise à jour impossible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static Version CurrentVersion() => typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0);

    private static async Task DownloadAsync(HttpClient client, string url, string destination)
    {
        await using var source = await client.GetStreamAsync(url);
        await using var target = File.Create(destination);
        await source.CopyToAsync(target);
    }
}
