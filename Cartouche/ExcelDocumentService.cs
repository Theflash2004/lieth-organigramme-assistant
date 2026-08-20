using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using AssistantArsef.Core;
using LiethOrganigrammeAssistant;

namespace AssistantArsef;

internal sealed record ExcelInspection(
    string WorkbookPath,
    string SheetName,
    int HeaderRow,
    IReadOnlyDictionary<string, int> Columns,
    IReadOnlyList<string> ClasserOptions,
    int LastUsedColumn);

internal sealed record ExcelAppendResult(bool Added, bool AlreadyExists, string Message);

internal static class ExcelDocumentService
{
    private static readonly string[] RequiredColumns = ["Document", "Codification", "Domain", "Version", "Date", "Classer"];
    private static readonly CultureInfo FrenchCulture = CultureInfo.GetCultureInfo("fr-FR");

    public static ExcelInspection Inspect(string workbookPath)
    {
        workbookPath = ValidateWorkbookPath(workbookPath);
        dynamic? excel = null;
        dynamic? workbook = null;
        try
        {
            excel = StartExcel();
            workbook = excel.Workbooks.Open(workbookPath, 0, true);
            ExcelInspection? inspection = InspectWorkbook(workbook, workbookPath);
            return inspection ?? throw new InvalidOperationException(
                "Le classeur ne contient pas une feuille de registre exploitable. " +
                "Il faut au minimum les colonnes Document enregistré, Codification, Domaine, Version, Date et Lieu de classement.");
        }
        finally
        {
            CloseExcel(workbook, excel, false);
        }
    }

    public static ExcelInspection Prepare(string workbookPath)
    {
        return ExecuteTransactional(workbookPath, (workbook, stagedPath) =>
        {
            ExcelInspection initial = InspectWorkbook(workbook, stagedPath)
                ?? throw new InvalidOperationException("Le classeur ne contient pas les colonnes nécessaires au registre.");
            return new TransactionOutcome<ExcelInspection>(PrepareRegistry(workbook, initial), true);
        });
    }

    public static ExcelAppendResult Append(
        string workbookPath,
        ArsefInput input,
        ArsefPlan plan,
        IReadOnlyList<string> classers,
        string documentPath)
    {
        return ExecuteTransactional(workbookPath, (workbook, stagedPath) =>
        {
            ExcelInspection initial = InspectWorkbook(workbook, stagedPath)
                ?? throw new InvalidOperationException("Le classeur ne contient pas les colonnes nécessaires au registre.");

            var allowed = new HashSet<string>(initial.ClasserOptions, StringComparer.OrdinalIgnoreCase);
            var selected = classers.Where(x => allowed.Contains(x.Trim())).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (selected.Length == 0)
                throw new InvalidOperationException("Sélectionnez au moins un lieu de classement existant dans la colonne « Lieu de classement ».");

            ExcelInspection inspection = PrepareRegistry(workbook, initial);
            dynamic sheet = workbook.Worksheets[inspection.SheetName];
            try
            {
                if (ContainsCode(sheet, inspection, plan.Code))
                    return new TransactionOutcome<ExcelAppendResult>(
                        new ExcelAppendResult(false, true, "Cette codification existe déjà dans le registre : aucune ligne en double n'a été ajoutée."),
                        false);

                var row = LastDataRow(sheet, inspection) + 1;
                CopyPreviousRowFormatting(sheet, row, inspection);
                Set(sheet, row, inspection.Columns["Codification"], plan.Code);
                SetHyperlink(sheet, row, inspection.Columns["Document"], input.Title.Trim(), documentPath, workbookPath);
                Set(sheet, row, inspection.Columns["Domain"], ArsefRules.GetDomain(input.DomainCode).ShortLabel);
                Set(sheet, row, inspection.Columns["Version"], FormatVersion(input.Version));
                SetDate(sheet, row, inspection.Columns["Date"], input.ValidityDate);
                SetDate(sheet, row, inspection.Columns["ReviewDate"], input.ValidityDate.AddYears(1));
                Set(sheet, row, inspection.Columns["Classer"], string.Join(" ; ", selected));

                if (inspection.Columns.TryGetValue("Type", out var typeColumn))
                    Set(sheet, row, typeColumn, ArsefRules.GetType(input.TypeCode).ShortLabel);
                if (inspection.Columns.TryGetValue("Author", out var authorColumn))
                    Set(sheet, row, authorColumn, input.Author.Trim());
                if (inspection.Columns.TryGetValue("Number", out var numberColumn))
                    Set(sheet, row, numberColumn, NextNumber(sheet, inspection));

                return new TransactionOutcome<ExcelAppendResult>(
                    new ExcelAppendResult(true, false, "Le document a été ajouté à la suite du registre et daté."),
                    true);
            }
            finally
            {
                Release(sheet);
            }
        });
    }

