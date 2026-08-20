namespace LiethOrganigrammeAssistant;

internal static class DivaTheme
{
    public static readonly Color Purple = Color.FromArgb(108, 49, 154);
    public static readonly Color PurpleDark = Color.FromArgb(73, 29, 106);
    public static readonly Color PurpleSoft = Color.FromArgb(241, 234, 248);
    public static readonly Color Background = Color.FromArgb(247, 246, 249);
    public static readonly Color Text = Color.FromArgb(42, 39, 47);
    public static readonly Color Muted = Color.FromArgb(100, 95, 106);
    public static readonly Font UiFont = new("Segoe UI", 10F);

    public static Button PrimaryButton(string text) => CreateButton(text, Purple, Color.White);

    public static Button SecondaryButton(string text) => CreateButton(text, Color.White, PurpleDark);

    private static Button CreateButton(string text, Color background, Color foreground)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(150, 40),
            Padding = new Padding(14, 0, 14, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = background,
            ForeColor = foreground,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = Purple;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = background == Color.White ? PurpleSoft : PurpleDark;
        return button;
    }
}
