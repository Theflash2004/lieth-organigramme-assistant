using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiethOrganigrammeAssistant;

internal static class UpdateService
{
    private const string Owner = "Theflash2004";
    private const string Repository = "lieth-organigramme-assistant";
    private const string SetupName = "DivaAssistant-Setup.exe";
    private const string ManifestName = "DivaAssistant-update.json";
    private const string SignatureName = "DivaAssistant-update.json.sig";
    private const long MaximumInstallerBytes = 350L * 1024 * 1024;
    private const string PublicKeyBase64 = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEMPsp/EGsXzr11vJ375kYs1GWPDw8jK44kIfo+U5dq4c0rtxl+8Y/dHrSy0bgBEpGJ6Co7Md2lqoe/2Ow9mPuSw==";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task CheckForUpdateAsync(MainForm owner)
    {
        try
        {
            using var client = CreateClient();
            var releaseBytes = await DownloadBytesAsync(
                client,
                $"https://api.github.com/repos/{Owner}/{Repository}/releases/latest",
                256 * 1024,
                CancellationToken.None);
            var release = JsonSerializer.Deserialize<GitHubRelease>(releaseBytes, JsonOptions);
            if (release is null || !Version.TryParse(release.TagName.TrimStart('v'), out var advertised) || advertised <= CurrentVersion)
                return;

            var manifestAsset = FindAsset(release, ManifestName);
            var signatureAsset = FindAsset(release, SignatureName);
            if (manifestAsset is null || signatureAsset is null) return;
            ValidateGitHubAssetUrl(manifestAsset.DownloadUrl);
            ValidateGitHubAssetUrl(signatureAsset.DownloadUrl);

            var manifestBytes = await DownloadBytesAsync(client, manifestAsset.DownloadUrl, 64 * 1024, CancellationToken.None);
            var signatureText = System.Text.Encoding.ASCII.GetString(
                await DownloadBytesAsync(client, signatureAsset.DownloadUrl, 8 * 1024, CancellationToken.None)).Trim();
            byte[] signature;
            try { signature = Convert.FromBase64String(signatureText); }
            catch (FormatException) { throw new InvalidDataException("La signature de la mise à jour est invalide."); }
            if (!VerifySignature(manifestBytes, signature, Convert.FromBase64String(PublicKeyBase64)))
                throw new InvalidDataException("La mise à jour n’a pas été signée par Diva Assistant.");

            var manifest = JsonSerializer.Deserialize<SignedUpdateManifest>(manifestBytes, JsonOptions)
                ?? throw new InvalidDataException("Le manifeste de mise à jour est illisible.");
            ValidateManifest(manifest, release.TagName, advertised);
            var setupAsset = FindAsset(release, manifest.Asset)
                ?? throw new InvalidDataException("L’installateur signé est absent de la publication.");
            ValidateGitHubAssetUrl(setupAsset.DownloadUrl);

            if (MessageBox.Show(owner,
                    $"La version {advertised} de Diva Assistant est disponible. L’installer maintenant ?",
                    "Mise à jour disponible", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
                return;

            using var progress = new UpdateProgressForm();
            progress.Show(owner);
            progress.SetStatus("Préparation de la mise à jour signée…");
            Application.DoEvents();

            var versionFolder = Path.Combine(AppSettings.RootFolder, "updates", advertised.ToString());
            Directory.CreateDirectory(versionFolder);
            var installer = Path.Combine(versionFolder, SetupName);
            await DownloadFileAsync(client, setupAsset.DownloadUrl, installer, manifest.Length, progress.SetDownloadProgress);

            progress.SetStatus("Vérification cryptographique…");
            await VerifyInstallerAsync(installer, manifest);

            var helper = Path.Combine(AppContext.BaseDirectory, "DivaUpdater.exe");
            if (!File.Exists(helper)) throw new FileNotFoundException("Le programme de mise à jour est introuvable.", helper);
            var helperCopy = Path.Combine(versionFolder, "DivaUpdater.exe");
            File.Copy(helper, helperCopy, true);
            var marker = Path.Combine(versionFolder, "healthy-" + Guid.NewGuid().ToString("N") + ".ok");

            progress.SetStatus("Installation et sauvegarde de la version actuelle…");
            Process.Start(BuildUpdaterStartInfo(
                helperCopy,
                Environment.ProcessId,
                installer,
                Application.ExecutablePath,
                AppContext.BaseDirectory,
                marker));
            owner.ExitForUpdate();
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            // Offline update checks are expected. No telemetry is emitted; a later startup retries.
        }
        catch (Exception error)
        {
            CrashReporter.Write(error);
            MessageBox.Show(owner, error.Message, "Mise à jour impossible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public static void MarkHealthy(string marker)
    {
        var full = Path.GetFullPath(marker);
        var updates = Path.Combine(AppSettings.RootFolder, "updates");
        if (!IsUnder(full, updates)) return;
        AtomicFile.WriteAllText(full, "ok");
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DivaAssistant", CurrentVersion.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static GitHubAsset? FindAsset(GitHubRelease release, string name) =>
        release.Assets.FirstOrDefault(asset => asset.Name.Equals(name, StringComparison.Ordinal));

    private static void ValidateManifest(SignedUpdateManifest manifest, string releaseTag, Version advertised)
    {
        if (!Version.TryParse(manifest.Version, out var signedVersion) || signedVersion != advertised ||
            !releaseTag.Equals("v" + manifest.Version, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("La version signée ne correspond pas à la publication GitHub.");
        if (!manifest.Asset.Equals(SetupName, StringComparison.Ordinal) || manifest.Length is <= 0 or > MaximumInstallerBytes)
            throw new InvalidDataException("Le manifeste de mise à jour contient un installateur invalide.");
        if (manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("L’empreinte signée de la mise à jour est invalide.");
    }

    private static void ValidateGitHubAssetUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith($"/{Owner}/{Repository}/releases/download/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("L’adresse de téléchargement de la mise à jour n’est pas autorisée.");
    }

    private static async Task<byte[]> DownloadBytesAsync(HttpClient client, string url, long maximumBytes, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length > maximumBytes)
            throw new InvalidDataException("La réponse de mise à jour dépasse la taille autorisée.");
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var target = new MemoryStream();
        var buffer = new byte[32 * 1024];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > maximumBytes) throw new InvalidDataException("La réponse de mise à jour dépasse la taille autorisée.");
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return target.ToArray();
    }

    private static async Task DownloadFileAsync(HttpClient client, string url, string destination, long expectedLength, Action<long, long?> progress)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var announced = response.Content.Headers.ContentLength;
        if (announced is > MaximumInstallerBytes || announced is long contentLength && contentLength != expectedLength)
            throw new InvalidDataException("La taille de l’installateur ne correspond pas au manifeste signé.");

        var temporary = destination + ".download";
        try
        {
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var target = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[128 * 1024];
            long downloaded = 0;
            int read;
            while ((read = await source.ReadAsync(buffer)) > 0)
            {
                downloaded += read;
                if (downloaded > MaximumInstallerBytes || downloaded > expectedLength)
                    throw new InvalidDataException("L’installateur téléchargé dépasse la taille signée.");
                await target.WriteAsync(buffer.AsMemory(0, read));
                progress(downloaded, expectedLength);
            }
            await target.FlushAsync();
            if (downloaded != expectedLength)
                throw new InvalidDataException("Le téléchargement de la mise à jour est incomplet.");
            File.Move(temporary, destination, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private static async Task VerifyInstallerAsync(string path, SignedUpdateManifest manifest)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != manifest.Length)
            throw new InvalidDataException("La taille de l’installateur est incorrecte.");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(stream));
        if (!digest.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("L’empreinte de l’installateur ne correspond pas à la signature Diva.");
    }

    private static bool VerifySignature(byte[] data, byte[] signature, byte[] publicKey)
    {
        using var verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(publicKey, out var consumed);
        return consumed == publicKey.Length && verifier.VerifyData(
            data,
            signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    private static ProcessStartInfo BuildUpdaterStartInfo(string helper, int pid, string installer, string app, string installDir, string marker)
    {
        var info = new ProcessStartInfo(helper) { UseShellExecute = true };
        foreach (var argument in new[]
                 {
                     "--pid", pid.ToString(CultureInfo.InvariantCulture),
                     "--installer", installer,
                     "--app", app,
                     "--install-dir", installDir,
                     "--marker", marker
                 })
            info.ArgumentList.Add(argument);
        return info;
    }

    private static bool IsUnder(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static Version CurrentVersion => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);

    internal static void SelfCheck()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var data = System.Text.Encoding.UTF8.GetBytes("Diva update check");
        var signature = key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        if (!VerifySignature(data, signature, key.ExportSubjectPublicKeyInfo()))
            throw new InvalidOperationException("Update signature check failed.");
        data[0] ^= 1;
        if (VerifySignature(data, signature, key.ExportSubjectPublicKeyInfo()))
            throw new InvalidOperationException("Update tamper check failed.");

        var info = BuildUpdaterStartInfo("updater.exe", 42, "setup.exe", "app.exe", @"C:\app", "marker.ok");
        if (info.ArgumentList.Count != 10 || info.ArgumentList[9] != "marker.ok")
            throw new InvalidOperationException("Updater arguments check failed.");
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("assets")] List<GitHubAsset> Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string DownloadUrl);

    private sealed record SignedUpdateManifest(
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("asset")] string Asset,
        [property: JsonPropertyName("sha256")] string Sha256,
        [property: JsonPropertyName("length")] long Length);
}

internal sealed class UpdateProgressForm : Form
{
    private readonly Label message = new() { AutoSize = true, Location = new Point(18, 18), Text = "Préparation de la mise à jour…" };
    private readonly ProgressBar bar = new() { Location = new Point(18, 52), Size = new Size(430, 24), Style = ProgressBarStyle.Marquee };

    public UpdateProgressForm()
    {
        Text = "Mise à jour de Diva Assistant";
        ClientSize = new Size(466, 100);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ControlBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = DivaTheme.UiFont;
        Controls.AddRange([message, bar]);
    }

    public void SetDownloadProgress(long downloaded, long? total)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(() => SetDownloadProgress(downloaded, total))); return; }
        if (total is > 0)
        {
            bar.Style = ProgressBarStyle.Continuous;
            bar.Value = Math.Clamp((int)(downloaded * 100 / total.Value), 0, 100);
            message.Text = $"Téléchargement de la mise à jour… {bar.Value} %";
        }
        else
        {
            bar.Style = ProgressBarStyle.Marquee;
            message.Text = "Téléchargement de la mise à jour…";
        }
    }

    public void SetStatus(string text)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(() => SetStatus(text))); return; }
        bar.Style = ProgressBarStyle.Marquee;
        message.Text = text;
    }
}
