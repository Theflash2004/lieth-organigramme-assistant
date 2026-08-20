using System.Globalization;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AssistantArsef.Core;

namespace AssistantArsef;

internal enum ArsefTemplateKind
{
    Cartouche,
    Plain,
    Register
}

internal static class WordAutomation
{
    private const int WdFindStop = 0;
    private const int WdLeft = 0;
    private const int WdCenter = 1;
    private const int WdRight = 2;
    private const int WdCellAlignVerticalCenter = 1;
    private const int WdShapeCenter = -999995;
    private const int WdDocx = 12;
    private const int WdPdf = 17;

    public static void CreateFromTemplate(
        string templatePath,
        ArsefInput input,
        ArsefPlan plan,
        ArsefTemplateKind templateKind = ArsefTemplateKind.Cartouche)
    {
        Directory.CreateDirectory(plan.OutputFolder);
        dynamic? word = null;
        dynamic? document = null;
        var completed = false;
        try
        {
            word = StartWord();
            document = AddDocumentFromTemplate(word, templatePath);
            document.CopyStylesFromTemplate(templatePath);
            PrepareDocument(document, input, plan.Code, templateKind);
            SaveDocument(document, plan);
            completed = true;
        }
        finally
        {
            CloseWord(document, word);
            if (!completed) RemoveEmptyOutputFolder(plan.OutputFolder);
        }
    }

    public static void ApplyToExistingFile(string sourcePath, string templatePath, ArsefInput input, ArsefPlan plan)
    {
        Directory.CreateDirectory(plan.OutputFolder);
        var tempPath = Path.Combine(Path.GetTempPath(), "arsef-" + Guid.NewGuid().ToString("N") + Path.GetExtension(sourcePath));
        File.Copy(sourcePath, tempPath, true);

        dynamic? word = null;
        dynamic? template = null;
        dynamic? document = null;
        var completed = false;
        try
        {
            word = StartWord();
            template = AddDocumentFromTemplate(word, templatePath);
            document = word.Documents.Open(tempPath);
            document.CopyStylesFromTemplate(templatePath);
            CopyTemplateHeadersAndFooters(template, document);
            PrepareDocument(document, input, plan.Code, ArsefTemplateKind.Cartouche);
            SaveDocument(document, plan);
            completed = true;
        }
        finally
        {
            CloseDocument(template);
            CloseWord(document, word);
            TryDelete(tempPath);
            if (!completed) RemoveEmptyOutputFolder(plan.OutputFolder);
        }
    }

    public static void UpdatePdf(string docxPath, string pdfPath)
    {
        if (!File.Exists(docxPath) || new FileInfo(docxPath).Length == 0)
            throw new FileNotFoundException("Le document Word est introuvable ou vide.", docxPath);

        Directory.CreateDirectory(Path.GetDirectoryName(pdfPath)!);
        dynamic? word = null;
        dynamic? document = null;
        var temporaryPdf = pdfPath + ".diva-" + Guid.NewGuid().ToString("N") + ".pdf";
        try
        {
            word = StartWord();
            document = word.Documents.Open(docxPath, false, true, false);
            document.ExportAsFixedFormat(temporaryPdf, WdPdf);
            if (!File.Exists(temporaryPdf) || new FileInfo(temporaryPdf).Length == 0)
                throw new InvalidOperationException("Word n’a pas produit de PDF valide.");
            File.Move(temporaryPdf, pdfPath, true);
        }
        catch (InvalidOperationException) when (word is null && TryConvertWithLibreOffice(docxPath, pdfPath))
        {
            return;
        }
        finally
        {
            CloseWord(document, word);
            TryDelete(temporaryPdf);
        }
    }

