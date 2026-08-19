using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LiethOrganigrammeAssistant;

internal sealed record CryptoEnvelope(string Nonce, string Ciphertext, string Tag);

internal sealed record VaultAccount(
    Guid Id,
    string Username,
    string Role,
    bool MustChangePassword,
    string PasswordSalt,
    CryptoEnvelope PasswordWrappedKey,
    CryptoEnvelope MasterWrappedKey,
    CryptoEnvelope? MasterKeyByPassword);

internal sealed record VaultRegistry(int Version, List<VaultAccount> Accounts);
internal sealed record EncryptedVault(int Version, string Nonce, string Ciphertext, string Tag, DateTime SavedAtUtc = default);

internal static class VaultSession
{
    private const int Pbkdf2Iterations = 300_000;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static byte[]? vaultKey;
    private static byte[]? masterKey;

    public static VaultAccount? Account { get; private set; }
    public static string Username => Account?.Username ?? "";
    public static string Role => Account?.Role ?? "";
    public static bool IsDirectrice => masterKey is not null;
    public static string? SharedFolder { get; private set; }
    public static string? LocalFolder { get; private set; }
    public static bool HasSavedLocation => File.Exists(Path.Combine(AppSettings.RootFolder, "vault-location.txt"));
    public static string DefaultSharedFolder
    {
        get
        {
            var saved = Path.Combine(AppSettings.RootFolder, "vault-location.txt");
            if (File.Exists(saved))
            {
                try
                {
                    var value = File.ReadAllText(saved).Trim();
                    if (value.Length > 0) return value;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
            var oneDrive = new[] { "OneDriveCommercial", "OneDrive", "OneDriveConsumer" }
                .Select(Environment.GetEnvironmentVariable)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            return string.IsNullOrWhiteSpace(oneDrive) ? "" : Path.Combine(oneDrive, "Diva Productivite");
        }
    }

    public static bool HasAccounts(string folder) => File.Exists(RegistryPath(folder));

    public static string CreateDirectrice(string folder, string username, string password)
    {
        ValidateUsername(username);
        ValidatePassword(password);
        Directory.CreateDirectory(DataFolder(folder));
        if (HasAccounts(folder)) throw new InvalidOperationException("Un coffre Diva existe déjà dans ce dossier.");

        var accountId = Guid.NewGuid();
        var newMasterKey = RandomNumberGenerator.GetBytes(32);
        var newVaultKey = RandomNumberGenerator.GetBytes(32);
        var salt = RandomNumberGenerator.GetBytes(16);
        var passwordKey = DeriveKey(password, salt);
        var account = new VaultAccount(
            accountId,
            username.Trim(),
            "Directrice",
            false,
            Convert.ToBase64String(salt),
            Encrypt(newVaultKey, passwordKey),
            Encrypt(newVaultKey, newMasterKey),
            Encrypt(newMasterKey, passwordKey));
        StartSession(folder, account, newVaultKey, newMasterKey, loadVault: false);
        MigrateLegacyData();
        SyncToVault();
        if (HasAccounts(folder)) throw new InvalidOperationException("Un coffre Diva vient d’être créé dans ce dossier. Recommencez avec ce coffre.");
        SaveRegistry(folder, new VaultRegistry(1, [account]));
        SaveLocation(folder);
        return Convert.ToBase64String(newMasterKey);
    }

    public static bool Login(string folder, string username, string password)
    {
        var registry = LoadRegistry(folder);
        var account = registry.Accounts.FirstOrDefault(item => string.Equals(item.Username, username.Trim(), StringComparison.OrdinalIgnoreCase));
        if (account is null) return false;
        try
        {
            var passwordKey = DeriveKey(password, Convert.FromBase64String(account.PasswordSalt));
            var userVaultKey = Decrypt(account.PasswordWrappedKey, passwordKey);
            var userMasterKey = account.MasterKeyByPassword is null ? null : Decrypt(account.MasterKeyByPassword, passwordKey);
            StartSession(folder, account, userVaultKey, userMasterKey, loadVault: true);
            return true;
        }
        catch (Exception error) when (error is CryptographicException or FormatException) { return false; }
    }

    public static IReadOnlyList<VaultAccount> ListAccounts()
    {
        EnsureDirectrice();
        return LoadRegistry(SharedFolder!).Accounts.OrderBy(account => account.Username, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static string GetRecoveryKey()
    {
        EnsureDirectrice();
        return Convert.ToBase64String(masterKey!);
    }

    public static void CreateUser(string username, string role, string temporaryPassword)
    {
        EnsureDirectrice();
        ValidateUsername(username);
        if (role is not ("Responsable du secteur SAD" or "IDEC SSIAD"))
            throw new InvalidOperationException("La fonction du compte doit être Responsable du secteur SAD ou IDEC SSIAD.");
        ValidatePassword(temporaryPassword);
        var registry = LoadRegistry(SharedFolder!);
        if (registry.Accounts.Any(account => string.Equals(account.Username, username.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Cet identifiant existe déjà.");

        var userVaultKey = RandomNumberGenerator.GetBytes(32);
        var salt = RandomNumberGenerator.GetBytes(16);
        var passwordKey = DeriveKey(temporaryPassword, salt);
        var account = new VaultAccount(
            Guid.NewGuid(), username.Trim(), role, true, Convert.ToBase64String(salt),
            Encrypt(userVaultKey, passwordKey), Encrypt(userVaultKey, masterKey!), null);
        SaveVault(account.Id, userVaultKey, EmptyArchive());
        registry.Accounts.Add(account);
        SaveRegistry(SharedFolder!, registry);
    }

    public static void ResetPassword(Guid accountId, string temporaryPassword)
    {
        EnsureDirectrice();
        ValidatePassword(temporaryPassword);
        var registry = LoadRegistry(SharedFolder!);
        var index = registry.Accounts.FindIndex(account => account.Id == accountId);
        if (index < 0) throw new InvalidOperationException("Utilisateur introuvable.");
        var account = registry.Accounts[index];
        var userVaultKey = Decrypt(account.MasterWrappedKey, masterKey!);
        var salt = RandomNumberGenerator.GetBytes(16);
        var passwordKey = DeriveKey(temporaryPassword, salt);
        registry.Accounts[index] = account with
        {
            MustChangePassword = true,
            PasswordSalt = Convert.ToBase64String(salt),
            PasswordWrappedKey = Encrypt(userVaultKey, passwordKey),
            MasterKeyByPassword = account.MasterKeyByPassword is null ? null : Encrypt(masterKey!, passwordKey)
        };
        SaveRegistry(SharedFolder!, registry);
    }

    public static void ChangePassword(string newPassword)
    {
        if (Account is null || vaultKey is null || SharedFolder is null) throw new InvalidOperationException("Aucune session Diva.");
        ValidatePassword(newPassword);
        var registry = LoadRegistry(SharedFolder);
        var index = registry.Accounts.FindIndex(item => item.Id == Account.Id);
        if (index < 0) throw new InvalidOperationException("Utilisateur introuvable.");
        var salt = RandomNumberGenerator.GetBytes(16);
        var passwordKey = DeriveKey(newPassword, salt);
        var updated = Account with
        {
            MustChangePassword = false,
            PasswordSalt = Convert.ToBase64String(salt),
            PasswordWrappedKey = Encrypt(vaultKey, passwordKey),
            MasterKeyByPassword = masterKey is null ? null : Encrypt(masterKey, passwordKey)
        };
        registry.Accounts[index] = updated;
        SaveRegistry(SharedFolder, registry);
        Account = updated;
    }

    public static void RecoverDirectrice(string folder, string recoveryKey, string newUsername, string newPassword)
    {
        ValidateUsername(newUsername);
        ValidatePassword(newPassword);
        byte[] recoveredMasterKey;
        try { recoveredMasterKey = Convert.FromBase64String(recoveryKey.Trim()); }
        catch (FormatException) { throw new InvalidOperationException("Clé de récupération invalide."); }
        if (recoveredMasterKey.Length != 32) throw new InvalidOperationException("Clé de récupération invalide.");

        var registry = LoadRegistry(folder);
        if (registry.Accounts.Any(account => string.Equals(account.Username, newUsername.Trim(), StringComparison.OrdinalIgnoreCase)
                                             && account.MasterKeyByPassword is null))
            throw new InvalidOperationException("Cet identifiant existe déjà.");
        var index = registry.Accounts.FindIndex(account => account.MasterKeyByPassword is not null);
        if (index < 0) throw new InvalidOperationException("Compte Directrice introuvable.");
        var directrice = registry.Accounts[index];
        byte[] directriceVaultKey;
        try { directriceVaultKey = Decrypt(directrice.MasterWrappedKey, recoveredMasterKey); }
        catch (CryptographicException) { throw new InvalidOperationException("Clé de récupération invalide."); }
        var salt = RandomNumberGenerator.GetBytes(16);
        var passwordKey = DeriveKey(newPassword, salt);
        var updated = directrice with
        {
            Username = newUsername.Trim(),
            MustChangePassword = false,
            PasswordSalt = Convert.ToBase64String(salt),
            PasswordWrappedKey = Encrypt(directriceVaultKey, passwordKey),
            MasterKeyByPassword = Encrypt(recoveredMasterKey, passwordKey)
        };
        registry.Accounts[index] = updated;
        SaveRegistry(folder, registry);
        StartSession(folder, updated, directriceVaultKey, recoveredMasterKey, loadVault: true);
    }

    public static void SyncToVault()
    {
        if (Account is null || vaultKey is null || LocalFolder is null) return;
        using var content = new MemoryStream();
        using (var archive = new ZipArchive(content, ZipArchiveMode.Create, true))
        {
            if (Directory.Exists(LocalFolder))
            {
                foreach (var file in Directory.EnumerateFiles(LocalFolder, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(LocalFolder, file);
                    var entry = archive.CreateEntry(relative, CompressionLevel.Fastest);
                    using var source = File.OpenRead(file);
                    using var destination = entry.Open();
                    source.CopyTo(destination);
                }
            }
        }
        SaveVault(Account.Id, vaultKey, content.ToArray());
    }

    public static bool TrySyncToVault()
    {
        try { SyncToVault(); return true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    private static void StartSession(string folder, VaultAccount account, byte[] userVaultKey, byte[]? userMasterKey, bool loadVault)
    {
        SharedFolder = Path.GetFullPath(folder);
        Account = account;
        vaultKey = userVaultKey;
        masterKey = userMasterKey;
        LocalFolder = Path.Combine(AppSettings.RootFolder, "profiles", account.Id.ToString("N"));
        Directory.CreateDirectory(LocalFolder);
        var vault = VaultPath(folder, account.Id);
        if (loadVault && !File.Exists(vault))
        {
            if (!Directory.EnumerateFiles(LocalFolder, "*", SearchOption.AllDirectories).Any() || !TrySyncToVault())
                throw new InvalidDataException("Le coffre de cet utilisateur n’est pas encore disponible. Attendez la fin de la synchronisation OneDrive puis réessayez.");
        }
        else if (loadVault && LocalDataIsNewer(vault, LocalFolder))
            TrySyncToVault();
        else if (loadVault)
            RestoreVault(vault, userVaultKey, LocalFolder);
        if (loadVault) SaveLocation(folder);
    }

    private static void MigrateLegacyData()
    {
        if (LocalFolder is null || Directory.EnumerateFileSystemEntries(LocalFolder).Any()) return;
        foreach (var name in new[] { "organigrammes", "diva-productivite" })
        {
            var source = Path.Combine(AppSettings.RootFolder, name);
            if (Directory.Exists(source)) CopyDirectory(source, Path.Combine(LocalFolder, name));
        }
        var settings = Path.Combine(AppSettings.RootFolder, "settings.json");
        if (File.Exists(settings)) File.Copy(settings, Path.Combine(LocalFolder, "settings.json"), true);
    }

    private static void RestoreVault(string path, byte[] key, string destination)
    {
        var encrypted = JsonSerializer.Deserialize<EncryptedVault>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Coffre Diva illisible.");
        var zipBytes = Decrypt(new CryptoEnvelope(encrypted.Nonce, encrypted.Ciphertext, encrypted.Tag), key);
        var temporary = destination + ".restore";
        if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
        Directory.CreateDirectory(temporary);
        using (var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read))
        {
            var root = Path.GetFullPath(temporary) + Path.DirectorySeparatorChar;
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith('/')) continue;
                var target = Path.GetFullPath(Path.Combine(temporary, entry.FullName));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Coffre Diva invalide.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, true);
            }
        }
        var previous = destination + ".previous";
        if (Directory.Exists(previous)) Directory.Delete(previous, true);
        var movedPrevious = false;
        try
        {
            if (Directory.Exists(destination))
            {
                Directory.Move(destination, previous);
                movedPrevious = true;
            }
            Directory.Move(temporary, destination);
        }
        catch
        {
            if (!Directory.Exists(destination) && movedPrevious && Directory.Exists(previous))
                Directory.Move(previous, destination);
            throw;
        }
        try { if (Directory.Exists(previous)) Directory.Delete(previous, true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static bool LocalDataIsNewer(string vault, string localFolder)
    {
        var files = Directory.EnumerateFiles(localFolder, "*", SearchOption.AllDirectories).ToList();
        if (files.Count == 0) return false;
        var encrypted = JsonSerializer.Deserialize<EncryptedVault>(File.ReadAllText(vault), JsonOptions)
            ?? throw new InvalidDataException("Coffre Diva illisible.");
        var cloudTime = encrypted.SavedAtUtc == default ? File.GetLastWriteTimeUtc(vault) : encrypted.SavedAtUtc;
        return files.Max(File.GetLastWriteTimeUtc) > cloudTime.AddSeconds(2);
    }

    private static void SaveVault(Guid accountId, byte[] key, byte[] content)
    {
        var envelope = Encrypt(content, key);
        var payload = new EncryptedVault(2, envelope.Nonce, envelope.Ciphertext, envelope.Tag, DateTime.UtcNow);
        WriteAtomic(VaultPath(SharedFolder!, accountId), JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static byte[] EmptyArchive()
    {
        using var content = new MemoryStream();
        using (new ZipArchive(content, ZipArchiveMode.Create, true)) { }
        return content.ToArray();
    }

    private static CryptoEnvelope Encrypt(byte[] plaintext, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return new CryptoEnvelope(Convert.ToBase64String(nonce), Convert.ToBase64String(ciphertext), Convert.ToBase64String(tag));
    }

    private static byte[] Decrypt(CryptoEnvelope envelope, byte[] key)
    {
        var nonce = Convert.FromBase64String(envelope.Nonce);
        var ciphertext = Convert.FromBase64String(envelope.Ciphertext);
        var tag = Convert.FromBase64String(envelope.Tag);
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, tag.Length);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    private static byte[] DeriveKey(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, 32);

    private static VaultRegistry LoadRegistry(string folder) =>
        JsonSerializer.Deserialize<VaultRegistry>(File.ReadAllText(RegistryPath(folder)), JsonOptions)
        ?? throw new InvalidDataException("Registre Diva illisible.");

    private static void SaveRegistry(string folder, VaultRegistry registry)
    {
        Directory.CreateDirectory(folder);
        WriteAtomic(RegistryPath(folder), JsonSerializer.Serialize(registry, JsonOptions));
    }

    private static void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content, new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }

    private static void ValidateUsername(string username)
    {
        if (username.Trim().Length is < 3 or > 64) throw new InvalidOperationException("L’identifiant doit contenir entre 3 et 64 caractères.");
    }

    private static void ValidatePassword(string password)
    {
        if (password.Length < 14) throw new InvalidOperationException("Le mot de passe doit contenir au moins 14 caractères.");
    }

    private static void EnsureDirectrice()
    {
        if (masterKey is null || SharedFolder is null) throw new InvalidOperationException("Cette action est réservée à la Directrice.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), true);
    }

    private static void SaveLocation(string folder)
    {
        Directory.CreateDirectory(AppSettings.RootFolder);
        File.WriteAllText(Path.Combine(AppSettings.RootFolder, "vault-location.txt"), Path.GetFullPath(folder));
    }

    private static string RegistryPath(string folder) => Path.Combine(folder, "accounts.json");
    private static string DataFolder(string folder) => Path.Combine(folder, "Diva Data");
    private static string VaultPath(string folder, Guid id) => Path.Combine(DataFolder(folder), id.ToString("N") + ".diva");

    internal static void SelfCheck()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var value = Encoding.UTF8.GetBytes("Données Diva");
        var decrypted = Decrypt(Encrypt(value, key), key);
        if (!decrypted.SequenceEqual(value)) throw new InvalidOperationException("Vault encryption check failed.");
        try { Decrypt(Encrypt(value, key), RandomNumberGenerator.GetBytes(32)); throw new InvalidOperationException("Vault authentication check failed."); }
        catch (AuthenticationTagMismatchException) { }
        using var archive = new ZipArchive(new MemoryStream(EmptyArchive()), ZipArchiveMode.Read);
        if (archive.Entries.Count != 0) throw new InvalidOperationException("Empty vault check failed.");

        var checkRoot = Path.Combine(Path.GetTempPath(), "LiethVaultCheck-" + Guid.NewGuid());
        var local = Path.Combine(checkRoot, "local");
        var vault = Path.Combine(checkRoot, "check.diva");
        try
        {
            Directory.CreateDirectory(local);
            File.WriteAllText(Path.Combine(local, "data.txt"), "local");
            var envelope = Encrypt(EmptyArchive(), key);
            File.WriteAllText(vault, JsonSerializer.Serialize(new EncryptedVault(2, envelope.Nonce, envelope.Ciphertext, envelope.Tag, DateTime.UtcNow.AddMinutes(-1)), JsonOptions));
            if (!LocalDataIsNewer(vault, local)) throw new InvalidOperationException("Newer local data check failed.");
            File.WriteAllText(vault, JsonSerializer.Serialize(new EncryptedVault(2, envelope.Nonce, envelope.Ciphertext, envelope.Tag, DateTime.UtcNow.AddMinutes(1)), JsonOptions));
            if (LocalDataIsNewer(vault, local)) throw new InvalidOperationException("Newer cloud data check failed.");
        }
        finally
        {
            if (Directory.Exists(checkRoot)) Directory.Delete(checkRoot, true);
        }
    }
}