    private static T ExecuteTransactional<T>(string workbookPath, Func<dynamic, string, TransactionOutcome<T>> action)
    {
        var fullPath = ValidateWorkbookPath(workbookPath);
        var lockRoot = Path.Combine(AppSettings.RootFolder, "locks");
        Directory.CreateDirectory(lockRoot);
        var lockName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath)))[..24] + ".registry.lock";
        using var processLock = AcquireLock(Path.Combine(lockRoot, lockName));

        var directory = Path.GetDirectoryName(fullPath)!;
        var stagedPath = Path.Combine(directory, ".diva-transaction-" + Guid.NewGuid().ToString("N") + Path.GetExtension(fullPath));
        byte[] originalHash;
        FileStream? sourceReservation = null;
        dynamic? excel = null;
        dynamic? workbook = null;
        try
        {
            try
            {
                sourceReservation = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (IOException error)
            {
                throw new InvalidOperationException(
                    "Le registre est ouvert ou en cours de synchronisation. Fermez-le dans Excel, attendez OneDrive, puis réessayez.",
                    error);
            }

            originalHash = ComputeHash(fullPath);
            File.Copy(fullPath, stagedPath, false);
            try { File.SetAttributes(stagedPath, FileAttributes.Hidden | FileAttributes.Temporary); } catch (IOException) { }

            excel = StartExcel();
            workbook = excel.Workbooks.Open(stagedPath, 0, false);
            if ((bool)workbook.ReadOnly)
                throw new InvalidOperationException("La copie de travail du registre s’est ouverte en lecture seule.");

            var outcome = action(workbook, stagedPath);
            if (!outcome.Changed) return outcome.Value;
            workbook.Save();
            CloseExcel(workbook, excel, false);
            workbook = null;
            excel = null;

            var stagedHash = ComputeHash(stagedPath);
            sourceReservation.Dispose();
            sourceReservation = null;

            if (!CryptographicOperations.FixedTimeEquals(originalHash, ComputeHash(fullPath)))
                throw new InvalidOperationException(
                    "Le registre a changé pendant l’opération. Diva n’a rien remplacé : relancez l’ajout pour conserver les modifications de l’autre personne.");

            var backup = CreateBackup(fullPath);
            try
            {
                File.Replace(stagedPath, fullPath, null, true);
            }
            catch (PlatformNotSupportedException)
            {
                File.Move(stagedPath, fullPath, true);
            }

            if (!CryptographicOperations.FixedTimeEquals(stagedHash, ComputeHash(fullPath)))
            {
                File.Copy(backup, fullPath, true);
                throw new IOException("La vérification finale du registre a échoué ; la sauvegarde précédente a été restaurée.");
            }

            return outcome.Value;
        }
        finally
        {
            sourceReservation?.Dispose();
            CloseExcel(workbook, excel, false);
            try { if (File.Exists(stagedPath)) File.Delete(stagedPath); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private static FileStream AcquireLock(string path)
    {
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error)
        {
            throw new InvalidOperationException("Une autre opération de registre est déjà en cours sur ce PC.", error);
        }
    }

    private static string ValidateWorkbookPath(string workbookPath)
    {
        var full = Path.GetFullPath(workbookPath);
        if (!File.Exists(full)) throw new FileNotFoundException("Le classeur sélectionné est introuvable.", full);
        if (!Path.GetExtension(full).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Pour votre sécurité, le registre doit être un classeur Excel .xlsx sans macros.");
        if (new FileInfo(full).Length is <= 0 or > 250L * 1024 * 1024)
            throw new InvalidOperationException("Le classeur est vide ou dépasse 250 Mo.");
        return full;
    }

    private static byte[] ComputeHash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 128 * 1024, FileOptions.SequentialScan);
        return SHA256.HashData(stream);
    }

    private static string CreateBackup(string workbookPath)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(workbookPath)))[..20];
        var folder = Path.Combine(AppSettings.Folder, "registry-backups", key);
        Directory.CreateDirectory(folder);
        var backup = Path.Combine(folder, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + Path.GetExtension(workbookPath));
        File.Copy(workbookPath, backup, false);
        foreach (var old in Directory.EnumerateFiles(folder).OrderByDescending(File.GetCreationTimeUtc).Skip(20))
        {
            try { File.Delete(old); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        return backup;
    }

    private sealed record TransactionOutcome<T>(T Value, bool Changed);

    private static ExcelInspection PrepareRegistry(dynamic workbook, ExcelInspection initial)
    {
        // ponytail: preserve the established Word-register order; new documents append without resorting the whole register.
        dynamic sheet = workbook.Worksheets[initial.SheetName];
        try { RemoveEmptyRows(sheet, initial); }
        finally { Release(sheet); }

        var inspection = InspectWorkbook(workbook, initial.WorkbookPath)
            ?? throw new InvalidOperationException("La feuille de registre n'est plus lisible après son nettoyage.");
        sheet = workbook.Worksheets[inspection.SheetName];
        try
        {
            if (!inspection.Columns.ContainsKey("ReviewDate"))
            {
                var reviewColumn = inspection.Columns["Date"] + 1;
                dynamic column = sheet.Columns[reviewColumn];
                try { column.Insert(); } finally { Release(column); }
                Set(sheet, inspection.HeaderRow, reviewColumn, "Date de revue");
            }
        }
        finally { Release(sheet); }

        inspection = InspectWorkbook(workbook, initial.WorkbookPath)
            ?? throw new InvalidOperationException("La colonne Date de revue n'a pas pu être créée.");
        sheet = workbook.Worksheets[inspection.SheetName];
        try
        {
            NormalizeDates(sheet, inspection);
            BackfillDocumentHyperlinks(sheet, inspection);
        }
        finally { Release(sheet); }

        return InspectWorkbook(workbook, initial.WorkbookPath)
            ?? throw new InvalidOperationException("Le registre n'a pas pu être relu après sa préparation.");
    }

    private static void RemoveEmptyRows(dynamic sheet, ExcelInspection inspection)
    {
        var lastPossible = LastPossibleRow(sheet);
        var dataColumns = inspection.Columns
            .Where(pair => pair.Key is not "Number")
            .Select(pair => pair.Value)
            .ToArray();
        for (var row = lastPossible; row > inspection.HeaderRow; row--)
        {
            if (dataColumns.Any(column => !string.IsNullOrWhiteSpace(CellValue((object)sheet, row, column)))) continue;
            dynamic entireRow = sheet.Rows[row];
            try { entireRow.Delete(); } finally { Release(entireRow); }
        }
    }

    private static void NormalizeDates(dynamic sheet, ExcelInspection inspection)
    {
        var last = LastDataRow(sheet, inspection);
        for (var row = inspection.HeaderRow + 1; row <= last; row++)
        {
            DateTime updated;
            if (!TryReadDate((object)sheet, row, inspection.Columns["Date"], out updated)) continue;
            SetDate(sheet, row, inspection.Columns["Date"], updated);
            SetDate(sheet, row, inspection.Columns["ReviewDate"], updated.AddYears(1));
        }
    }

    private static void BackfillDocumentHyperlinks(dynamic sheet, ExcelInspection inspection)
    {
        var root = Path.Combine(ArsefRules.DetectDesktopRoot(), ArsefRules.RootFolderName);
        if (!Directory.Exists(root)) return;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = true
        };
        var documents = Directory.EnumerateFiles(root, "*.docx", options)
            .Take(10_000)
            .Select(path => (Code: Path.GetFileNameWithoutExtension(path) ?? string.Empty, Path: path))
            .Where(item => item.Code.Length > 0)
            .GroupBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Path, StringComparer.OrdinalIgnoreCase);
        var last = LastDataRow(sheet, inspection);
        for (var row = inspection.HeaderRow + 1; row <= last; row++)
        {
            var code = CellValue((object)sheet, row, inspection.Columns["Codification"]);
            var title = CellValue((object)sheet, row, inspection.Columns["Document"]);
            if (code.Length > 0 && title.Length > 0 && documents.TryGetValue(code, out var document))
                SetHyperlink(sheet, row, inspection.Columns["Document"], title, document, inspection.WorkbookPath);
        }
    }

    private static bool TryReadDate(object sheetObject, int row, int column, out DateTime value)
    {
        value = default;
        object? raw = RawCell(sheetObject, row, column);
        if (raw is double number && number > 0)
        {
            try { value = DateTime.FromOADate(number).Date; return true; } catch { return false; }
        }
        if (raw is DateTime date) { value = date.Date; return true; }
        var text = Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (DateTime.TryParse(text, FrenchCulture, DateTimeStyles.AllowWhiteSpaces, out value)) { value = value.Date; return true; }
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out value)) { value = value.Date; return true; }
        return false;
    }

    private static string FormatDate(DateTime date) => date.ToString("dd/MM/yyyy", FrenchCulture);

    private static void SetDate(dynamic sheet, int row, int column, DateTime date)
    {
        try { sheet.Cells[row, column].NumberFormat = "@"; } catch { }
        Set(sheet, row, column, FormatDate(date));
    }

    private static string FormatVersion(string version)
    {
        var value = version.Trim();
        if (value.StartsWith("v.", StringComparison.OrdinalIgnoreCase)) return value;
        if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase)) value = value[1..].TrimStart('.');
        return "v." + value;
    }

    private static int NextNumber(dynamic sheet, ExcelInspection inspection)
    {
        if (!inspection.Columns.TryGetValue("Number", out var numberColumn)) return 0;
        var maximum = 0;
        var last = LastDataRow(sheet, inspection);
        for (var row = inspection.HeaderRow + 1; row <= last; row++)
        {
            var value = CellValue((object)sheet, row, numberColumn);
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) maximum = Math.Max(maximum, number);
        }
        return maximum + 1;
    }

    private static dynamic StartExcel()
    {
        var type = Type.GetTypeFromProgID("Excel.Application")
                   ?? throw new InvalidOperationException("Microsoft Excel n'est pas installé sur cet ordinateur.");
        dynamic excel = Activator.CreateInstance(type)!;
        excel.Visible = false;
        excel.DisplayAlerts = false;
        excel.AutomationSecurity = 3;
        excel.AskToUpdateLinks = false;
        excel.EnableEvents = false;
        return excel;
    }

    private static ExcelInspection? InspectWorkbook(dynamic workbook, string path)
    {
        dynamic sheets = workbook.Worksheets;
        try
        {
            for (var index = 1; index <= (int)sheets.Count; index++)
            {
                dynamic sheet = sheets[index];
                try
                {
                    var inspection = InspectSheet(sheet, path);
                    if (inspection is not null) return inspection;
                }
                finally { Release(sheet); }
            }
        }
        finally { Release(sheets); }
        return null;
    }

    private static ExcelInspection? InspectSheet(dynamic sheet, string path)
    {
        dynamic used = sheet.UsedRange;
        try
        {
            var rows = (int)used.Rows.Count;
            var columns = (int)used.Columns.Count;
            if (rows < 2 || columns < 2) return null;
            var values = used.Value2;
            var firstRow = (int)used.Row;
            var firstColumn = (int)used.Column;
            Dictionary<string, int>? found = null;
            var headerRow = 0;
            for (var relativeRow = 1; relativeRow <= Math.Min(rows, 15); relativeRow++)
            {
                var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (var relativeColumn = 1; relativeColumn <= columns; relativeColumn++)
                {
                    var header = CellText(values, relativeRow, relativeColumn, rows, columns);
                    if (!string.IsNullOrWhiteSpace(header)) headers[Normalize(header)] = firstColumn + relativeColumn - 1;
                }
                var mapped = MapColumns(headers);
                ResolveNumberColumn(sheet, mapped, headerRow: firstRow + relativeRow - 1, lastRow: firstRow + rows - 1);
                if (RequiredColumns.All(mapped.ContainsKey)) { found = mapped; headerRow = firstRow + relativeRow - 1; break; }
            }
            if (found is null) return null;
            var options = new List<string>();
            var lastRow = firstRow + rows - 1;
            for (var row = headerRow + 1; row <= lastRow; row++)
            {
                foreach (var value in CellValue((object)sheet, row, found["Classer"])
                             .Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (!options.Contains(value, StringComparer.OrdinalIgnoreCase)) options.Add(value);
            }
            return new ExcelInspection(Path.GetFullPath(path), (string)sheet.Name, headerRow, found, options, firstColumn + columns - 1);
        }
        finally { Release(used); }
    }

    private static Dictionary<string, int> MapColumns(Dictionary<string, int> headers)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Add(headers, result, "Codification", "codification existante", "codification", "code");
        Add(headers, result, "Document", "document enregistre", "document", "titre", "intitule", "objet");
        Add(headers, result, "Type", "type");
        Add(headers, result, "Domain", "domaine");
        Add(headers, result, "Version", "numero de version", "version");
        Add(headers, result, "Date", "date de mise a jour", "date de validite", "date");
        Add(headers, result, "ReviewDate", "date de revue", "revue");
        Add(headers, result, "Classer", "lieu de classement", "classeur", "emplacement", "localisation", "dossier");
        Add(headers, result, "Author", "prepare par", "auteur", "responsable");
        AddExact(headers, result, "Number", "numero", "numero d ordre", "ordre", "n", "n a");
        return result;
    }

    private static void AddExact(Dictionary<string, int> headers, Dictionary<string, int> result, string key, params string[] aliases)
    {
        foreach (var alias in aliases)
            if (headers.TryGetValue(Normalize(alias), out var column))
            {
                result[key] = column;
                return;
            }
    }

    private static void ResolveNumberColumn(dynamic sheet, Dictionary<string, int> columns, int headerRow, int lastRow)
    {
        if (!columns.TryGetValue("Document", out var documentColumn)) return;

        var values = Enumerable.Range(headerRow + 1, Math.Max(0, lastRow - headerRow))
            .Select(row => CellValue((object)sheet, row, documentColumn))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Take(25)
            .ToArray();
        if (values.Length == 0 || !values.All(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))) return;

        columns["Number"] = documentColumn;
        columns.Remove("Document");
    }

    private static void Add(Dictionary<string, int> headers, Dictionary<string, int> result, string key, params string[] aliases)
    {
        foreach (var header in headers)
        foreach (var alias in aliases)
        {
            var normalizedAlias = Normalize(alias);
            if (header.Key.Equals(normalizedAlias, StringComparison.OrdinalIgnoreCase) ||
                (normalizedAlias.Length > 2 && header.Key.Contains(normalizedAlias, StringComparison.OrdinalIgnoreCase)))
            {
                result[key] = header.Value;
                return;
            }
        }
    }

    private static string Normalize(string value)
    {
        var form = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(form.Length);
        foreach (var character in form)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != System.Globalization.UnicodeCategory.NonSpacingMark)
                builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        return string.Join(' ', builder.ToString().Split([' '], StringSplitOptions.RemoveEmptyEntries));
    }

    private static string CellText(object values, int row, int column, int rows, int columns)
    {
        object? value = values is Array array && rows > 1 && columns > 1 ? array.GetValue(row, column) : row == 1 && column == 1 ? values : null;
        return Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
    }

    private static bool ContainsCode(dynamic sheet, ExcelInspection inspection, string code)
    {
        var last = LastDataRow(sheet, inspection);
        for (var row = inspection.HeaderRow + 1; row <= last; row++)
            if (string.Equals(CellValue((object)sheet, row, inspection.Columns["Codification"]), code, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static int LastDataRow(dynamic sheet, ExcelInspection inspection)
    {
        var columns = inspection.Columns.Values.Distinct().ToArray();
        var lastPossible = LastPossibleRow(sheet);
        for (var row = lastPossible; row > inspection.HeaderRow; row--)
            if (columns.Any(column => !string.IsNullOrWhiteSpace(CellValue((object)sheet, row, column)))) return row;
        return inspection.HeaderRow;
    }

    private static int LastPossibleRow(dynamic sheet)
    {
        dynamic used = sheet.UsedRange;
        try { return (int)used.Row + (int)used.Rows.Count - 1; }
        finally { Release(used); }
    }

    private static void CopyPreviousRowFormatting(dynamic sheet, int row, ExcelInspection inspection)
    {
        if (row <= inspection.HeaderRow + 1) return;
        try
        {
            dynamic source = sheet.Range[sheet.Cells[row - 1, 1], sheet.Cells[row - 1, inspection.LastUsedColumn]];
            dynamic target = sheet.Range[sheet.Cells[row, 1], sheet.Cells[row, inspection.LastUsedColumn]];
            source.Copy();
            target.PasteSpecial(-4122);
            sheet.Application.CutCopyMode = false;
            Release(source);
            Release(target);
        }
        catch { }
    }

    private static object? RawCell(object sheetObject, int row, int column)
    {
        dynamic sheet = sheetObject;
        return sheet.Cells[row, column].Value2;
    }

    private static string CellValue(object sheetObject, int row, int column)
        => Convert.ToString(RawCell(sheetObject, row, column), CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;

    private static void Set(dynamic sheet, int row, int column, object value) => sheet.Cells[row, column].Value2 = value;

    private static void SetHyperlink(dynamic sheet, int row, int column, string displayText, string documentPath, string workbookPath)
    {
        var fullDocumentPath = Path.GetFullPath(documentPath);
        if (!File.Exists(fullDocumentPath)) throw new FileNotFoundException("Le document à lier dans le registre est introuvable.", fullDocumentPath);
        var address = LinkAddress(fullDocumentPath, workbookPath);
        dynamic? cell = null;
        dynamic? hyperlinks = null;
        try
        {
            cell = sheet.Cells[row, column];
            hyperlinks = sheet.Hyperlinks;
            try { cell.Hyperlinks.Delete(); } catch { }
            hyperlinks.Add(cell, address, Type.Missing, Type.Missing, displayText);
        }
        finally
        {
            Release(hyperlinks);
            Release(cell);
        }
    }

    private static string LinkAddress(string documentPath, string workbookPath)
    {
        var fullDocumentPath = Path.GetFullPath(documentPath);
        var workbookFolder = Path.GetDirectoryName(Path.GetFullPath(workbookPath))!;
        return Path.GetPathRoot(workbookFolder)!.Equals(Path.GetPathRoot(fullDocumentPath), StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(workbookFolder, fullDocumentPath)
            : fullDocumentPath;
    }

    private static void CloseExcel(dynamic? workbook, dynamic? excel, bool save)
    {
        if (workbook is not null) { try { workbook.Close(save); } catch { } Release(workbook); }
        if (excel is not null) { try { excel.Quit(); } catch { } Release(excel); }
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    private static void Release(object? value)
    {
        try { if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); } catch { }
    }

    internal static void SelfCheck()
    {
        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [Normalize("Document enregistré")] = 2,
            [Normalize("Codification")] = 3,
            [Normalize("N° de version")] = 4,
            [Normalize("N°")] = 1
        };
        var mapped = MapColumns(headers);
        if (mapped["Document"] != 2 || mapped["Version"] != 4 || mapped["Number"] != 1 || FormatVersion("v1") != "v.1")
            throw new InvalidOperationException("Excel mapping self-check failed.");
        var relative = LinkAddress(@"C:\Users\Person\OneDrive\ARSEF\Desktop\ARSEF\document.docx", @"C:\Users\Person\OneDrive\Gestion documentaire ARSEF\registre.xlsx");
        if (Path.IsPathRooted(relative) || !relative.EndsWith("document.docx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Excel hyperlink self-check failed.");
    }
}
