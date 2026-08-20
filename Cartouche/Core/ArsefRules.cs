using System.Text;
using System.Text.RegularExpressions;

namespace AssistantArsef.Core;

public sealed record ArsefOption(string Code, string Label, string Folder, string? DisplayText = null)
{
    public string ChoiceLabel => DisplayText ?? Label;
    public string ShortLabel
    {
        get
        {
            var separator = Label.IndexOf(" - ", StringComparison.Ordinal);
            return separator >= 0 ? Label[(separator + 3)..] : Label;
        }
    }
    public override string ToString() => ChoiceLabel;
}

public sealed record ArsefInput(
    string Title,
    string TypeCode,
    string DomainCode,
    string ServiceCode,
    string DocumentCode,
    string Version,
    string Author,
    DateTime ValidityDate)
{
    public string EmailSubject { get; init; } = string.Empty;
    public string EmailRecipient { get; init; } = string.Empty;
}

public sealed record ArsefPlan(
    string Code,
    string DomainFolder,
    string TypeFolder,
    string ServiceFolder,
    string OutputFolder,
    string DocxPath,
    string PdfPath);

public static class ArsefRules
{
    private static DivaSchema schema = DivaSchema.CreateDefault();

    public static IReadOnlyList<ArsefOption> Types { get; private set; } = [];
    public static IReadOnlyList<ArsefOption> Domains { get; private set; } = [];
    public static IReadOnlyList<ArsefOption> Services { get; private set; } = [];
    public static string RootFolderName => CleanSegment(schema.RootFolderName, "Nom du dossier racine");
    public static string LetterPlace => string.IsNullOrWhiteSpace(schema.LetterPlace) ? "Place" : schema.LetterPlace.Trim();
    public static string ServiceDomainCode => schema.ServiceDomainCode;
    public static string RegisterCodeMarker => schema.RegisterCodeMarker;
    public static string RegisterVersionMarker => schema.RegisterVersionMarker;
    public static string RegisterTitleMarker => schema.RegisterTitleMarker;

    static ArsefRules() => Configure(schema);

    public static void Configure(DivaSchema value)
    {
        schema = value;
        Types = value.Types.Select(ToOption).ToArray();
        Domains = value.Domains.Where(x => !x.Code.Equals("RGPD", StringComparison.OrdinalIgnoreCase)).Select(ToOption).ToArray();
        Services = value.Services.Select(ToOption).ToArray();
    }