    private static bool TryConvertWithLibreOffice(string docxPath, string pdfPath)
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LibreOffice", "program", "soffice.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "LibreOffice", "program", "soffice.exe")
        };
        var soffice = candidates.FirstOrDefault(File.Exists);
        if (soffice is null) return false;

        var tempFolder = Path.Combine(Path.GetTempPath(), "arsef-pdf-" + Guid.NewGuid().ToString("N"));
        var profile = Path.Combine(tempFolder, "profile");
        Directory.CreateDirectory(profile);
        try
        {
            var startInfo = new ProcessStartInfo(soffice)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-env:UserInstallation=file:///" + profile.Replace('\\', '/'));
            startInfo.ArgumentList.Add("--headless");
            startInfo.ArgumentList.Add("--convert-to");
            startInfo.ArgumentList.Add("pdf:writer_pdf_Export");
            startInfo.ArgumentList.Add("--outdir");
            startInfo.ArgumentList.Add(tempFolder);
            startInfo.ArgumentList.Add(docxPath);

            using var process = Process.Start(startInfo);
            if (process is null) return false;
            if (!process.WaitForExit(120_000))
            {
                try { process.Kill(true); } catch (InvalidOperationException) { }
                return false;
            }
            if (process.ExitCode != 0) return false;

            var generatedPdf = Path.Combine(tempFolder, Path.GetFileNameWithoutExtension(docxPath) + ".pdf");
            if (!File.Exists(generatedPdf)) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(pdfPath)!);
            if (File.Exists(pdfPath))
            {
                try { File.Replace(generatedPdf, pdfPath, null, true); }
                catch { File.Move(generatedPdf, pdfPath, true); }
            }
            else
            {
                File.Move(generatedPdf, pdfPath);
            }
            return true;
        }
        finally
        {
            try { if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true); } catch { }
        }
    }

    private static dynamic StartWord()
    {
        var type = Type.GetTypeFromProgID("Word.Application")
                   ?? throw new InvalidOperationException("Microsoft Word n'est pas installé.");
        dynamic word = Activator.CreateInstance(type)!;
        word.Visible = false;
        word.DisplayAlerts = 0;
        word.AutomationSecurity = 3;
        word.ScreenUpdating = false;
        try { word.Options.UpdateLinksAtOpen = false; } catch { }
        try { word.Options.SaveNormalPrompt = false; } catch { }
        return word;
    }

    private static dynamic AddDocumentFromTemplate(dynamic word, string templatePath)
    {
        // Explicit Visible:=False keeps the source .dotm from appearing while Word builds the new document.
        return word.Documents.Add(templatePath, false, 0, false);
    }

    private static void CopyTemplateHeadersAndFooters(dynamic template, dynamic document)
    {
        dynamic sourceSection = template.Sections[1];
        for (var sectionIndex = 1; sectionIndex <= document.Sections.Count; sectionIndex++)
        {
            dynamic targetSection = document.Sections[sectionIndex];
            targetSection.PageSetup.DifferentFirstPageHeaderFooter = sourceSection.PageSetup.DifferentFirstPageHeaderFooter;
            targetSection.PageSetup.OddAndEvenPagesHeaderFooter = sourceSection.PageSetup.OddAndEvenPagesHeaderFooter;

            for (var index = 1; index <= 3; index++)
            {
                CopyHeaderFooter(sourceSection.Headers[index], targetSection.Headers[index]);
                CopyHeaderFooter(sourceSection.Footers[index], targetSection.Footers[index]);
            }
        }
    }

    private static void CopyHeaderFooter(dynamic source, dynamic target)
    {
        target.LinkToPrevious = false;
        target.Range.FormattedText = source.Range.FormattedText;
    }

    private static void PrepareDocument(dynamic document, ArsefInput input, string code, ArsefTemplateKind templateKind)
    {
        switch (templateKind)
        {
            case ArsefTemplateKind.Cartouche:
                InsertTitle(document, input.Title);
                ReplaceTokens(document, input, code);
                NormalizeCartoucheLayout(document, code);
                break;
            case ArsefTemplateKind.Register:
                ReplaceInAllStories(document, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [ArsefRules.RegisterCodeMarker] = code,
                    [ArsefRules.RegisterVersionMarker] = "Version " + input.Version,
                    [ArsefRules.RegisterTitleMarker] = input.Title.Trim()
                });
                ClearRegisterTables(document);
                break;
            case ArsefTemplateKind.Plain:
                PrepareEmailDocument(document, input);
                break;
        }
    }

    private static void PrepareEmailDocument(dynamic document, ArsefInput input)
    {
        var subject = string.IsNullOrWhiteSpace(input.EmailSubject) ? input.Title.Trim() : input.EmailSubject.Trim();
        var recipient = string.IsNullOrWhiteSpace(input.EmailRecipient) ? "Madame, Monsieur" : input.EmailRecipient.Trim();
        ReplaceInAllStories(document, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["[OBJET]"] = subject,
            ["[DESTINATAIRE]"] = recipient
        });

        var bodyText = ((string)document.Content.Text).Replace("\r", string.Empty).Replace("\a", string.Empty).Trim();
        if (bodyText.Length == 0)
        {
            BuildLetterBody(document, recipient, subject);
            return;
        }

        dynamic? recipientParagraph = null;
        dynamic? subjectParagraph = null;
        dynamic? greetingParagraph = null;
        dynamic paragraphs = document.Content.Paragraphs;
        for (var index = 1; index <= paragraphs.Count; index++)
        {
            dynamic paragraph = paragraphs[index];
            var text = ((string)paragraph.Range.Text).Trim();
            if (text.StartsWith("Destinataire", StringComparison.OrdinalIgnoreCase)) recipientParagraph = paragraph;
            if (text.StartsWith("Objet", StringComparison.OrdinalIgnoreCase)) subjectParagraph = paragraph;
            if (greetingParagraph is null && (text.StartsWith("Madame", StringComparison.OrdinalIgnoreCase) || text.StartsWith("Bonjour", StringComparison.OrdinalIgnoreCase)))
                greetingParagraph = paragraph;
        }

        if (recipientParagraph is null && subjectParagraph is null && paragraphs.Count > 0)
        {
            InsertBefore(document, paragraphs[1], "Destinataire : " + recipient + "\rObjet : " + subject);
            EnsureLetterDate(document);
            return;
        }

        if (recipientParagraph is not null)
            ReplaceParagraphText(document, recipientParagraph, "Destinataire : " + recipient);

        if (subjectParagraph is not null)
            ReplaceParagraphText(document, subjectParagraph, "Objet : " + subject);

        if (recipientParagraph is null)
        {
            var anchor = greetingParagraph ?? subjectParagraph ?? (paragraphs.Count > 0 ? paragraphs[1] : null);
            if (anchor is not null)
                InsertBefore(document, anchor, "Destinataire : " + recipient);
        }

        if (subjectParagraph is null)
        {
            var anchor = greetingParagraph ?? (paragraphs.Count > 0 ? paragraphs[1] : null);
            if (anchor is not null)
                InsertBefore(document, anchor, "Objet : " + subject);
        }

        EnsureLetterDate(document);
    }

    private static void BuildLetterBody(dynamic document, string recipient, string subject)
    {
        var date = DateTime.Today.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("fr-FR"));
        document.Content.Text =
            $"{ArsefRules.LetterPlace}, le {date}\r\r" +
            $"Destinataire : {recipient}\r\r" +
            $"Objet : {subject}\r\r" +
            "Madame, Monsieur,\r";

        dynamic paragraphs = document.Content.Paragraphs;
        if (paragraphs.Count >= 1)
            paragraphs[1].Alignment = WdRight;
        if (paragraphs.Count >= 5)
            paragraphs[5].Range.Font.Bold = 1;
    }

    private static void EnsureLetterDate(dynamic document)
    {
        var date = ArsefRules.LetterPlace + ", le " + DateTime.Today.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("fr-FR"));
        dynamic paragraphs = document.Content.Paragraphs;
        dynamic? dateParagraph = null;
        for (var index = 1; index <= paragraphs.Count; index++)
        {
            dynamic paragraph = paragraphs[index];
            var text = ((string)paragraph.Range.Text).Trim();
            if (text.StartsWith(ArsefRules.LetterPlace, StringComparison.OrdinalIgnoreCase) || text.StartsWith("Fait", StringComparison.OrdinalIgnoreCase))
            {
                dateParagraph = paragraph;
                break;
            }
        }

        if (dateParagraph is not null)
        {
            ReplaceParagraphText(document, dateParagraph, date);
            dateParagraph.Alignment = WdRight;
            return;
        }

        if (paragraphs.Count > 0)
        {
            InsertBefore(document, paragraphs[1], date);
            document.Content.Paragraphs[1].Alignment = WdRight;
        }
    }

    private static void ReplaceParagraphText(dynamic document, dynamic paragraph, string text)
    {
        dynamic range = paragraph.Range.Duplicate;
        range.End = range.End - 1;
        var start = (int)range.Start;
        var fontName = (string)range.Font.Name;
        var fontSize = (float)range.Font.Size;
        var fontColor = (int)range.Font.Color;
        var bold = (int)range.Font.Bold;
        range.Text = text;
        dynamic formatted = document.Range(start, start + text.Length);
        formatted.Font.Name = fontName;
        formatted.Font.Size = fontSize;
        formatted.Font.Color = fontColor;
        formatted.Font.Bold = bold;
    }

    private static void InsertBefore(dynamic document, dynamic paragraph, string text)
    {
        dynamic source = paragraph.Range.Duplicate;
        source.End = source.End - 1;
        var fontName = (string)source.Font.Name;
        var fontSize = (float)source.Font.Size;
        var fontColor = (int)source.Font.Color;
        var bold = (int)source.Font.Bold;
        var start = (int)paragraph.Range.Start;
        paragraph.Range.InsertBefore(text + "\r");
        dynamic inserted = document.Range(start, start + text.Length);
        inserted.Font.Name = fontName;
        inserted.Font.Size = fontSize;
        inserted.Font.Color = fontColor;
        inserted.Font.Bold = bold;
    }

    private static void ReplaceTokens(dynamic document, ArsefInput input, string code)
    {
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["@@NUMERO@@"] = code,
            ["@@numero@@"] = code,
            ["[ID]"] = code,
            ["[VERSION]"] = input.Version,
            ["[DOMAINE]"] = ArsefRules.GetDomain(input.DomainCode).ShortLabel,
            ["[DATE]"] = input.ValidityDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            ["[PREPARE_PAR]"] = input.Author
        };

        ReplaceInAllStories(document, replacements);
        FormatLongCode(document, code);
    }

    private static void ReplaceInAllStories(dynamic document, IReadOnlyDictionary<string, string> replacements)
    {
        ReplaceInRange(document.Content, replacements);
        for (var sectionIndex = 1; sectionIndex <= document.Sections.Count; sectionIndex++)
        {
            dynamic section = document.Sections[sectionIndex];
            for (var index = 1; index <= 3; index++)
            {
                dynamic header = section.Headers[index];
                dynamic footer = section.Footers[index];
                if (header.Exists) ReplaceInRange(header.Range, replacements);
                if (footer.Exists) ReplaceInRange(footer.Range, replacements);
            }
        }
    }

    private static void FormatLongCode(dynamic document, string code)
    {
        var size = code.Length > 24 ? 8 : 10;
        for (var sectionIndex = 1; sectionIndex <= document.Sections.Count; sectionIndex++)
        {
            dynamic section = document.Sections[sectionIndex];
            for (var index = 1; index <= 3; index++)
            {
                dynamic header = section.Headers[index];
                dynamic footer = section.Footers[index];
                if (header.Exists) FormatLongCodeInRange(header.Range, code, size);
                if (footer.Exists) FormatLongCodeInRange(footer.Range, code, size);
            }
        }
    }

    private static void FormatLongCodeInRange(dynamic range, string code, int size)
    {
        dynamic paragraphs = range.Paragraphs;
        for (var index = 1; index <= paragraphs.Count; index++)
        {
            dynamic paragraph = paragraphs[index];
            string text = paragraph.Range.Text;
            if (text.Contains(code, StringComparison.OrdinalIgnoreCase))
                paragraph.Range.Font.Size = size;
        }
    }

    private static void ReplaceInRange(dynamic range, IReadOnlyDictionary<string, string> replacements)
    {
        foreach (var pair in replacements)
        {
            dynamic search = range.Duplicate;
            try
            {
                var boundary = (int)search.End;
                while ((int)search.Start < boundary)
                {
                    dynamic find = search.Find;
                    try
                    {
                        find.ClearFormatting();
                        find.Text = pair.Key;
                        find.Forward = true;
                        find.Wrap = WdFindStop;
                        find.Format = false;
                        find.MatchCase = false;
                        find.MatchWholeWord = false;
                        if (!(bool)find.Execute()) break;
                    }
                    finally { Release(find); }

                    var start = (int)search.Start;
                    var previousLength = (int)search.End - start;
                    search.Text = pair.Value;
                    boundary += pair.Value.Length - previousLength;
                    search.SetRange(start + pair.Value.Length, boundary);
                }
            }
            finally { Release(search); }
        }
    }

    private static void InsertTitle(dynamic document, string title)
    {
        for (var sectionIndex = 1; sectionIndex <= document.Sections.Count; sectionIndex++)
        {
            dynamic section = document.Sections[sectionIndex];
            for (var headerIndex = 1; headerIndex <= 3; headerIndex++)
            {
                dynamic header = section.Headers[headerIndex];
                if (!header.Exists) continue;

                dynamic tables = header.Range.Tables;
                for (var tableIndex = 1; tableIndex <= tables.Count; tableIndex++)
                {
                    dynamic table = tables[tableIndex];
                    if (table.Rows.Count < 5 || table.Columns.Count < 3) continue;
                    if (!((string)table.Range.Text).Contains("@@NUMERO@@", StringComparison.OrdinalIgnoreCase)) continue;

                    dynamic cell = table.Cell(1, 2);
                    dynamic content = cell.Range.Duplicate;
                    content.End = content.End - 1;
                    content.Text = title.Trim();
                    content.ParagraphFormat.Alignment = WdCenter;
                    cell.VerticalAlignment = WdCellAlignVerticalCenter;
                    return;
                }
            }
        }

        throw new InvalidOperationException("Le modèle de cartouche ne contient pas la case centrale prévue pour le titre.");
    }

    private static void ClearRegisterTables(dynamic document)
    {
        for (var tableIndex = 1; tableIndex <= document.Tables.Count; tableIndex++)
        {
            dynamic table = document.Tables[tableIndex];
            for (var rowIndex = 1; rowIndex <= table.Rows.Count; rowIndex++)
            {
                for (var columnIndex = 1; columnIndex <= table.Columns.Count; columnIndex++)
                {
                    dynamic content = table.Cell(rowIndex, columnIndex).Range.Duplicate;
                    content.End = content.End - 1;
                    content.Text = string.Empty;
                }
            }
        }
    }

    private static void NormalizeCartoucheLayout(dynamic document, string code)
    {
        for (var sectionIndex = 1; sectionIndex <= document.Sections.Count; sectionIndex++)
        {
            dynamic section = document.Sections[sectionIndex];
            for (var headerIndex = 1; headerIndex <= 3; headerIndex++)
            {
                dynamic header = section.Headers[headerIndex];
                if (!header.Exists) continue;

                dynamic tables = header.Range.Tables;
                for (var tableIndex = 1; tableIndex <= tables.Count; tableIndex++)
                {
                    dynamic table = tables[tableIndex];
                    if (table.Rows.Count < 5 || table.Columns.Count < 3) continue;
                    if (!((string)table.Range.Text).Contains(code, StringComparison.OrdinalIgnoreCase)) continue;

                    dynamic codeCell = table.Cell(1, 3);
                    dynamic codeFormat = codeCell.Range.ParagraphFormat;
                    codeFormat.Alignment = WdLeft;
                    codeFormat.LeftIndent = 0;
                    codeFormat.FirstLineIndent = 0;
                    codeFormat.RightIndent = 0;
                    codeFormat.SpaceBefore = 0;
                    codeFormat.SpaceAfter = 0;
                    codeFormat.TabStops.ClearAll();

                    dynamic logoCell = table.Cell(1, 1);
                    logoCell.VerticalAlignment = WdCellAlignVerticalCenter;
                    dynamic logoFormat = logoCell.Range.ParagraphFormat;
                    logoFormat.Alignment = WdCenter;
                    logoFormat.LeftIndent = 0;
                    logoFormat.FirstLineIndent = 0;
                    logoFormat.RightIndent = 0;
                    logoFormat.SpaceBefore = 0;
                    logoFormat.SpaceAfter = 0;
                    for (var inlineIndex = 1; inlineIndex <= logoCell.Range.InlineShapes.Count; inlineIndex++)
                        logoCell.Range.InlineShapes[inlineIndex].Range.ParagraphFormat.Alignment = WdCenter;
                    for (var shapeIndex = 1; shapeIndex <= header.Shapes.Count; shapeIndex++)
                    {
                        dynamic shape = header.Shapes[shapeIndex];
                        dynamic anchor = shape.Anchor;
                        if (anchor.Start < logoCell.Range.Start || anchor.Start > logoCell.Range.End) continue;
                        shape.Left = WdShapeCenter;
                        shape.Top = WdShapeCenter;
                    }

                    return;
                }
            }
        }
    }

    private static void SaveDocument(dynamic document, ArsefPlan plan)
    {
        document.SaveAs2(plan.DocxPath, WdDocx);
        document.Save();
        if (!File.Exists(plan.DocxPath) || new FileInfo(plan.DocxPath).Length == 0)
            throw new InvalidOperationException("Word n’a pas enregistré le document généré.");
    }

    private static void CloseDocument(dynamic? document)
    {
        if (document is null) return;
        try { document.Close(false); } catch { }
        Release(document);
    }

    private static void CloseWord(dynamic? document, dynamic? word)
    {
        CloseDocument(document);
        if (word is not null)
        {
            try
            {
                while (word.Documents.Count > 0)
                {
                    dynamic openDocument = word.Documents[1];
                    openDocument.Close(false);
                    Release(openDocument);
                }
            }
            catch { }
            try { word.Quit(); } catch { }
            Release(word);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    private static void Release(object value)
    {
        try
        {
            if (Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
        }
        catch { }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void RemoveEmptyOutputFolder(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                Directory.Delete(path);
        }
        catch { }
    }
}
