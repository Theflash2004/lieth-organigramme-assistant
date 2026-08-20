using AssistantArsef;
using System.Diagnostics;

namespace LiethOrganigrammeAssistant;

internal sealed class MainForm : Form
{
    private readonly Label synchronization = new();
    private readonly string? postUpdateMarker;
    private readonly Dictionary<Type, Form> openModules = new();
    private bool exitingForUpdate;
    public bool LogoutRequested { get; private set; }

    public MainForm(string? postUpdateMarker)
    {
        this.postUpdateMarker = postUpdateMarker;
        Text = "Diva Assistant";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1120, 760);
        MinimumSize = new Size(920, 650);
        Font = DivaTheme.UiFont;
        BackColor = DivaTheme.Background;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        BuildUi();
        FormClosing += OnFormClosing;
        Shown += OnShown;
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(30),
            BackColor = DivaTheme.Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            Margin = new Padding(0, 0, 0, 26)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.Controls.Add(header, 0, 0);

        header.Controls.Add(new PictureBox
        {
            Image = LoadCatLogo(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(64, 64),
            Margin = new Padding(0, 0, 14, 0)
        }, 0, 0);

        var headings = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, RowCount = 2, Margin = Padding.Empty };
        headings.Controls.Add(new Label
        {
            Text = "Diva Assistant",
            AutoSize = true,
            Font = new Font("Segoe UI", 22F, FontStyle.Bold),
            ForeColor = DivaTheme.PurpleDark,
            Margin = Padding.Empty
        });
        headings.Controls.Add(new Label
        {
            Text = "Vos outils ARSEF réunis au même endroit",
            AutoSize = true,
            ForeColor = DivaTheme.Muted,
            Margin = new Padding(2, 2, 0, 0)
        });
        header.Controls.Add(headings, 1, 0);

        var account = new TableLayoutPanel { AutoSize = true, RowCount = 2, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        account.Controls.Add(new Label
        {
            Text = VaultSession.Username,
            AutoSize = true,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = DivaTheme.Text,
            TextAlign = ContentAlignment.MiddleRight,
            Anchor = AnchorStyles.Right
        });
        account.Controls.Add(new Label
        {
            Text = VaultSession.Role,
            AutoSize = true,
            ForeColor = DivaTheme.Muted,
            TextAlign = ContentAlignment.MiddleRight,
            Anchor = AnchorStyles.Right
        });
        header.Controls.Add(account, 2, 0);

        var cards = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            BackColor = DivaTheme.Background
        };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        cards.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        cards.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        root.Controls.Add(cards, 0, 1);

        cards.Controls.Add(ModuleCard(
            "Cartouche et documents",
            "Créez le document Word, classez-le sur le Bureau, reprenez une session et exportez le PDF final.",
            "Ouvrir Cartouche",
            () => OpenModule(() => new ArsefForm())), 0, 0);

        cards.Controls.Add(ModuleCard(
            "Organigrammes",
            "Construisez des logigrammes, reliez les étapes, retrouvez l’historique et exportez une image PNG.",
            "Ouvrir Organigrammes",
            () => OpenModule(() => new FlowchartForm())), 1, 0);

        cards.Controls.Add(ModuleCard(
            "Diva Productivité",
            "Confiez une mission, préparez un e-mail et ajoutez l’échéance au calendrier sans envoi automatique.",
            "Ouvrir Productivité",
            () => OpenModule(() => new ProductivityForm())), 0, 1);

