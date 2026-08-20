namespace AssistantArsef;

internal sealed class ClasserSelectionDialog : Form
{
    private readonly CheckedListBox choices = new();

    public IReadOnlyList<string> SelectedValues => choices.CheckedItems.Cast<string>().ToArray();

    public ClasserSelectionDialog(IReadOnlyList<string> values)
    {
        Text = "Lieu de classement";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(460, 360);
        Size = new Size(560, 440);
        Font = new Font("Segoe UI", 10F);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        layout.Controls.Add(new Label
        {
            Text = "Où le document doit-il être trouvé ?\r\nVous pouvez sélectionner plusieurs classeurs ou emplacements.",
            AutoSize = true,
            ForeColor = Color.FromArgb(78, 35, 112),
            Margin = new Padding(0, 0, 0, 10)
        }, 0, 0);

        choices.Dock = DockStyle.Fill;
        choices.CheckOnClick = true;
        foreach (var value in values) choices.Items.Add(value);
        layout.Controls.Add(choices, 0, 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var ok = new Button { Text = "Valider", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Annuler", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 2);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}
