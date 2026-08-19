using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace LiethOrganigrammeAssistant;

internal sealed class ProductivityForm : Form
{
    private static readonly string[] Roles = ["Directrice", "Responsable du secteur SAD", "IDEC SSIAD"];
    private readonly ComboBox managerRole = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox recipientName = new();
    private readonly TextBox recipientEmail = new();
    private readonly TextBox task = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 110 };
    private readonly DateTimePicker dueAt = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dddd dd MMMM yyyy à HH:mm", ShowUpDown = true };
    private readonly ListView history = new() { View = View.Details, FullRowSelect = true, GridLines = true, Dock = DockStyle.Fill };
    private readonly Label status = new() { AutoSize = true };

    public ProductivityForm()
    {
        Text = "Diva Productivité";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1040, 680);
        MinimumSize = new Size(850, 560);
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.White;
        managerRole.Items.AddRange(Roles);
        managerRole.SelectedIndex = 0;
        dueAt.Value = DateTime.Now.AddDays(1);
        BuildUi();
        RefreshHistory();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(18), BackColor = Color.White };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var entry = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true };
        root.Controls.Add(entry, 0, 0);
        entry.Controls.Add(new Label { Text = "Diva Productivité", AutoSize = true, Font = new Font(Font, FontStyle.Bold), ForeColor = Color.FromArgb(112, 48, 160), Margin = new Padding(0, 0, 0, 6) });
        entry.Controls.Add(new Label { Text = "Créez une mission, conservez son historique et préparez l’e-mail ou l’événement calendrier.", AutoSize = true, MaximumSize = new Size(340, 0), Margin = new Padding(0, 0, 0, 16) });
        AddField(entry, "Fonction responsable", managerRole);
        AddField(entry, "Destinataire", recipientName);
        AddField(entry, "E-mail du destinataire", recipientEmail);
        AddField(entry, "Tâche ou mission", task);
        AddField(entry, "Échéance", dueAt);
        var add = new Button { Text = "Ajouter la mission", Height = 38, Dock = DockStyle.Top, BackColor = Color.FromArgb(112, 48, 160), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 12, 0, 6) };
        add.Click += (_, _) => AddMission();
        entry.Controls.Add(add);
        status.ForeColor = Color.FromArgb(70, 70, 70);
        status.MaximumSize = new Size(340, 0);
        entry.Controls.Add(status);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(18, 0, 0, 0) };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(right, 1, 0);
        right.Controls.Add(new Label { Text = "Historique des missions", AutoSize = true, Font = new Font(Font, FontStyle.Bold), ForeColor = Color.FromArgb(112, 48, 160), Margin = new Padding(0, 0, 0, 8) }, 0, 0);
        history.Columns.AddRange([new ColumnHeader { Text = "Échéance", Width = 145 }, new ColumnHeader { Text = "Responsable", Width = 155 }, new ColumnHeader { Text = "Destinataire", Width = 170 }, new ColumnHeader { Text = "Mission", Width = 300 }]);
        right.Controls.Add(history, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 10, 0, 0) };
        actions.Controls.Add(ActionButton("Préparer l’e-mail", (_, _) => PrepareEmail()));
        actions.Controls.Add(ActionButton("Exporter calendrier (.ics)", (_, _) => ExportCalendar()));
        actions.Controls.Add(ActionButton("Actualiser", (_, _) => RefreshHistory()));
        right.Controls.Add(actions, 0, 2);
    }

    private static void AddField(TableLayoutPanel panel, string label, Control input)
    {
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 8, 0, 3) });
        input.Dock = DockStyle.Top;
        panel.Controls.Add(input);
    }

    private static Button ActionButton(string text, EventHandler action)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 36, FlatStyle = FlatStyle.Flat, ForeColor = Color.FromArgb(85, 35, 125), BackColor = Color.White };
        button.Click += action;
        return button;
    }

    private void AddMission()
    {
        if (string.IsNullOrWhiteSpace(recipientName.Text) || string.IsNullOrWhiteSpace(task.Text) || !IsEmail(recipientEmail.Text))
        {
            MessageBox.Show(this, "Indiquez le destinataire, une adresse e-mail valide et la mission.", "Mission incomplète", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        MissionHistory.Add(new Mission(Guid.NewGuid(), managerRole.Text, recipientName.Text.Trim(), recipientEmail.Text.Trim(), task.Text.Trim(), dueAt.Value, DateTime.Now));
        task.Clear();
        RefreshHistory();
        status.Text = "Mission ajoutée à l’historique local.";
    }

    private void RefreshHistory()
    {
        history.Items.Clear();
        foreach (var mission in MissionHistory.List().OrderBy(m => m.DueAt))
        {
            var item = new ListViewItem(mission.DueAt.ToString("dd/MM/yyyy HH:mm")) { Tag = mission };
            item.SubItems.Add(mission.ManagerRole);
            item.SubItems.Add($"{mission.RecipientName} ({mission.RecipientEmail})");
            item.SubItems.Add(mission.Task.ReplaceLineEndings(" "));
            history.Items.Add(item);
        }
    }

    private Mission? SelectedMission() => history.SelectedItems.Count == 1 ? history.SelectedItems[0].Tag as Mission : null;

    private void PrepareEmail()
    {
        var mission = SelectedMission();
        if (mission is null) { SelectMissionWarning(); return; }
        if (MessageBox.Show(this, "Autoriser l’ouverture de votre messagerie avec cet e-mail prérempli ? Vous pourrez le relire puis cliquer vous-même sur Envoyer.", "Autorisation e-mail", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        var subject = $"Mission à réaliser avant le {mission.DueAt:dd/MM/yyyy}";
        var body = $"Bonjour {mission.RecipientName},\r\n\r\n{mission.ManagerRole} vous confie la mission suivante :\r\n{mission.Task}\r\n\r\nÉchéance : {mission.DueAt:dddd dd MMMM yyyy à HH:mm}.\r\n\r\nCordialement,";
        try
        {
            Process.Start(new ProcessStartInfo($"mailto:{Uri.EscapeDataString(mission.RecipientEmail)}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}") { UseShellExecute = true });
            status.Text = "Brouillon d’e-mail ouvert : relisez-le puis cliquez sur Envoyer.";
        }
        catch (Win32Exception)
        {
            MessageBox.Show(this, "Aucune application de messagerie n’est configurée par défaut dans Windows.", "Messagerie indisponible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ExportCalendar()
    {
        var mission = SelectedMission();
        if (mission is null) { SelectMissionWarning(); return; }
        if (MessageBox.Show(this, "Autoriser la création d’un fichier calendrier pour cette mission ? Vous pourrez l’ouvrir avec Outlook, Google Calendar ou Apple Calendar.", "Autorisation calendrier", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        using var save = new SaveFileDialog { Filter = "Fichier calendrier (*.ics)|*.ics", FileName = $"mission-{mission.DueAt:yyyyMMdd}.ics" };
        if (save.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(save.FileName, ToCalendarEvent(mission), new UTF8Encoding(false));
        status.Text = "Événement calendrier créé : " + save.FileName;
    }

    private static string ToCalendarEvent(Mission mission)
    {
        static string Escape(string value) => value.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").ReplaceLineEndings("\\n");
        var start = mission.DueAt;
        var end = start.AddMinutes(30);
        return $"BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Lieth//Diva Productivite//FR\r\nBEGIN:VEVENT\r\nUID:{mission.Id}\r\nDTSTAMP:{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}\r\nDTSTART:{start:yyyyMMdd'T'HHmmss}\r\nDTEND:{end:yyyyMMdd'T'HHmmss}\r\nSUMMARY:{Escape("Mission : " + mission.Task)}\r\nDESCRIPTION:{Escape($"Responsable : {mission.ManagerRole}\nDestinataire : {mission.RecipientName} ({mission.RecipientEmail})\nÉchéance : {mission.DueAt:dd/MM/yyyy HH:mm}")}\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
    }

    private void SelectMissionWarning() => MessageBox.Show(this, "Sélectionnez une mission dans l’historique.", "Diva Productivité", MessageBoxButtons.OK, MessageBoxIcon.Information);
    private static bool IsEmail(string value) => System.Net.Mail.MailAddress.TryCreate(value.Trim(), out _);

    internal static void SelfCheck()
    {
        var mission = new Mission(Guid.NewGuid(), "Directrice", "Camille", "camille@example.org", "Réunion", new DateTime(2026, 1, 2, 9, 30, 0), DateTime.UtcNow);
        var calendar = ToCalendarEvent(mission);
        if (!calendar.Contains("DESCRIPTION:Responsable : Directrice\\nDestinataire") || calendar.Contains("Directrice\\\\nDestinataire"))
            throw new InvalidOperationException("Calendar check failed.");
    }
}
