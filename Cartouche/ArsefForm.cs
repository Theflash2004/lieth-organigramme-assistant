using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AssistantArsef.Core;
using LiethOrganigrammeAssistant;

namespace AssistantArsef;

internal sealed class ArsefForm : Form
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly ComboBox modelBox = new();
    private readonly ComboBox typeBox = new();
    private readonly ComboBox domainBox = new();
    private readonly ComboBox serviceBox = new();
    private readonly TextBox recipientBox = new();
    private readonly TextBox titleBox = new();
    private readonly TextBox codeWordBox = new();
    private readonly TextBox versionBox = new();
    private readonly TextBox authorBox = new();
    private readonly DateTimePicker dateBox = new();
    private readonly Label titleLabel = new();
    private readonly Label recipientLabel = new();
    private readonly Label codePreview = new();
    private readonly Label pathPreview = new();
    private readonly Label status = new();
    private readonly Button createButton = new();
    private readonly Button documentFinishedButton = new();
    private readonly ListBox recentDocuments = new();
    private IReadOnlyList<DocumentHistoryEntry> recentEntries = [];
    private string arsefRoot = string.Empty;
    private string settingsPath = string.Empty;
    private bool foldersPrepared;
    private FileStream? outputReservation;
    private ActiveDocumentSession? activeSession;
    private bool operationRunning;

    public ArsefForm()
    {
        Text = "Diva cartouche assistant";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 650);
        Size = new Size(820, 720);
        Font = new Font("Segoe UI", 10F);

        BuildUi();
        LoadSettings();
        RefreshRecentDocuments();
        ApplySelectionRules();
        UpdatePreview();
        FormClosing += (_, eventArgs) =>
        {
            if (!operationRunning) return;
            eventArgs.Cancel = true;
            MessageBox.Show(this, "Une opération Word ou Excel est en cours. Attendez sa fin avant de fermer cette fenêtre.",
                "Diva travaille", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        Shown += (_, _) =>
        {
            RestorePendingSession();
        };
    }

    private void BuildUi()
    {
        var purple = Color.FromArgb(112, 48, 160);
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 17,
            AutoScroll = true,
            BackColor = Color.FromArgb(248, 246, 251)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 225));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var banner = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(10, 8, 10, 8),
            Margin = new Padding(0, 0, 0, 8)
        };
        banner.Controls.Add(new PictureBox
        {
            Image = LoadLogo(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(52, 52),
            Margin = new Padding(0, 0, 12, 0)
        });
        banner.Controls.Add(new Label
        {
            Text = "Diva cartouche assistant",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 16F, FontStyle.Bold),
            ForeColor = purple,
            Margin = new Padding(0, 12, 0, 0)
        });
        banner.Controls.Add(new Label
        {
            Text = "Créer • classer • exporter le PDF à la fin",
            AutoSize = true,
            ForeColor = Color.FromArgb(95, 95, 95),
            Margin = new Padding(18, 17, 0, 0)
        });
        root.Controls.Add(banner, 0, 0);
        root.SetColumnSpan(banner, 2);

        AddRow(root, 1, "Modèle", modelBox);
        AddRow(root, 2, "Titre du document", titleBox, "Exemple : Codification des documents");
        AddRow(root, 3, "Type", typeBox);
        AddRow(root, 4, "Domaine", domainBox);
        AddRow(root, 5, "Service", serviceBox);
        AddRow(root, 6, "Mot-clé de codification", codeWordBox, "Libre : texte long accepté");
        AddRow(root, 7, "Version", versionBox);
        AddRow(root, 8, "Préparé par", authorBox);
        AddRow(root, 9, "Date de validité", dateBox);

        codePreview.AutoSize = true;
        codePreview.Font = new Font(Font, FontStyle.Bold);
        codePreview.ForeColor = Color.DarkGreen;
        root.Controls.Add(new Label { Text = "Code généré", AutoSize = true }, 0, 10);
        root.Controls.Add(codePreview, 1, 11);

        pathPreview.AutoSize = true;
        pathPreview.MaximumSize = new Size(520, 0);
        pathPreview.ForeColor = Color.FromArgb(70, 70, 70);
        root.Controls.Add(new Label { Text = "Dossier prévu", AutoSize = true }, 0, 11);
        root.Controls.Add(pathPreview, 1, 12);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        createButton.Text = "Créer et ouvrir le document";
        createButton.AutoSize = true;
        createButton.Height = 38;
        createButton.Padding = new Padding(12, 0, 12, 0);
        createButton.FlatStyle = FlatStyle.Flat;
        createButton.BackColor = Color.FromArgb(112, 48, 160);
        createButton.ForeColor = Color.White;
        createButton.UseVisualStyleBackColor = false;
        createButton.Click += async (_, _) => await CreateNewAsync();
        actions.Controls.Add(createButton);
        root.Controls.Add(actions, 0, 13);
        root.SetColumnSpan(actions, 2);

        var finishedGroup = new GroupBox
        {
            Text = "Quand le contenu est terminé",
            Dock = DockStyle.Fill,
            AutoSize = true,
            ForeColor = purple,
            Padding = new Padding(10, 8, 10, 4),
            Margin = new Padding(0, 4, 0, 4)
        };
        var finishedLayout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2 };
        finishedLayout.Controls.Add(new Label
        {
            Text = "Après avoir complété et enregistré le document dans Word :",
            AutoSize = true,
            ForeColor = Color.FromArgb(70, 70, 70),
            Margin = new Padding(0, 8, 12, 8)
        }, 0, 0);
        var finishedActions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        documentFinishedButton.Text = "Document fini — exporter le PDF";
        documentFinishedButton.AutoSize = true;
        documentFinishedButton.MinimumSize = new Size(275, 40);
        documentFinishedButton.Enabled = false;
        documentFinishedButton.Click += async (_, _) => await FinishDocumentAsync();
        finishedActions.Controls.Add(documentFinishedButton);
        finishedActions.Controls.Add(Button("Historique complet", (_, _) => ShowHistory()));
        finishedLayout.Controls.Add(finishedActions, 0, 1);
        finishedGroup.Controls.Add(finishedLayout);
        root.Controls.Add(finishedGroup, 0, 14);
        root.SetColumnSpan(finishedGroup, 2);

        var recentGroup = new GroupBox
        {
            Text = "Documents récents",
            Dock = DockStyle.Fill,
            AutoSize = true,
            ForeColor = purple,
            Padding = new Padding(10),
            Margin = new Padding(0, 4, 0, 4)
        };
        var recentLayout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2 };
        recentDocuments.Height = 105;
        recentDocuments.IntegralHeight = false;
        recentDocuments.Dock = DockStyle.Top;
        recentDocuments.HorizontalScrollbar = true;
        recentDocuments.DoubleClick += (_, _) => OpenRecentDocument();
        recentLayout.Controls.Add(recentDocuments, 0, 0);
        var recentActions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        recentActions.Controls.Add(Button("Ouvrir le document sélectionné", (_, _) => OpenRecentDocument()));
        recentActions.Controls.Add(Button("Ouvrir son dossier", (_, _) => OpenRecentFolder()));
        recentActions.Controls.Add(Button("Actualiser les liens du registre", async (_, _) => await UpdateRegistryLinksAsync()));
        recentLayout.Controls.Add(recentActions, 0, 1);
        recentGroup.Controls.Add(recentLayout);
        root.Controls.Add(recentGroup, 0, 15);
        root.SetColumnSpan(recentGroup, 2);

        status.AutoSize = true;
        status.MaximumSize = new Size(700, 0);
        status.ForeColor = Color.FromArgb(70, 70, 70);
        root.Controls.Add(status, 0, 16);
        root.SetColumnSpan(status, 2);

        var oldTitleLabel = root.GetControlFromPosition(0, 2);
        if (oldTitleLabel is not null) root.Controls.Remove(oldTitleLabel);
        titleLabel.Text = "Titre du document";
        titleLabel.AutoSize = true;
        titleLabel.Anchor = AnchorStyles.Left;
        titleLabel.Margin = new Padding(0, 8, 0, 4);
        root.Controls.Add(titleLabel, 0, 2);

        var oldCodeLabel = root.GetControlFromPosition(0, 10);
        var oldPathLabel = root.GetControlFromPosition(0, 11);
        if (oldCodeLabel is not null) root.Controls.Remove(oldCodeLabel);
        if (oldPathLabel is not null) root.Controls.Remove(oldPathLabel);
        recipientLabel.Text = "Destinataire";
        recipientLabel.AutoSize = true;
        recipientLabel.Anchor = AnchorStyles.Left;
        recipientLabel.Margin = new Padding(0, 8, 0, 4);
        recipientBox.Dock = DockStyle.Top;
        recipientBox.Margin = new Padding(0, 4, 0, 4);
        recipientBox.PlaceholderText = "Exemple : Madame Dupont ou service@exemple.fr";
        root.Controls.Add(recipientLabel, 0, 10);
        root.Controls.Add(recipientBox, 1, 10);
        root.Controls.Add(new Label { Text = "Code généré", AutoSize = true }, 0, 11);
        root.Controls.Add(new Label { Text = "Dossier prévu", AutoSize = true }, 0, 12);

        modelBox.DropDownStyle = ComboBoxStyle.DropDownList;
        typeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        domainBox.DropDownStyle = ComboBoxStyle.DropDownList;
        serviceBox.DropDownStyle = ComboBoxStyle.DropDownList;
        modelBox.DropDownWidth = 360;
        typeBox.DropDownWidth = 620;
        domainBox.DropDownWidth = 620;
        serviceBox.DropDownWidth = 420;
        modelBox.DataSource = TemplateCatalog.Models.ToList();
        typeBox.DataSource = ArsefRules.Types.ToList();
        domainBox.DataSource = ArsefRules.Domains.ToList();
        serviceBox.DataSource = ArsefRules.Services.ToList();
        modelBox.DisplayMember = nameof(ArsefTemplateModel.Label);
        typeBox.DisplayMember = nameof(ArsefOption.ChoiceLabel);
        domainBox.DisplayMember = nameof(ArsefOption.ShortLabel);
        serviceBox.DisplayMember = nameof(ArsefOption.ChoiceLabel);
        modelBox.SelectedIndexChanged += (_, _) => { ApplyModelRules(true); ApplySelectionRules(); UpdatePreview(); };
        typeBox.SelectedIndexChanged += (_, _) => { ApplySelectionRules(); UpdatePreview(); };
        domainBox.SelectedIndexChanged += (_, _) => { ApplySelectionRules(); UpdatePreview(); };
        serviceBox.SelectedIndexChanged += (_, _) => UpdatePreview();
        foreach (var control in new Control[] { titleBox, recipientBox, codeWordBox, versionBox, authorBox })
            control.TextChanged += (_, _) => UpdatePreview();
        authorBox.TextChanged += (_, _) => { if (settingsPath.Length > 0) SaveSettings(); };
        dateBox.ValueChanged += (_, _) => UpdatePreview();
        modelBox.SelectedIndex = 0;
        typeBox.SelectedIndex = 0;
        domainBox.SelectedIndex = 0;
        serviceBox.SelectedIndex = 0;
        versionBox.Text = "1";
        authorBox.Text = Environment.UserName;
        dateBox.Value = DateTime.Today;
    }

    private static void AddRow(TableLayoutPanel root, int row, string label, Control control, string? placeholder = null)
    {
        root.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Color.FromArgb(78, 35, 112), Margin = new Padding(0, 8, 0, 4) }, 0, row);
        control.Dock = DockStyle.Top;
        control.Margin = new Padding(0, 4, 0, 4);
        control.BackColor = Color.White;
        if (control is TextBox textBox && placeholder is not null)
            textBox.PlaceholderText = placeholder;
        root.Controls.Add(control, 1, row);
    }

    private static Button Button(string text, EventHandler click, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 36,
            Padding = new Padding(12, 0, 12, 0),
            Margin = new Padding(0, 0, 8, 8),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Color.FromArgb(112, 48, 160) : Color.White,
            ForeColor = primary ? Color.White : Color.FromArgb(112, 48, 160),
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(112, 48, 160);
        button.FlatAppearance.BorderSize = 1;
        button.Click += click;
        return button;
    }

    private void ApplySelectionRules()
    {
        var domain = domainBox.SelectedItem as ArsefOption;
        var type = typeBox.SelectedItem as ArsefOption;
        var rgpd = type?.Code == "RGPD";
        if (rgpd)
        {
            domainBox.SelectedItem = ArsefRules.Domains.FirstOrDefault(x => x.Code == "QUA") ??
                                     (ArsefRules.Domains.Count > 0 ? ArsefRules.Domains[0] : null);
            domainBox.Enabled = false;
        }
        else
        {
            domainBox.Enabled = true;
        }

        var selectedDomain = domainBox.SelectedItem as ArsefOption;
        var needsService = selectedDomain?.Code.Equals(ArsefRules.ServiceDomainCode, StringComparison.OrdinalIgnoreCase) == true;
        serviceBox.Enabled = needsService;
        serviceBox.Visible = needsService;
        if (!needsService) serviceBox.SelectedIndex = -1;
        else if (serviceBox.SelectedIndex < 0) serviceBox.SelectedIndex = 0;

        ApplyModelRules();
    }

    private void ApplyModelRules(bool resetRegisterDefaults = false)
    {
        var model = modelBox.SelectedItem as ArsefTemplateModel;
        var email = model?.Kind == ArsefTemplateKind.Plain;
        titleLabel.Text = email ? "Objet du document" : "Titre du document";
        titleBox.PlaceholderText = email ? "Exemple : Accusé de réception de votre réclamation" : "Exemple : Codification des documents";
        codeWordBox.PlaceholderText = "Mot-clé : décrire le document en 3 mots";
        recipientLabel.Visible = email;
        recipientBox.Visible = email;
        if (model?.DefaultTypeCode is { Length: > 0 } defaultTypeCode)
        {
            typeBox.SelectedItem = ArsefRules.GetType(defaultTypeCode);
            typeBox.Enabled = false;
        }
        else
        {
            typeBox.Enabled = true;
        }

        if (model?.DefaultDomainCode is { Length: > 0 } defaultDomainCode)
        {
            domainBox.SelectedItem = ArsefRules.GetDomain(defaultDomainCode);
            domainBox.Enabled = false;
        }

        if (model?.Kind == ArsefTemplateKind.Register && resetRegisterDefaults)
        {
            typeBox.SelectedItem = ArsefRules.Types.FirstOrDefault(x => x.Code == "REG") ??
                                   (ArsefRules.Types.Count > 0 ? ArsefRules.Types[0] : null);
            var registerDomain = model?.DefaultDomainCode;
            domainBox.SelectedItem = ArsefRules.Domains.FirstOrDefault(x => x.Code == registerDomain) ??
                                     (ArsefRules.Domains.Count > 0 ? ArsefRules.Domains[0] : null);
            if (string.IsNullOrWhiteSpace(titleBox.Text)) titleBox.Text = "Registre";
            if (string.IsNullOrWhiteSpace(codeWordBox.Text)) codeWordBox.Text = "REGISTRE";
            if (string.IsNullOrWhiteSpace(versionBox.Text)) versionBox.Text = "1";
        }
    }

    private void UpdatePreview()
    {
        try
        {
            var input = ReadInput();
            var plan = ArsefRules.CreatePlan(input, string.IsNullOrWhiteSpace(arsefRoot) ? DesktopArsefRoot() : arsefRoot);
            codePreview.Text = plan.Code;
            pathPreview.Text = plan.OutputFolder;
        }
        catch
        {
            codePreview.Text = "À compléter";
            pathPreview.Text = "À compléter";
        }
    }

    private ArsefInput ReadInput()
    {
        var model = modelBox.SelectedItem as ArsefTemplateModel;
        var type = (typeBox.SelectedItem as ArsefOption)?.Code ?? string.Empty;
        var domain = (domainBox.SelectedItem as ArsefOption)?.Code ?? string.Empty;
        var service = domain.Equals(ArsefRules.ServiceDomainCode, StringComparison.OrdinalIgnoreCase)
            ? (serviceBox.SelectedItem as ArsefOption)?.Code ?? string.Empty
            : string.Empty;
        return new ArsefInput(titleBox.Text, type, domain, service, codeWordBox.Text, versionBox.Text, authorBox.Text, dateBox.Value.Date)
        {
            EmailSubject = model?.Kind == ArsefTemplateKind.Plain ? titleBox.Text : string.Empty,
            EmailRecipient = model?.Kind == ArsefTemplateKind.Plain ? recipientBox.Text : string.Empty
        };
    }

    private bool ValidateInput(out ArsefInput input, out ArsefPlan plan)
    {
        input = ReadInput();
        plan = null!;
        var errors = ArsefRules.Validate(input, arsefRoot).ToList();
        if ((modelBox.SelectedItem as ArsefTemplateModel)?.Kind == ArsefTemplateKind.Plain && string.IsNullOrWhiteSpace(input.EmailRecipient))
            errors.Add("Le destinataire est obligatoire pour un modèle Email.");
        if (errors.Count > 0)
        {
            MessageBox.Show(string.Join(Environment.NewLine, errors), "Vérification nécessaire", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        plan = ArsefRules.CreatePlan(input, arsefRoot);
        return true;
    }

    private async Task CreateNewAsync()
    {
        if (operationRunning) return;
        if (activeSession is not null && File.Exists(activeSession.DocxPath))
        {
            var answer = MessageBox.Show(
                "Un document est encore en cours :\r\n\r\n" + activeSession.Code + "\r\n\r\n" +
                "Cliquez sur « Document fini » pour l'exporter, ou choisissez Oui pour abandonner cette session et créer un nouveau document.",
                "Document en cours", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer == DialogResult.No) return;
            ClearActiveSession();
        }

        if (!EnsureArsefRoot()) return;
        if (!ValidateInput(out var input, out var plan)) return;
        if (!PrepareFolders()) return;
        if (!ConfirmOutput(plan)) return;
        try
        {
            SetBusy(true, "Création du document Word…");
            var model = SelectedModel();
            var template = TemplateCatalog.Extract(model);
            await StaTask.Run(() => WordAutomation.CreateFromTemplate(template, input, plan, model.Kind));
            Finish(plan, input, model);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            outputReservation?.Dispose();
            outputReservation = null;
            SetBusy(false);
        }
    }

    private bool ConfirmOutput(ArsefPlan plan)
    {
        if (Directory.Exists(plan.OutputFolder) && !Directory.EnumerateFileSystemEntries(plan.OutputFolder).Any())
        {
            try { Directory.Delete(plan.OutputFolder); } catch { }
        }

        if (Directory.Exists(plan.OutputFolder) || File.Exists(plan.DocxPath))
        {
            ShowCollision(plan.OutputFolder);
            return false;
        }

        try
        {
            var parent = Path.GetDirectoryName(plan.OutputFolder)!;
            var lockFolder = Path.Combine(AppPaths.DataRoot, "Locks");
            Directory.CreateDirectory(lockFolder);
            var lockName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(parent)))[..16] + ".lock";
            // One local creator at a time for this parent; the handle also prevents stale lock files.
            outputReservation = new FileStream(Path.Combine(lockFolder, lockName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (Directory.Exists(plan.OutputFolder) || File.Exists(plan.DocxPath))
            {
                outputReservation.Dispose();
                outputReservation = null;
                ShowCollision(plan.OutputFolder);
                return false;
            }
            return true;
        }
        catch (IOException)
        {
            MessageBox.Show(
                "Un autre document est déjà en cours de création dans ce dossier. Attendez quelques secondes, puis réessayez.",
                "Création en cours", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
    }

    private static void ShowCollision(string outputFolder)
    {

        MessageBox.Show(
            "Un dossier ou document portant exactement ce nom existe déjà :\r\n\r\n" +
            outputFolder + "\r\n\r\n" +
            "Si c'est une nouvelle version, changez le champ « Version ».\r\n" +
            "Si c'est un nouveau document, changez le « Mot-clé de codification ».\r\n\r\n" +
            "Aucun fichier n'a été remplacé.",
            "Nom déjà utilisé", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void Finish(ArsefPlan plan, ArsefInput input, ArsefTemplateModel model)
    {
        status.Text = "Terminé : le document Word et le PDF sont sur le Bureau.\r\n" + plan.OutputFolder;
        activeSession = ActiveDocumentSession.From(input, plan, model.Code);
        SaveActiveSession();
        DocumentHistory.Started(new DocumentHistoryEntry(activeSession.Code, activeSession.Title, activeSession.DocxPath, activeSession.PdfPath, DateTime.Now, null, false));
        RefreshRecentDocuments();
        documentFinishedButton.Enabled = true;
        status.Text = "Document Word créé. Complétez son contenu, enregistrez-le, puis cliquez sur « Document fini ».\r\n" + plan.OutputFolder;
        TryOpenFile(plan.DocxPath);
        SelectInExplorer(plan.DocxPath);
    }

    private async Task FinishDocumentAsync()
    {
        if (operationRunning) return;
        if (activeSession is null) LoadActiveSession();
        if (activeSession is null)
        {
            MessageBox.Show("Aucun document en cours n'a été trouvé.", "Document fini", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!File.Exists(activeSession.DocxPath))
        {
            MessageBox.Show("Le fichier Word de la session n'existe plus :\r\n" + activeSession.DocxPath, "Document introuvable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            ClearActiveSession();
            return;
        }

        var management = MessageBox.Show(
            "Ce document doit-il être inclus dans la gestion documentaire ?",
            "Gestion documentaire", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        try
        {
            SetBusy(true, "Export du PDF final…");
            await StaTask.Run(() => WordAutomation.UpdatePdf(activeSession.DocxPath, activeSession.PdfPath));
            if (!File.Exists(activeSession.PdfPath) || new FileInfo(activeSession.PdfPath).Length == 0)
                throw new InvalidOperationException("Le PDF n'a pas pu être créé ou vérifié.");

            string? oneDrivePath = null;
            if (MessageBox.Show(
                    this,
                    "Voulez-vous également copier le document Word et son PDF dans le dossier ARSEF partagé sur OneDrive ?\r\n\r\n" +
                    "Les originaux resteront sur votre Bureau.",
                    "Copie OneDrive ARSEF",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) == DialogResult.Yes)
            {
                status.Text = "Copie sécurisée vers OneDrive ARSEF…";
                oneDrivePath = await Task.Run(() => OneDriveArsefCopy.Copy(activeSession.ToPlan()));
            }

            if (management == DialogResult.Yes && !await AppendToManagementRegisterAsync(oneDrivePath)) return;

            var pdfPath = activeSession.PdfPath;
            var code = activeSession.Code;
            DocumentHistory.Finished(code, management == DialogResult.Yes, oneDrivePath);
            RefreshRecentDocuments();
            ClearActiveSession();
            status.Text = "Document terminé : PDF exporté" +
                          (management == DialogResult.Yes ? ", registre mis à jour" : "") +
                          (oneDrivePath is not null ? ", copie OneDrive créée" : "") +
                          ".\r\n" + pdfPath;
            TryOpenFile(pdfPath);
            SelectInExplorer(pdfPath);
            MessageBox.Show("PDF créé :\r\n" + code + ".pdf", "Document fini", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<bool> AppendToManagementRegisterAsync(string? oneDrivePath)
    {
        if (activeSession is null) return false;
        var workbookPath = ChooseManagementWorkbook(activeSession.RegistryPath);
        if (string.IsNullOrWhiteSpace(workbookPath)) return false;

        ExcelInspection inspection;
        try
        {
            SetBusy(true, "Lecture sécurisée du registre…");
            inspection = await StaTask.Run(() => ExcelDocumentService.Inspect(workbookPath));
        }
        catch (Exception ex)
        {
            ShowError(ex);
            return false;
        }
        finally
        {
            SetBusy(false);
        }

        if (inspection.ClasserOptions.Count == 0)
        {
            MessageBox.Show("La colonne « Lieu de classement » ne contient encore aucune option utilisable.", "Classeur incomplet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        using var selector = new ClasserSelectionDialog(inspection.ClasserOptions);
        if (selector.ShowDialog(this) != DialogResult.OK || selector.SelectedValues.Count == 0)
        {
            MessageBox.Show("Sélectionnez au moins un lieu de classement pour continuer.", "Sélection nécessaire", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        activeSession = activeSession with { RegistryPath = workbookPath };
        SaveActiveSession();
        try
        {
            SetBusy(true, "Ajout du document au registre…");
            var result = await StaTask.Run(() => ExcelDocumentService.Append(
                workbookPath,
                activeSession.ToInput(),
                activeSession.ToPlan(),
                selector.SelectedValues,
                oneDrivePath is null
                    ? activeSession.DocxPath
                    : Path.Combine(oneDrivePath, Path.GetFileName(activeSession.DocxPath))));
            MessageBox.Show(result.Message, "Gestion documentaire", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }
        catch (Exception ex)
        {
            ShowError(ex);
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task UpdateRegistryLinksAsync()
    {
        if (operationRunning) return;
        var workbookPath = ChooseManagementWorkbook(activeSession?.RegistryPath);
        if (string.IsNullOrWhiteSpace(workbookPath)) return;
        try
        {
            SetBusy(true, "Recherche des documents ARSEF et mise à jour des liens…");
            await StaTask.Run(() => ExcelDocumentService.Prepare(workbookPath));
            MessageBox.Show(
                this,
                "Les noms du registre ont été reliés aux documents trouvés dans ARSEF sur ce Bureau.",
                "Liens du registre actualisés",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception error) { ShowError(error); }
        finally { SetBusy(false); }
    }

    private string? ChooseManagementWorkbook(string? rememberedPath)
    {
        if (!string.IsNullOrWhiteSpace(rememberedPath) && File.Exists(rememberedPath))
        {
            var answer = MessageBox.Show(
                "Utiliser le classeur mémorisé ?\r\n\r\n" + rememberedPath,
                "Classeur de gestion documentaire", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Yes) return rememberedPath;
            if (answer == DialogResult.Cancel) return null;
        }

        var automaticPath = FindDefaultManagementWorkbook();
        if (automaticPath is not null)
        {
            var answer = MessageBox.Show(
                "Classeur ARSEF trouvé automatiquement :\r\n\r\n" + automaticPath + "\r\n\r\nUtiliser ce classeur ?",
                "Classeur de gestion documentaire", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Yes) return automaticPath;
            if (answer == DialogResult.Cancel) return null;
        }

        using var picker = new OpenFileDialog
        {
            Title = "Choisir le classeur de gestion documentaire",
            Filter = "Classeur Excel sans macros (*.xlsx)|*.xlsx",
            CheckFileExists = true,
            Multiselect = false
        };
        return picker.ShowDialog(this) == DialogResult.OK ? picker.FileName : null;
    }

    private static string? FindDefaultManagementWorkbook()
    {
        const string fileName = "REGISTRE DE MAITRISE DOCUMENTAIRE - VERSION DIRECTION - ORDRE ALPHABETIQUE - COMPLET.xlsx";
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(profile)) return null;
        try
        {
            foreach (var bundled in new[]
                     {
                         Path.Combine(AppContext.BaseDirectory, fileName),
                         Path.Combine(AppPaths.TemplatesRoot, fileName)
                     })
                if (File.Exists(bundled)) return bundled;

            foreach (var oneDrive in Directory.EnumerateDirectories(profile, "OneDrive*", SearchOption.TopDirectoryOnly))
            {
                var candidate = Path.Combine(oneDrive, "Gestion documentaire ARSEF", fileName);
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch { }
        return null;
    }

    private void ShowHistory()
    {
        using var dialog = new DocumentHistoryDialog(DocumentHistory.Load());
        dialog.ShowDialog(this);
        RefreshRecentDocuments();
    }

    private void RefreshRecentDocuments()
    {
        recentEntries = DocumentHistory.Load().OrderByDescending(entry => entry.StartedAt).Take(8).ToArray();
        recentDocuments.Items.Clear();
        foreach (var entry in recentEntries)
        {
            var state = entry.FinishedAt is null ? "En cours" : entry.OneDrivePath is null ? "Terminé" : "Terminé · OneDrive";
            recentDocuments.Items.Add($"{state} — {entry.StartedAt:dd/MM/yyyy HH:mm} — {entry.Code} — {entry.Title}");
        }
        if (recentDocuments.Items.Count > 0) recentDocuments.SelectedIndex = 0;
    }

    private DocumentHistoryEntry? SelectedRecent() =>
        recentDocuments.SelectedIndex >= 0 && recentDocuments.SelectedIndex < recentEntries.Count
            ? recentEntries[recentDocuments.SelectedIndex]
            : null;

    private void OpenRecentDocument()
    {
        var entry = SelectedRecent();
        if (entry is not null && DocumentHistory.IsSafeDocumentPath(entry.DocxPath)) TryOpenFile(entry.DocxPath);
    }

    private void OpenRecentFolder()
    {
        var entry = SelectedRecent();
        var folder = entry is null ? null : Path.GetDirectoryName(entry.DocxPath);
        if (folder is not null && DocumentHistory.IsSafeDocumentPath(folder)) TryOpenFile(folder);
    }

    private bool EnsureArsefRoot()
    {
        if (string.IsNullOrWhiteSpace(arsefRoot)) arsefRoot = DesktopArsefRoot();
        return !string.IsNullOrWhiteSpace(arsefRoot);
    }

    private bool PrepareFolders()
    {
        if (foldersPrepared) return true;
        try
        {
            ArsefRules.PrepareFixedFolders(arsefRoot);
            foldersPrepared = true;
            return true;
        }
        catch (Exception ex)
        {
            ShowError(ex);
            return false;
        }
    }

    private static void SelectInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch { }
    }

    private static Bitmap? LoadLogo()
    {
        using var source = typeof(ArsefForm).Assembly.GetManifestResourceStream("AssistantArsef.Assets.diva-cat-logo.png");
        if (source is null) return null;
        using var image = Image.FromStream(source);
        return new Bitmap(image);
    }

    private static void TryOpenFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch
        {
            // The document is already saved; opening it is only a convenience.
        }
    }

    private ArsefTemplateModel SelectedModel()
    {
        return modelBox.SelectedItem as ArsefTemplateModel
            ?? throw new InvalidOperationException("Aucun modèle n'est sélectionné.");
    }

    private void RestorePendingSession()
    {
        var session = ReadActiveSession();
        if (session is null) return;
        if (!File.Exists(session.DocxPath))
        {
            ClearActiveSession();
            return;
        }

        activeSession = session;
        documentFinishedButton.Enabled = true;
        ApplySessionToFields(session);
        var answer = MessageBox.Show(
            "Un document n'est pas terminé :\r\n\r\n" + session.Code + "\r\n" + session.DocxPath + "\r\n\r\nVoulez-vous reprendre cette session ?",
            "Document en cours", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer == DialogResult.Yes)
        {
            ApplySessionToFields(session);
            status.Text = "Session reprise. Complétez le document Word, enregistrez-le, puis cliquez sur « Document fini ».";
            TryOpenFile(session.DocxPath);
            SelectInExplorer(session.DocxPath);
        }
        else
        {
            status.Text = "Session conservée : cliquez sur « Document fini » quand le document est prêt.\r\n" + session.OutputFolder;
        }
    }

    private void ApplySessionToFields(ActiveDocumentSession session)
    {
        modelBox.SelectedItem = TemplateCatalog.Models.FirstOrDefault(x => x.Code.Equals(session.TemplateCode, StringComparison.OrdinalIgnoreCase)) ?? modelBox.SelectedItem;
        titleBox.Text = session.Title;
        recipientBox.Text = session.EmailRecipient;
        codeWordBox.Text = session.DocumentCode;
        versionBox.Text = session.Version;
        authorBox.Text = session.Author;
        dateBox.Value = session.ValidityDate < dateBox.MinDate ? dateBox.MinDate : session.ValidityDate > dateBox.MaxDate ? dateBox.MaxDate : session.ValidityDate;
        typeBox.SelectedItem = ArsefRules.Types.FirstOrDefault(x => x.Code.Equals(session.TypeCode, StringComparison.OrdinalIgnoreCase)) ?? typeBox.SelectedItem;
        domainBox.SelectedItem = ArsefRules.Domains.FirstOrDefault(x => x.Code.Equals(session.DomainCode, StringComparison.OrdinalIgnoreCase)) ?? domainBox.SelectedItem;
        serviceBox.SelectedItem = ArsefRules.Services.FirstOrDefault(x => x.Code.Equals(session.ServiceCode, StringComparison.OrdinalIgnoreCase)) ?? serviceBox.SelectedItem;
        ApplySelectionRules();
        UpdatePreview();
    }

    private void LoadActiveSession()
    {
        var session = ReadActiveSession();
        if (session is not null && File.Exists(session.DocxPath))
        {
            activeSession = session;
            documentFinishedButton.Enabled = true;
        }
    }

    private static ActiveDocumentSession? ReadActiveSession()
    {
        try
        {
            if (!File.Exists(AppPaths.ActiveSessionPath)) return null;
            var session = JsonSerializer.Deserialize<ActiveDocumentSession>(File.ReadAllText(AppPaths.ActiveSessionPath));
            if (session is null || !SessionIsSafe(session))
                throw new InvalidDataException("La session Cartouche mémorisée contient un chemin invalide.");
            return session;
        }
        catch (Exception error) when (error is JsonException or NotSupportedException or InvalidDataException or ArgumentException)
        {
            CrashReporter.Write(error);
            try
            {
                var preserved = AppPaths.ActiveSessionPath + $".corrompu-{DateTime.Now:yyyyMMdd-HHmmss}";
                File.Move(AppPaths.ActiveSessionPath, preserved, false);
            }
            catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException) { }
            return null;
        }
    }

    private void SaveActiveSession()
    {
        if (activeSession is null) return;
        Directory.CreateDirectory(AppPaths.DataRoot);
        AtomicFile.WriteAllText(AppPaths.ActiveSessionPath, JsonSerializer.Serialize(activeSession, JsonOptions));
        VaultSession.TrySyncToVault();
    }

    private void ClearActiveSession()
    {
        activeSession = null;
        documentFinishedButton.Enabled = false;
        try { if (File.Exists(AppPaths.ActiveSessionPath)) File.Delete(AppPaths.ActiveSessionPath); } catch { }
        VaultSession.TrySyncToVault();
    }

    private void LoadSettings()
    {
        settingsPath = AppPaths.SettingsPath;
        arsefRoot = DesktopArsefRoot();
        try
        {
            if (File.Exists(settingsPath))
            {
                var settings = JsonSerializer.Deserialize<ArsefSettings>(File.ReadAllText(settingsPath));
                if (!string.IsNullOrWhiteSpace(settings?.Author)) authorBox.Text = settings.Author;
            }
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            CrashReporter.Write(error);
        }
    }

    private static string DesktopArsefRoot()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop)) desktop = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(desktop, ArsefRules.RootFolderName);
    }

    private void SaveSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        AtomicFile.WriteAllText(settingsPath, JsonSerializer.Serialize(new ArsefSettings(DesktopArsefRoot(), authorBox.Text.Trim()), JsonOptions));
        VaultSession.TrySyncToVault();
    }

    private static bool SessionIsSafe(ActiveDocumentSession session)
    {
        try
        {
            var expected = ArsefRules.CreatePlan(session.ToInput(), DesktopArsefRoot());
            return session.Code.Equals(expected.Code, StringComparison.Ordinal) &&
                   Path.GetFullPath(session.OutputFolder).Equals(Path.GetFullPath(expected.OutputFolder), StringComparison.OrdinalIgnoreCase) &&
                   Path.GetFullPath(session.DocxPath).Equals(Path.GetFullPath(expected.DocxPath), StringComparison.OrdinalIgnoreCase) &&
                   Path.GetFullPath(session.PdfPath).Equals(Path.GetFullPath(expected.PdfPath), StringComparison.OrdinalIgnoreCase) &&
                   TemplateCatalog.Models.Any(model => model.Code.Equals(session.TemplateCode, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        operationRunning = busy;
        createButton.Enabled = !busy;
        documentFinishedButton.Enabled = !busy && activeSession is not null;
        UseWaitCursor = busy;
        if (!string.IsNullOrWhiteSpace(message)) status.Text = message;
    }

    private void ShowError(Exception ex)
    {
        status.Text = "Échec. Aucun succès n'est annoncé tant que les fichiers ne sont pas vérifiés.";
        MessageBox.Show(ex.Message, "Diva – action impossible", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    internal void SetStartupStatus(string text)
    {
        status.Text = text;
    }

    private sealed record ArsefSettings(string ArsefRoot, string Author = "");

    private sealed record ActiveDocumentSession(
        string Code,
        string DomainFolder,
        string TypeFolder,
        string ServiceFolder,
        string OutputFolder,
        string DocxPath,
        string PdfPath,
        string TemplateCode,
        string Title,
        string TypeCode,
        string DomainCode,
        string ServiceCode,
        string DocumentCode,
        string Version,
        string Author,
        DateTime ValidityDate,
        string EmailSubject,
        string EmailRecipient,
        string? RegistryPath = null)
    {
        public static ActiveDocumentSession From(ArsefInput input, ArsefPlan plan, string templateCode) => new(
            plan.Code, plan.DomainFolder, plan.TypeFolder, plan.ServiceFolder, plan.OutputFolder, plan.DocxPath, plan.PdfPath,
            templateCode, input.Title, input.TypeCode, input.DomainCode, input.ServiceCode, input.DocumentCode, input.Version,
            input.Author, input.ValidityDate, input.EmailSubject, input.EmailRecipient);

        public ArsefInput ToInput() => new(Title, TypeCode, DomainCode, ServiceCode, DocumentCode, Version, Author, ValidityDate)
        {
            EmailSubject = EmailSubject,
            EmailRecipient = EmailRecipient
        };

        public ArsefPlan ToPlan() => new(Code, DomainFolder, TypeFolder, ServiceFolder, OutputFolder, DocxPath, PdfPath);
    }
}

internal static class OneDriveArsefCopy
{
    public static string Copy(ArsefPlan plan)
    {
        ValidateSource(plan.DocxPath);
        ValidateSource(plan.PdfPath);

        var oneDrive = FindOneDriveRoot();
        var sharedRoot = Path.Combine(oneDrive, "ARSEF", "Desktop", ArsefRules.RootFolderName);
        ArsefRules.PrepareFixedFolders(sharedRoot);

        var desktopRoot = Path.GetFullPath(Path.Combine(ArsefRules.DetectDesktopRoot(), ArsefRules.RootFolderName));
        var relative = Path.GetRelativePath(desktopRoot, Path.GetFullPath(plan.OutputFolder));
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new InvalidDataException("Le document n’est pas situé dans le dossier ARSEF du Bureau.");

        var destination = Path.GetFullPath(Path.Combine(sharedRoot, relative));
        var rootBoundary = Path.GetFullPath(sharedRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(rootBoundary, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Le chemin OneDrive calculé est invalide.");

        CopyPair(plan.DocxPath, plan.PdfPath, destination);
        return destination;
    }

    private static void CopyPair(string sourceDocx, string sourcePdf, string destination)
    {
        Directory.CreateDirectory(destination);
        var targetDocx = Path.Combine(destination, Path.GetFileName(sourceDocx));
        var targetPdf = Path.Combine(destination, Path.GetFileName(sourcePdf));
        if (File.Exists(targetDocx) || File.Exists(targetPdf))
        {
            if (SameFile(sourceDocx, targetDocx) && SameFile(sourcePdf, targetPdf)) return;
            throw new IOException(
                "Un document portant cette codification existe déjà dans OneDrive ARSEF. " +
                "Aucun fichier n’a été remplacé. Changez la version ou le mot-clé si nécessaire.");
        }

        var temporaryDocx = targetDocx + ".diva-" + Guid.NewGuid().ToString("N") + ".tmp";
        var temporaryPdf = targetPdf + ".diva-" + Guid.NewGuid().ToString("N") + ".tmp";
        var docxCreated = false;
        try
        {
            File.Copy(sourceDocx, temporaryDocx, false);
            File.Copy(sourcePdf, temporaryPdf, false);
            File.Move(temporaryDocx, targetDocx, false);
            docxCreated = true;
            File.Move(temporaryPdf, targetPdf, false);
            if (!SameFile(sourceDocx, targetDocx) || !SameFile(sourcePdf, targetPdf))
                throw new IOException("La vérification de la copie OneDrive a échoué.");
        }
        catch
        {
            if (docxCreated && !File.Exists(targetPdf)) TryDelete(targetDocx);
            throw;
        }
        finally
        {
            TryDelete(temporaryDocx);
            TryDelete(temporaryPdf);
        }
    }

    private static string FindOneDriveRoot()
    {
        foreach (var name in new[] { "OneDriveCommercial", "OneDrive", "OneDriveConsumer" })
        {
            var path = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) return Path.GetFullPath(path);
        }
        throw new DirectoryNotFoundException("OneDrive est introuvable sur ce PC. Vérifiez qu’il est connecté, puis réessayez.");
    }

    private static void ValidateSource(string path)
    {
        if (!DocumentHistory.IsSafeDocumentPath(path) || !File.Exists(path) || new FileInfo(path).Length == 0)
            throw new FileNotFoundException("Le document à copier est introuvable ou vide.", path);
    }

    private static bool SameFile(string source, string target)
    {
        if (!File.Exists(target) || new FileInfo(source).Length != new FileInfo(target).Length) return false;
        using var left = File.OpenRead(source);
        using var right = File.OpenRead(target);
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(left), SHA256.HashData(right));
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    internal static void SelfCheck()
    {
        var root = Path.Combine(Path.GetTempPath(), "DivaOneDriveCopyCheck-" + Guid.NewGuid().ToString("N"));
        try
        {
            var source = Path.Combine(root, "source");
            var destination = Path.Combine(root, "destination");
            Directory.CreateDirectory(source);
            var docx = Path.Combine(source, "test.docx");
            var pdf = Path.Combine(source, "test.pdf");
            File.WriteAllText(docx, "docx");
            File.WriteAllText(pdf, "pdf");
            CopyPair(docx, pdf, destination);
            CopyPair(docx, pdf, destination);
            File.WriteAllText(docx, "changed");
            try { CopyPair(docx, pdf, destination); throw new InvalidOperationException("OneDrive collision check failed."); }
            catch (IOException) { }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
