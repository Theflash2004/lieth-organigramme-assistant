using System.Security.Cryptography;
using AssistantArsef.Core;
using LiethOrganigrammeAssistant;

namespace AssistantArsef;

internal sealed record ArsefTemplateModel(
    string Code,
    string Label,
    string FileName,
    ArsefTemplateKind Kind,
    string? DefaultTypeCode = null,
    string? DefaultDomainCode = null);

internal static class TemplateCatalog
{
    private const string ResourcePrefix = "AssistantArsef.Templates.";
    private static string templatesRoot = AppPaths.TemplatesRoot;
    public static IReadOnlyList<ArsefTemplateModel> Models { get; private set; } = [];

    public static void Configure(DivaSchema schema)
    {
        templatesRoot = AppPaths.TemplatesRoot;
        Models = schema.Templates.Select(x => new ArsefTemplateModel(
            x.Code,
            x.Label,
            x.FileName,
            Enum.TryParse<ArsefTemplateKind>(x.Kind, true, out var kind) ? kind : ArsefTemplateKind.Cartouche,
            x.DefaultTypeCode,
            x.DefaultDomainCode)).ToArray();
    }

    public static string Extract(ArsefTemplateModel model)
    {
        ValidateFileName(model.FileName);

        var assembly = typeof(TemplateCatalog).Assembly;
        using var source = assembly.GetManifestResourceStream(ResourcePrefix + model.FileName);
        if (source is null)
        {
            var external = Path.Combine(templatesRoot, model.FileName);
            if (!File.Exists(external))
                throw new FileNotFoundException($"Le modèle '{model.Label}' est absent.", model.FileName);
            if (new FileInfo(external).Length is <= 0 or > 50 * 1024 * 1024)
                throw new InvalidDataException($"Le modèle '{model.Label}' a une taille invalide.");
            return external;
        }
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes))[..12].ToLowerInvariant();
        var path = Path.Combine(templatesRoot, Path.GetFileNameWithoutExtension(model.FileName) + "-" + hash + Path.GetExtension(model.FileName));
        Directory.CreateDirectory(templatesRoot);
        if (!File.Exists(path)) AtomicFile.WriteAllBytes(path, bytes);
        return path;
    }

    // Kept for private template bundles that want content-addressed copies.
    public static string CacheCopy(string sourcePath)
    {
        if (new FileInfo(sourcePath).Length is <= 0 or > 50 * 1024 * 1024)
            throw new InvalidDataException("Le modèle a une taille invalide.");
        var bytes = File.ReadAllBytes(sourcePath);
        var hash = Convert.ToHexString(SHA256.HashData(bytes))[..12].ToLowerInvariant();
        var path = Path.Combine(templatesRoot, Path.GetFileNameWithoutExtension(sourcePath) + "-" + hash + Path.GetExtension(sourcePath));
        Directory.CreateDirectory(templatesRoot);
        if (!File.Exists(path)) AtomicFile.WriteAllBytes(path, bytes);
        return path;
    }

    private static void ValidateFileName(string fileName)
    {
        if (!Path.GetFileName(fileName).Equals(fileName, StringComparison.Ordinal) ||
            Path.GetExtension(fileName) is not (".dotm" or ".dotx" or ".docx"))
            throw new InvalidDataException("Le nom du modèle Word est invalide.");
    }
}
