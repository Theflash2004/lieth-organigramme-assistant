namespace LiethOrganigrammeAssistant;

internal sealed class LoginForm : Form
{
    private readonly TextBox folder = new() { ReadOnly = true };
    private readonly TextBox username = new();
    private readonly TextBox password = new() { UseSystemPasswordChar = true };
    private readonly TextBox confirmation = new() { UseSystemPasswordChar = true };
    private readonly Label title = new() { AutoSize = true, Font = new Font("Segoe UI", 15F, FontStyle.Bold), ForeColor = Color.FromArgb(112, 48, 160) };
    private readonly Label confirmationLabel = new() { Text = "Confirmer le mot de passe", AutoSize = true };
    private readonly Button submit = PrimaryButton("");
    private readonly Button recover = SecondaryButton("Récupérer le compte Directrice");
    private bool setupMode;
    private bool vaultUnavailable;

    public LoginForm()
    {
        Text = "Connexion Diva";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(540, 520);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 10F);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(24), ColumnCount = 1 };
        Controls.Add(layout);
        layout.Controls.Add(title);
        layout.Controls.Add(new Label { Text = "Les données restent sur ce PC et une copie chiffrée est synchronisée dans le OneDrive commun.", AutoSize = true, MaximumSize = new Size(485, 0), Margin = new Padding(0, 6, 0, 14) });
        AddField(layout, "Dossier Diva partagé", folder);
        var browse = SecondaryButton("Choisir un autre dossier…");
        browse.Click += (_, _) => Browse();
        layout.Controls.Add(browse);
        AddField(layout, "Identifiant donné par la Directrice", username);
        AddField(layout, "Mot de passe", password);
        layout.Controls.Add(confirmationLabel);
        layout.Controls.Add(confirmation);
        submit.Click += (_, _) => Submit();
        submit.Margin = new Padding(0, 16, 0, 6);
        layout.Controls.Add(submit);
        recover.Click += (_, _) => Recover();
        layout.Controls.Add(recover);
        AcceptButton = submit;

        folder.Text = VaultSession.DefaultSharedFolder;
        RefreshMode();
    }

    private void Browse()
    {
        using var dialog = new FolderBrowserDialog { Description = "Choisissez le dossier Diva Productivite dans le OneDrive commun", UseDescriptionForTitle = true, SelectedPath = Directory.Exists(folder.Text) ? folder.Text : "" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        folder.Text = dialog.SelectedPath;
        RefreshMode();
    }

    private void RefreshMode()
    {
        var hasAccounts = folder.Text.Length > 0 && VaultSession.HasAccounts(folder.Text);
        vaultUnavailable = folder.Text.Length > 0 && !hasAccounts && VaultSession.HasSavedLocation;
        setupMode = folder.Text.Length > 0 && !hasAccounts && !vaultUnavailable;
        title.Text = vaultUnavailable ? "Coffre Diva indisponible" : setupMode ? "Première configuration — Directrice" : "Connexion à Diva";
        submit.Text = vaultUnavailable ? "Réessayer" : setupMode ? "Créer le coffre Diva" : "Se connecter";
        confirmation.Visible = confirmationLabel.Visible = setupMode;
        recover.Visible = hasAccounts;
    }

    private void Submit()
    {
        if (folder.Text.Length == 0)
        {
            MessageBox.Show(this, "Le dossier OneDrive commun est introuvable. Choisissez-le une seule fois.", "Dossier Diva", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            RefreshMode();
            if (vaultUnavailable)
                throw new InvalidOperationException("Le coffre configuré n’est pas disponible. Attendez la synchronisation OneDrive ou choisissez le dossier Diva Productivite existant.");
            if (setupMode)
            {
                if (password.Text != confirmation.Text) throw new InvalidOperationException("Les mots de passe ne correspondent pas.");
                var recoveryKey = VaultSession.CreateDirectrice(folder.Text, username.Text, password.Text);
                using var recovery = new RecoveryKeyForm(recoveryKey);
                recovery.ShowDialog(this);
            }
            else
            {
                if (!VaultSession.Login(folder.Text, username.Text, password.Text))
                    throw new InvalidOperationException("Identifiant ou mot de passe incorrect.");
                if (VaultSession.Account!.MustChangePassword)
                {
                    using var change = new NewPasswordForm("Choisir votre nouveau mot de passe");
                    if (change.ShowDialog(this) != DialogResult.OK) return;
                    VaultSession.ChangePassword(change.PasswordValue);
                }
            }
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException or System.Security.Cryptography.CryptographicException or FormatException)
        {
            MessageBox.Show(this, error.Message, "Connexion Diva impossible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Recover()
    {
        using var recovery = new DirectriceRecoveryForm(folder.Text);
        if (recovery.ShowDialog(this) != DialogResult.OK) return;
        DialogResult = DialogResult.OK;
        Close();
    }

    internal static void AddField(TableLayoutPanel panel, string label, Control input)
    {
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 8, 0, 3) });
        input.Dock = DockStyle.Top;
        panel.Controls.Add(input);
    }

    internal static Button PrimaryButton(string text) => new() { Text = text, Height = 38, Dock = DockStyle.Top, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(112, 48, 160), ForeColor = Color.White };
    internal static Button SecondaryButton(string text) => new() { Text = text, Height = 34, AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = Color.FromArgb(85, 35, 125), BackColor = Color.White };
}

internal sealed class NewPasswordForm : Form
{
    private readonly TextBox password = new() { UseSystemPasswordChar = true };
    private readonly TextBox confirmation = new() { UseSystemPasswordChar = true };
    public string PasswordValue => password.Text;

    public NewPasswordForm(string title)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(430, 245);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        Font = new Font("Segoe UI", 10F);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1 };
        Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "Utilisez au moins 14 caractères.", AutoSize = true, ForeColor = Color.FromArgb(85, 35, 125) });
        LoginForm.AddField(layout, "Nouveau mot de passe", password);
        LoginForm.AddField(layout, "Confirmer", confirmation);
        var save = LoginForm.PrimaryButton("Enregistrer le mot de passe");
        save.Margin = new Padding(0, 14, 0, 0);
        save.Click += (_, _) => SavePassword();
        layout.Controls.Add(save);
        AcceptButton = save;
    }

    private void SavePassword()
    {
        if (password.Text.Length < 14 || password.Text != confirmation.Text)
        {
            MessageBox.Show(this, "Le mot de passe doit contenir au moins 14 caractères et les deux saisies doivent correspondre.", "Mot de passe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}

internal sealed class RecoveryKeyForm : Form
{
    public RecoveryKeyForm(string key)
    {
        Text = "Clé de récupération Directrice";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(580, 280);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        Font = new Font("Segoe UI", 10F);
        var value = new TextBox { Text = key, ReadOnly = true, Multiline = true, Dock = DockStyle.Top, Height = 72 };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1 };
        Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "Conservez cette clé hors du OneDrive commun (papier ou clé USB). Elle permet de récupérer le compte Directrice.", AutoSize = true, MaximumSize = new Size(530, 0), Margin = new Padding(0, 0, 0, 12) });
        layout.Controls.Add(value);
        var copy = LoginForm.SecondaryButton("Copier la clé");
        copy.Click += (_, _) => Clipboard.SetText(key);
        layout.Controls.Add(copy);
        var done = LoginForm.PrimaryButton("J’ai conservé la clé");
        done.Margin = new Padding(0, 12, 0, 0);
        done.Click += (_, _) => Close();
        layout.Controls.Add(done);
    }
}