        if (VaultSession.IsDirectrice)
        {
            cards.Controls.Add(ModuleCard(
                "Utilisateurs Diva",
                "Créez les comptes, attribuez les fonctions, réinitialisez un mot de passe et conservez la clé de récupération.",
                "Gérer les utilisateurs",
                () => OpenModule(() => new UserManagementForm())), 1, 1);
        }
        else
        {
            cards.Controls.Add(ModuleCard(
                "Compte sécurisé",
                "Vos données de travail sont enregistrées localement et leur copie OneDrive est chiffrée.",
                "Synchroniser maintenant",
                SynchronizeNow), 1, 1);
        }

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 20, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        synchronization.Text = "Prêt.";
        synchronization.AutoSize = true;
        synchronization.ForeColor = DivaTheme.Muted;
        synchronization.Anchor = AnchorStyles.Left;
        footer.Controls.Add(synchronization, 0, 0);
        var sync = DivaTheme.SecondaryButton("Synchroniser");
        sync.MinimumSize = new Size(120, 36);
        sync.Click += (_, _) => SynchronizeNow();
        footer.Controls.Add(sync, 1, 0);
        var supportMenu = new ContextMenuStrip();
        supportMenu.Items.Add("E-mail : liethavid@gmail.com", null, (_, _) => OpenSupportLink("mailto:liethavid@gmail.com"));
        supportMenu.Items.Add("WhatsApp : +33 7 44 22 45 41", null, (_, _) => OpenSupportLink("https://wa.me/33744224541"));
        var support = DivaTheme.SecondaryButton("Support");
        support.MinimumSize = new Size(100, 36);
        support.Margin = new Padding(8, 0, 0, 0);
        support.Click += (_, _) => supportMenu.Show(support, new Point(0, support.Height));
        footer.Controls.Add(support, 2, 0);
        var logout = DivaTheme.SecondaryButton("Se déconnecter");
        logout.MinimumSize = new Size(130, 36);
        logout.Margin = new Padding(8, 0, 0, 0);
        logout.Click += (_, _) =>
        {
            LogoutRequested = true;
            Close();
        };
        footer.Controls.Add(logout, 3, 0);
        root.Controls.Add(footer, 0, 2);
    }

    private static Panel ModuleCard(string title, string description, string actionText, Action action)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Margin = new Padding(8),
            Padding = new Padding(24),
            BorderStyle = BorderStyle.FixedSingle
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.Controls.Add(layout);
        layout.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            ForeColor = DivaTheme.PurpleDark,
            Margin = new Padding(0, 0, 0, 10)
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = description,
            AutoSize = true,
            MaximumSize = new Size(410, 0),
            ForeColor = DivaTheme.Muted
        }, 0, 1);
        var button = DivaTheme.PrimaryButton(actionText);
        button.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        button.Click += (_, _) => action();
        layout.Controls.Add(button, 0, 2);
        return card;
    }

    private void OpenModule<T>(Func<T> factory) where T : Form
    {
        if (openModules.TryGetValue(typeof(T), out var existing) && !existing.IsDisposed)
        {
            if (existing.WindowState == FormWindowState.Minimized) existing.WindowState = FormWindowState.Normal;
            existing.Show();
            existing.Activate();
            return;
        }

        var form = factory();
        openModules[typeof(T)] = form;
        form.FormClosed += (_, _) => openModules.Remove(typeof(T));
        form.Show(this);
    }

    private async void OnShown(object? sender, EventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(postUpdateMarker))
        {
            UpdateService.MarkHealthy(postUpdateMarker);
            synchronization.Text = "Diva Assistant a été mis à jour avec succès.";
            MessageBox.Show(this, "Diva Assistant est à jour.", "Mise à jour terminée", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        await UpdateService.CheckForUpdateAsync(this);
    }

    private void SynchronizeNow()
    {
        synchronization.Text = VaultSession.TrySyncToVault()
            ? "Synchronisation terminée."
            : "OneDrive est indisponible. Vos données restent enregistrées sur ce PC.";
    }

    private void OpenSupportLink(string address)
    {
        try { Process.Start(new ProcessStartInfo(address) { UseShellExecute = true }); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, "Impossible d’ouvrir ce contact sur ce PC.", "Support Diva", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        foreach (var module in openModules.Values.Where(module => !module.IsDisposed).ToArray())
            module.Close();
        if (openModules.Values.Any(module => !module.IsDisposed))
        {
            e.Cancel = true;
            return;
        }

        if (!exitingForUpdate) SynchronizeNow();
    }

    public void ExitForUpdate()
    {
        exitingForUpdate = true;
        BeginInvoke(new Action(Close));
    }

    private static Bitmap? LoadCatLogo()
    {
        using var source = typeof(MainForm).Assembly.GetManifestResourceStream("AssistantArsef.Assets.diva-cat-logo.png");
        if (source is null) return null;
        using var image = Image.FromStream(source);
        return new Bitmap(image);
    }
}