    public static string DetectDesktopRoot() => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public static ArsefOption GetType(string code) => Types.First(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
    public static ArsefOption GetDomain(string code) => Domains.First(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
    public static ArsefOption? GetService(string code) => Services.FirstOrDefault(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    public static string CleanSegment(string value, string fieldName)
    {
        var cleaned = Regex.Replace(value.Normalize(NormalizationForm.FormC).Trim(), @"\s+", " ");
        cleaned = string.Concat(cleaned.Where(character => !char.IsControl(character)));
        foreach (var invalid in Path.GetInvalidFileNameChars()) cleaned = cleaned.Replace(invalid, '-');
        cleaned = cleaned.Trim().Trim('.');
        if (cleaned.Length == 0) throw new InvalidOperationException($"{fieldName} est obligatoire.");
        if (Regex.IsMatch(cleaned, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase))
            throw new InvalidOperationException($"{fieldName} utilise un nom réservé par Windows.");
        return cleaned;
    }

    public static ArsefPlan CreatePlan(ArsefInput input, string root)
    {
        var type = GetType(input.TypeCode);
        var domain = GetDomain(input.DomainCode);
        var service = string.IsNullOrWhiteSpace(input.ServiceCode) ? null : GetService(input.ServiceCode);
        var documentCode = CleanSegment(input.DocumentCode, "Mot-clé de codification");
        var version = CleanSegment(input.Version, "Version");
        var code = $"{type.Code}-{domain.Code}-{documentCode}-{version}";
        var parent = Path.Combine(root, domain.Folder, type.Folder);
        if (service is not null) parent = Path.Combine(parent, service.Folder);
        var outputFolder = Path.Combine(parent, code);
        return new(code, domain.Folder, type.Folder, service?.Folder ?? string.Empty, outputFolder,
            Path.Combine(outputFolder, code + ".docx"), Path.Combine(outputFolder, code + ".pdf"));
    }

    public static IReadOnlyList<string> FixedParentFolders(string root)
    {
        var folders = new List<string> { root };
        foreach (var domain in Domains)
        foreach (var type in Types)
        {
            if (type.Code.Equals("RGPD", StringComparison.OrdinalIgnoreCase) &&
                !domain.Code.Equals("QUA", StringComparison.OrdinalIgnoreCase))
                continue;
            var parent = Path.Combine(root, domain.Folder, type.Folder);
            folders.Add(parent);
            if (domain.Code.Equals(ServiceDomainCode, StringComparison.OrdinalIgnoreCase))
                folders.AddRange(Services.Select(service => Path.Combine(parent, service.Folder)));
        }
        return folders.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static void PrepareFixedFolders(string root)
    {
        foreach (var folder in FixedParentFolders(root)) Directory.CreateDirectory(folder);
    }

    public static IReadOnlyList<string> Validate(ArsefInput input, string root)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(input.Title)) errors.Add("Le titre est obligatoire.");
        if (string.IsNullOrWhiteSpace(input.DocumentCode)) errors.Add("Le mot-clé de codification est obligatoire.");
        if (string.IsNullOrWhiteSpace(input.Version)) errors.Add("La version est obligatoire.");
        if (string.IsNullOrWhiteSpace(input.Author)) errors.Add("Le nom de la personne préparatrice est obligatoire.");
        if (input.ValidityDate == default) errors.Add("La date de validité est obligatoire.");
        if (string.IsNullOrWhiteSpace(root)) errors.Add("Le dossier de travail n'est pas configuré.");
        if (!Types.Any(x => x.Code.Equals(input.TypeCode, StringComparison.OrdinalIgnoreCase))) errors.Add("Le type de document est invalide.");
        if (!Domains.Any(x => x.Code.Equals(input.DomainCode, StringComparison.OrdinalIgnoreCase))) errors.Add("Le domaine est invalide.");
        if (input.TypeCode.Equals("RGPD", StringComparison.OrdinalIgnoreCase) &&
            !input.DomainCode.Equals("QUA", StringComparison.OrdinalIgnoreCase))
            errors.Add("Les documents RGPD doivent être classés uniquement dans Qualité et Risques.");
        if (input.DomainCode.Equals(ServiceDomainCode, StringComparison.OrdinalIgnoreCase) && !Services.Any(x => x.Code.Equals(input.ServiceCode, StringComparison.OrdinalIgnoreCase)))
            errors.Add("Le service est obligatoire pour ce domaine.");
        if (!input.DomainCode.Equals(ServiceDomainCode, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(input.ServiceCode))
            errors.Add("Le service ne peut être utilisé que pour le domaine concerné.");
        try
        {
            var plan = CreatePlan(input, root);
            if (plan.DocxPath.Length > 240 || plan.PdfPath.Length > 240)
                errors.Add("Le chemin final est trop long pour Windows. Raccourcissez le mot-clé ou choisissez un dossier plus court.");
        }
        catch (Exception ex) { errors.Add(ex.Message); }
        return errors;
    }

    private static ArsefOption ToOption(DivaOptionDefinition definition) => new(definition.Code, definition.Label, definition.Folder, definition.DisplayText);
}

internal static class ArsefRulesSelfCheck
{
    public static void Run()
    {
        ArsefRules.Configure(DivaSchema.CreateDefault());
        var root = Path.Combine(Path.GetTempPath(), "DivaRulesCheck");
        var input = new ArsefInput(
            "L’objet d’un document",
            "OUT",
            "QUA",
            "",
            "L'objet du document",
            "1",
            "Personne préparatrice",
            new DateTime(2026, 8, 20));
        var plan = ArsefRules.CreatePlan(input, root);
        if (!plan.Code.Equals("OUT-QUA-L'objet du document-1", StringComparison.Ordinal))
            throw new InvalidOperationException("Cartouche apostrophe check failed.");
        if (ArsefRules.FixedParentFolders(root).Any(path =>
                path.Contains($"{Path.DirectorySeparatorChar}RGPD", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains("ARSEF Qualité et Risques", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("RGPD folder scope check failed.");
    }
}