internal sealed class DirectriceRecoveryForm : Form
{
    private readonly string folder;
    private readonly TextBox key = new() { Multiline = true, Height = 55 };
    private readonly TextBox username = new();
    private readonly TextBox password = new() { UseSystemPasswordChar = true };
    private readonly TextBox confirmation = new() { UseSystemPasswordChar = true };

    public DirectriceRecoveryForm(string folder)
    {
        this.folder = folder;
        Text = "Récupérer le compte Directrice";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(500, 405);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        Font = new Font("Segoe UI", 10F);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1 };
        Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "La clé hors ligne permet de choisir un nouvel identifiant et un nouveau mot de passe sans perdre les données.", AutoSize = true, MaximumSize = new Size(450, 0) });
        LoginForm.AddField(layout, "Clé de récupération", key);
        LoginForm.AddField(layout, "Nouvel identifiant Directrice", username);
        LoginForm.AddField(layout, "Nouveau mot de passe", password);
        LoginForm.AddField(layout, "Confirmer", confirmation);
        var save = LoginForm.PrimaryButton("Récupérer le compte");
        save.Margin = new Padding(0, 14, 0, 0);
        save.Click += (_, _) => Recover();
        layout.Controls.Add(save);
        AcceptButton = save;
    }

    private void Recover()
    {
        try
        {
            if (password.Text != confirmation.Text) throw new InvalidOperationException("Les mots de passe ne correspondent pas.");
            VaultSession.RecoverDirectrice(folder, key.Text, username.Text, password.Text);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException or System.Security.Cryptography.CryptographicException or FormatException)
        {
            MessageBox.Show(this, error.Message, "Récupération impossible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

internal sealed class UserManagementForm : Form
{
    private readonly ListView users = new() { View = View.Details, FullRowSelect = true, GridLines = true, Dock = DockStyle.Fill };
    private readonly TextBox username = new();
    private readonly ComboBox role = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox customRole = new() { Visible = false, PlaceholderText = "Saisissez la fonction" };
    private readonly TextBox temporaryPassword = new() { UseSystemPasswordChar = true };

    public UserManagementForm()
    {
        Text = "Utilisateurs Diva — Directrice";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(900, 530);
        MinimumSize = new Size(760, 470);
        Font = new Font("Segoe UI", 10F);
        role.Items.AddRange(["Responsable du secteur SAD", "IDEC SSIAD"]);
        role.SelectedIndex = 0;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(root);
        var creation = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(0, 0, 18, 0) };
        root.Controls.Add(creation, 0, 0);
        creation.Controls.Add(new Label { Text = "Créer un utilisateur", AutoSize = true, Font = new Font(Font, FontStyle.Bold), ForeColor = Color.FromArgb(112, 48, 160) });
        LoginForm.AddField(creation, "Identifiant choisi par la Directrice", username);
        LoginForm.AddField(creation, "Fonction", role);
        var otherRole = LoginForm.SecondaryButton("Autre…");
        otherRole.Click += (_, _) =>
        {
            customRole.Visible = !customRole.Visible;
            role.Enabled = !customRole.Visible;
            if (customRole.Visible) customRole.Focus();
        };
        creation.Controls.Add(otherRole);
        creation.Controls.Add(customRole);
        LoginForm.AddField(creation, "Mot de passe temporaire (14 caractères)", temporaryPassword);
        var create = LoginForm.PrimaryButton("Créer le compte");
        create.Margin = new Padding(0, 14, 0, 0);
        create.Click += (_, _) => CreateUser();
        creation.Controls.Add(create);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(right, 1, 0);
        right.Controls.Add(new Label { Text = "Comptes et identifiants", AutoSize = true, Font = new Font(Font, FontStyle.Bold), ForeColor = Color.FromArgb(112, 48, 160), Margin = new Padding(0, 0, 0, 8) }, 0, 0);
        users.Columns.AddRange([new ColumnHeader { Text = "Identifiant", Width = 180 }, new ColumnHeader { Text = "Fonction", Width = 220 }, new ColumnHeader { Text = "État", Width = 145 }]);
        right.Controls.Add(users, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 10, 0, 0) };
        var reset = LoginForm.SecondaryButton("Nouveau mot de passe temporaire");
        reset.Click += (_, _) => ResetPassword();
        actions.Controls.Add(reset);
        var recovery = LoginForm.SecondaryButton("Afficher la clé de récupération");
        recovery.Click += (_, _) => new RecoveryKeyForm(VaultSession.GetRecoveryKey()).ShowDialog(this);
        actions.Controls.Add(recovery);
        right.Controls.Add(actions, 0, 2);
        RefreshUsers();
    }

    private void CreateUser()
    {
        try
        {
            var selectedRole = customRole.Visible ? customRole.Text.Trim() : role.Text;
            VaultSession.CreateUser(username.Text, selectedRole, temporaryPassword.Text);
            MessageBox.Show(this, $"Compte {username.Text.Trim()} créé. Donnez cet identifiant et le mot de passe temporaire à la personne.", "Compte créé", MessageBoxButtons.OK, MessageBoxIcon.Information);
            username.Clear();
            temporaryPassword.Clear();
            customRole.Clear();
            customRole.Visible = false;
            role.Enabled = true;
            RefreshUsers();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException or System.Security.Cryptography.CryptographicException or FormatException)
        {
            MessageBox.Show(this, error.Message, "Création impossible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ResetPassword()
    {
        if (users.SelectedItems.Count != 1)
        {
            MessageBox.Show(this, "Sélectionnez d’abord un compte.", "Utilisateurs Diva", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var account = (VaultAccount)users.SelectedItems[0].Tag!;
        using var dialog = new NewPasswordForm($"Mot de passe temporaire — {account.Username}");
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            VaultSession.ResetPassword(account.Id, dialog.PasswordValue);
            MessageBox.Show(this, "Mot de passe temporaire enregistré. La personne devra le remplacer à sa prochaine connexion.", "Mot de passe réinitialisé", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshUsers();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException or System.Security.Cryptography.CryptographicException or FormatException)
        {
            MessageBox.Show(this, error.Message, "Réinitialisation impossible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RefreshUsers()
    {
        users.Items.Clear();
        try
        {
            foreach (var account in VaultSession.ListAccounts())
            {
                var item = new ListViewItem(account.Username) { Tag = account };
                item.SubItems.Add(account.Role);
                item.SubItems.Add(account.MustChangePassword ? "Mot de passe temporaire" : "Actif");
                users.Items.Add(item);
                if (!account.Role.Equals("Directrice", StringComparison.OrdinalIgnoreCase) && !role.Items.Contains(account.Role))
                    role.Items.Add(account.Role);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException or System.Security.Cryptography.CryptographicException or FormatException)
        {
            MessageBox.Show(this, error.Message, "Lecture des utilisateurs impossible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
