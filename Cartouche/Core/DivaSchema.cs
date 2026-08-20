using System.Text.Json;

namespace AssistantArsef.Core;

public sealed class DivaSchema
{
    public string RootFolderName { get; set; } = "ARSEF";
    public string LetterPlace { get; set; } = "Roche La Molière";
    public string ServiceDomainCode { get; set; } = "SOI";
    public string RegisterCodeMarker { get; set; } = "REG-QUA-VIOL-1";
    public string RegisterVersionMarker { get; set; } = "Version 1";
    public string RegisterTitleMarker { get; set; } = "REGISTRE DES VIOLATIONS DE DONNÉES PERSONNELLES";
    public List<DivaOptionDefinition> Types { get; set; } = new();
    public List<DivaOptionDefinition> Domains { get; set; } = new();
    public List<DivaOptionDefinition> Services { get; set; } = new();
    public List<DivaTemplateDefinition> Templates { get; set; } = new();

    public static DivaSchema Load(string path)
    {
        if (!File.Exists(path)) return CreateDefault();
        try
        {
            var schema = JsonSerializer.Deserialize<DivaSchema>(File.ReadAllText(path));
            return schema ?? CreateDefault();
        }
        catch
        {
            return CreateDefault();
        }
    }

    public static DivaSchema CreateDefault() => new()
    {
        Types =
        [
            new("MAN", "MAN - Manuel", "MANUEL", "MAN - Manuel (Manuel qualité, chartes, règles générales)"),
            new("PROC", "PROC - Procédure", "PROCEDURES", "PROC - Procédure (Étapes à suivre pour réaliser une activité)"),
            new("PROT", "PROT - Protocole", "PROTOCOLE", "PROT - Protocole (Consignes détaillées, notamment pour les soins)"),
            new("OUT", "OUT - Outil / Guide", "OUTILS", "OUT - Outil / Guide (Support pratique à utiliser au quotidien)"),
            new("ENR", "ENR - Enregistrement", "ENREGISTREMENT", "ENR - Enregistrement (Document à signer ou compléter ; privilégier le modèle email si le document est court)"),
            new("REG", "REG - Registre", "REGISTRE", "REG - Registre (Tableau de suivi à compléter dans le temps)"),
            new("RGPD", "RGPD - Document conformité", "RGPD", "RGPD - Conformité (Document relatif aux données personnelles)")
        ],
        Domains =
        [
            new("QUA", "QUA - Qualité et Risques", "ARSEF Qualité et Risques", "QUA - Qualité et Risques (Réclamations, audits, amélioration continue)"),
            new("RH", "RH - Ressources Humaines", "ARSEF RH", "RH - Ressources Humaines (Personnel, recrutement, formation)"),
            new("SOI", "SOI - Pôle Soins", "ARSEF Pôle soins ( SSIAD, ESA)", "SOI - Pôle Soins (SSIAD, ESA, accompagnement des soins)"),
            new("AID", "AID - Pôle Aide à domicile", "ARSEF Pôle Aide à domicile", "AID - Pôle Aide à domicile (Interventions et accompagnement)"),
            new("DIR", "DIR - Direction", "ARSEF Direction", "DIR - Direction (Décisions, pilotage, correspondances)"),
            new("USA", "USA - Usagers", "ARSEF Usagers", "USA - Usagers (Dossiers et relations avec les usagers)"),
            new("SI", "SI - Système d'Information", "ARSEF Système d'Information", "SI - Système d'information (Logiciels, accès, sécurité)"),
            new("LOG", "LOG - Logistique", "ARSEF Logistique", "LOG - Logistique (Locaux, équipements, achats)")
        ],
        Services =
        [
            new("ESA", "ESA - Équipe Spécialisée Alzheimer", "ESA"),
            new("SSIAD", "SSIAD - Soins Infirmiers à Domicile", "SSIAD")
        ],
        Templates =
        [
            new("ARSEF", "Document ARSEF (cartouche)", "ARSEF.dotm", "Cartouche"),
            new("EMAIL_DIRECTION", "Email - Direction", "Email direction.dotm", "Plain", "ENR", "DIR"),
            new("EMAIL_SAD", "Email - SAD", "SAD Email.dotm", "Plain", "ENR", "AID"),
            new("EMAIL_SSIAD", "Email - SSIAD", "SSIAD EMAIL.dotm", "Plain", "ENR", "SOI"),
            new("REGISTRE", "Registre (tableau vide)", "REG-QUA-VIOL-1.docx", "Register", "REG", "QUA")
        ]
    };
}

public sealed record DivaOptionDefinition(string Code, string Label, string Folder, string? DisplayText = null);

public sealed record DivaTemplateDefinition(
    string Code,
    string Label,
    string FileName,
    string Kind,
    string? DefaultTypeCode = null,
    string? DefaultDomainCode = null);
