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

            using var progress = new UpdateProgressForm();
            progress.Show(owner);
            var updates = Path.Combine(AppSettings.RootFolder, "updates", latest.ToString());
            Directory.CreateDirectory(updates);
            var installer = Path.Combine(updates, SetupName);
            await DownloadAsync(client, setup.GetProperty("browser_download_url").GetString()!, installer, progress.SetDownloadProgress);
            progress.SetStatus("Vérification de sécurité…");
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
            progress.SetStatus("Installation de la mise à jour…");
            Process.Start(BuildUpdaterStartInfo(helperCopy, Environment.ProcessId, installer, Application.ExecutablePath, AppContext.BaseDirectory));
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

    private static ProcessStartInfo BuildUpdaterStartInfo(string helper, int pid, string installer, string app, string installDir)
    {
        var info = new ProcessStartInfo(helper) { UseShellExecute = true };
        foreach (var argument in new[] { "--pid", pid.ToString(), "--installer", installer, "--app", app, "--install-dir", installDir })
            info.ArgumentList.Add(argument);
        return info;
    }

    internal static void SelfCheck()
    {
        const string installDir = @"C:\Program Files\Lieth Organigramme Assistant\";
        var info = BuildUpdaterStartInfo("updater.exe", 42, "setup.exe", "app.exe", installDir);
        if (info.ArgumentList.Count != 8 || info.ArgumentList[7] != installDir)
            throw new InvalidOperationException("Updater arguments check failed.");
    }

    private static async Task DownloadAsync(HttpClient client, string url, string destination, Action<long, long?> progress)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync();
        await using var target = File.Create(destination);
        var buffer = new byte[81920];
        long downloaded = 0;
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read));
            downloaded += read;
            progress(downloaded, response.Content.Headers.ContentLength);
        }
    }
}

internal sealed class UpdateProgressForm : Form
{
    private readonly Label message = new() { AutoSize = true, Location = new Point(18, 18), Text = "Téléchargement de la mise à jour…" };
    private readonly ProgressBar bar = new() { Location = new Point(18, 50), Size = new Size(380, 24) };

    public UpdateProgressForm()
    {
        Text = "Mise à jour";
        ClientSize = new Size(416, 95);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ControlBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Controls.AddRange([message, bar]);
    }

    public void SetDownloadProgress(long downloaded, long? total)
    {
        if (total is > 0)
        {
            bar.Style = ProgressBarStyle.Continuous;
            bar.Value = Math.Clamp((int)(downloaded * 100 / total.Value), 0, 100);
            message.Text = $"Téléchargement de la mise à jour… {bar.Value}%";
        }
        else
        {
            bar.Style = ProgressBarStyle.Marquee;
            message.Text = "Téléchargement de la mise à jour…";
        }
    }

    public void SetStatus(string text)
    {
        bar.Style = ProgressBarStyle.Marquee;
        message.Text = text;
    }
}
