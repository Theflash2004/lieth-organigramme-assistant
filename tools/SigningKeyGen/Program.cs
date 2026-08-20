using System.Security.Cryptography;
using System.Text;

if (args is ["--verify", var publicKeyBase64, var signedPath, var signaturePath])
{
    using var key = ECDsa.Create();
    key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
    var valid = key.VerifyData(
        await File.ReadAllBytesAsync(signedPath),
        Convert.FromBase64String((await File.ReadAllTextAsync(signaturePath)).Trim()),
        HashAlgorithmName.SHA256,
        DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    return valid ? 0 : 1;
}

if (args is ["--sign", var keyPath, var inputPath, var outputPath])
{
    using var key = ECDsa.Create();
    key.ImportFromPem(await File.ReadAllTextAsync(keyPath));
    var signature = key.SignData(
        await File.ReadAllBytesAsync(inputPath),
        HashAlgorithmName.SHA256,
        DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    if (!key.VerifyData(
            await File.ReadAllBytesAsync(inputPath),
            signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        throw new CryptographicException("Signature verification failed.");
    await File.WriteAllTextAsync(outputPath, Convert.ToBase64String(signature), new UTF8Encoding(false));
    return 0;
}

if (args is not [var destination]) return 2;
var path = Path.GetFullPath(destination);
if (File.Exists(path)) return 3;
Directory.CreateDirectory(Path.GetDirectoryName(path)!);
using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
{
    await File.WriteAllTextAsync(path, PemEncoding.WriteString("PRIVATE KEY", key.ExportPkcs8PrivateKey()), new UTF8Encoding(false));
    Console.WriteLine(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
}
return 0;
