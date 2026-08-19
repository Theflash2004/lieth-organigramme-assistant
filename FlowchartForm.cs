using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace LiethOrganigrammeAssistant;

internal enum DiagramNodeKind
{
    Process,
    Decision,
    Terminator
}

internal sealed class DiagramNode
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Text { get; set; }
    public DiagramNodeKind Kind { get; set; }
    public RectangleF Bounds { get; set; }

    public DiagramNode(DiagramNodeKind kind, string text, RectangleF bounds)
    {
        Kind = kind;
        Text = text;
        Bounds = bounds;
    }
}

internal sealed record DiagramArrow(Guid From, Guid To);

internal sealed class DiagramModel
{
    public const int Width = 1460;
    public const int Height = 1622;
    public List<DiagramNode> Nodes { get; } = new();
    public List<DiagramArrow> Arrows { get; } = new();

    public DiagramNode Add(DiagramNodeKind kind, string text, RectangleF bounds)
    {
        var node = new DiagramNode(kind, text, bounds);
        Nodes.Add(node);
        return node;
    }

    public void Connect(DiagramNode from, DiagramNode to)
    {
        if (from.Id != to.Id && !Arrows.Any(a => a.From == from.Id && a.To == to.Id))
            Arrows.Add(new DiagramArrow(from.Id, to.Id));
    }
}

internal sealed class FlowchartForm : Form
{
    private readonly DiagramModel model = new();
    private readonly DiagramCanvas canvas;
    private readonly TextBox nodeText = new();
    private readonly ComboBox nodeKind = new();
    private readonly Label selectionLabel = new();
    private readonly Label status = new();
    private DiagramNode? selectedNode;
    private DiagramArrow? selectedArrow;
    private bool connectMode;
    private bool updatingInspector;

    public FlowchartForm()
    {
        Text = "Lieth Organigramme Assistant";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1420, 880);
        MinimumSize = new Size(1150, 700);
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.White;
        KeyPreview = true;
        KeyDown += HandleKeyDown;

        canvas = new DiagramCanvas(model);
        canvas.SelectionChanged += (_, _) =>
        {
            selectedNode = canvas.SelectedNode;
            selectedArrow = canvas.SelectedArrow;
            UpdateInspector();
        };
        canvas.ModelChanged += (_, _) => UpdateStatus();
        canvas.ArrowDragCompleted += (_, e) => HandleArrowDrag(e.From, e.To);
        BuildUi();
        UpdateInspector();
        UpdateStatus();
        Shown += async (_, _) => await UpdateService.CheckForUpdateAsync(this);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.White
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 475));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var sidebar = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(248, 246, 251) };
        root.Controls.Add(sidebar, 0, 0);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Padding = new Padding(14)
        };
        sidebar.Controls.Add(layout);

        var header = new Panel { Width = 420, Height = 126, BackColor = Color.FromArgb(112, 48, 160), Margin = new Padding(0, 0, 0, 14) };
        header.Controls.Add(new PictureBox
        {
            Image = LoadLiethLogo(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(14, 20),
            Size = new Size(52, 52)
        });
        header.Controls.Add(new Label
        {
            Text = "Lieth Organigramme",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 16F, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(78, 20)
        });
        header.Controls.Add(new Label
        {
            Text = "Assistant",
            AutoSize = true,
            ForeColor = Color.FromArgb(236, 224, 249),
            Location = new Point(80, 48)
        });
        header.Controls.Add(new Label
        {
            Text = "Besoin d’aide ? liethavid@gmail.com",
            AutoSize = true,
            ForeColor = Color.FromArgb(236, 224, 249),
            Location = new Point(18, 92)
        });
        layout.Controls.Add(header);

        layout.Controls.Add(new Label
        {
            Text = "Éditeur de logigramme",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = Color.FromArgb(112, 48, 160),
            Margin = new Padding(0, 0, 0, 10)
        });
        layout.Controls.Add(new Label
        {
            Text = "Ajoutez des étapes, déplacez-les, puis glissez d’un nœud à l’autre pour créer une flèche.",
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            Margin = new Padding(0, 0, 0, 12)
        });

        layout.Controls.Add(SectionHeader("Créer un nœud"));
        var addRow = ToolbarColumn();
        addRow.Controls.Add(Button("Ajouter une étape", (_, _) => AddNode(DiagramNodeKind.Process)));
        addRow.Controls.Add(Button("Ajouter une décision", (_, _) => AddNode(DiagramNodeKind.Decision)));
        addRow.Controls.Add(Button("Ajouter début / fin", (_, _) => AddNode(DiagramNodeKind.Terminator)));
        layout.Controls.Add(addRow);

        layout.Controls.Add(SectionHeader("Modifier et relier"));
        var arrowRow = ToolbarColumn();
        arrowRow.Controls.Add(Button("Créer une flèche", (_, _) => ToggleConnectMode()));
        arrowRow.Controls.Add(Button("Supprimer nœud / flèche", (_, _) => DeleteSelection()));
        layout.Controls.Add(arrowRow);
        layout.Controls.Add(new Label
        {
            Text = "Déplacement : glissez n’importe quel nœud. Flèche : activez le bouton, puis glissez depuis un petit cercle sur un nœud vers un autre nœud.",
            AutoSize = true,
            MaximumSize = new Size(430, 0),
            ForeColor = Color.FromArgb(80, 80, 80),
            Margin = new Padding(0, 0, 0, 8)
        });

        layout.Controls.Add(new Label
        {
            Text = "Sélection",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = Color.FromArgb(112, 48, 160),
            Margin = new Padding(0, 16, 0, 4)
        });
        selectionLabel.AutoSize = true;
        selectionLabel.MaximumSize = new Size(420, 0);
        layout.Controls.Add(selectionLabel);

        layout.Controls.Add(new Label { Text = "Texte", AutoSize = true, Margin = new Padding(0, 10, 0, 2) });
        nodeText.Multiline = true;
        nodeText.ScrollBars = ScrollBars.Vertical;
        nodeText.Height = 100;
        nodeText.Dock = DockStyle.Top;
        nodeText.BackColor = Color.White;
        nodeText.TextChanged += (_, _) =>
        {
            if (!updatingInspector && selectedNode is not null)
            {
                selectedNode.Text = nodeText.Text;
                canvas.Invalidate();
                UpdateStatus();
            }
        };
        layout.Controls.Add(nodeText);

        layout.Controls.Add(new Label { Text = "Forme", AutoSize = true, Margin = new Padding(0, 10, 0, 2) });
        nodeKind.DropDownStyle = ComboBoxStyle.DropDownList;
        nodeKind.BackColor = Color.White;
        nodeKind.Items.AddRange(new object[] { "Étape", "Décision", "Début / fin" });
        nodeKind.SelectedIndexChanged += (_, _) =>
        {
            if (!updatingInspector && selectedNode is not null && nodeKind.SelectedIndex >= 0)
            {
                selectedNode.Kind = (DiagramNodeKind)nodeKind.SelectedIndex;
                canvas.Invalidate();
                UpdateStatus();
            }
        };
        layout.Controls.Add(nodeKind);

        layout.Controls.Add(SectionHeader("Fichier"));
        var utilityRow = ToolbarColumn();
        utilityRow.Controls.Add(Button("Vider le canevas", (_, _) => ClearCanvas()));
        utilityRow.Controls.Add(Button("Exporter PNG", (_, _) => ExportPng()));
        layout.Controls.Add(utilityRow);

        status.AutoSize = true;
        status.MaximumSize = new Size(420, 0);
        status.ForeColor = Color.FromArgb(70, 70, 70);
        status.Margin = new Padding(0, 12, 0, 0);
        layout.Controls.Add(status);

        var canvasPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8), BackColor = Color.White };
        canvas.Dock = DockStyle.Fill;
        canvasPanel.Controls.Add(canvas);
        root.Controls.Add(canvasPanel, 1, 0);
    }

    private static Button Button(string text, EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = false,
            Width = 420,
            Height = 40,
            Margin = new Padding(0, 0, 0, 7),
            BackColor = Color.White,
            ForeColor = Color.FromArgb(85, 35, 125),
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0)
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(206, 183, 225);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(239, 229, 248);
        button.Click += click;
        return button;
    }

    private static FlowLayoutPanel ToolbarColumn() => new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        Margin = new Padding(0, 0, 0, 2)
    };

    private static Label SectionHeader(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
        ForeColor = Color.FromArgb(112, 48, 160),
        Margin = new Padding(0, 12, 0, 4)
    };

    private static Image? LoadLiethLogo()
    {
        using var source = typeof(FlowchartForm).Assembly.GetManifestResourceStream("LiethOrganigrammeAssistant.Assets.lieth-organigramme-logo.png");
        if (source is null) return null;
        using var image = Image.FromStream(source);
        return new Bitmap(image);
    }

    private void AddNode(DiagramNodeKind kind)
    {
        using var dialog = new NodeDialog(kind);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (string.IsNullOrWhiteSpace(dialog.NodeText))
        {
            MessageBox.Show(this, "Le nœud doit contenir un texte.", "Nœud incomplet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var count = model.Nodes.Count;
        var x = 100 + (count * 43) % 850;
        var y = 120 + (count * 67) % 1250;
        var size = dialog.Kind == DiagramNodeKind.Decision ? new SizeF(430, 100) : dialog.Kind == DiagramNodeKind.Terminator ? new SizeF(430, 110) : new SizeF(430, 100);
        var node = model.Add(dialog.Kind, dialog.NodeText, new RectangleF(x, y, size.Width, size.Height));
        canvas.SelectNode(node);
        canvas.Invalidate();
        UpdateStatus();
    }

    private void ToggleConnectMode()
    {
        connectMode = !connectMode;
        canvas.CancelArrowDrag();
        canvas.ConnectMode = connectMode;
        status.Text = connectMode ? "Glissez du nœud de départ au nœud d’arrivée." : "Mode flèche annulé.";
        canvas.Invalidate();
    }

    private void HandleArrowDrag(DiagramNode from, DiagramNode to)
    {
        var exists = model.Arrows.Any(a => a.From == from.Id && a.To == to.Id);
        if (!exists) model.Arrows.Add(new DiagramArrow(from.Id, to.Id));
        connectMode = false;
        canvas.ConnectMode = false;
        canvas.Invalidate();
        UpdateStatus(exists ? "Cette flèche existait déjà." : "Flèche créée.");
    }

    private void DeleteSelection()
    {
        if (selectedNode is not null)
        {
            model.Nodes.Remove(selectedNode);
            model.Arrows.RemoveAll(a => a.From == selectedNode.Id || a.To == selectedNode.Id);
        }
        else if (selectedArrow is not null)
        {
            model.Arrows.Remove(selectedArrow);
        }
        else return;

        selectedNode = null;
        selectedArrow = null;
        canvas.ClearSelection();
        canvas.Invalidate();
        UpdateInspector();
        UpdateStatus("Sélection supprimée.");
    }

    private void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete && (selectedNode is not null || selectedArrow is not null))
        {
            DeleteSelection();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape && connectMode)
        {
            ToggleConnectMode();
            e.Handled = true;
        }
    }

    private void UpdateInspector()
    {
        updatingInspector = true;
        try
        {
            if (selectedNode is not null)
            {
                selectionLabel.Text = "Nœud sélectionné";
                nodeText.Enabled = true;
                nodeKind.Enabled = true;
                nodeText.Text = selectedNode.Text;
                nodeKind.SelectedIndex = (int)selectedNode.Kind;
            }
            else if (selectedArrow is not null)
            {
                selectionLabel.Text = "Flèche sélectionnée — Suppr pour la retirer";
                nodeText.Enabled = false;
                nodeKind.Enabled = false;
                nodeText.Clear();
                nodeKind.SelectedIndex = -1;
            }
            else
            {
                selectionLabel.Text = "Aucune sélection";
                nodeText.Enabled = false;
                nodeKind.Enabled = false;
                nodeText.Clear();
                nodeKind.SelectedIndex = -1;
            }
        }
        finally { updatingInspector = false; }
    }

    private void ClearCanvas()
    {
        if (model.Nodes.Count == 0 && model.Arrows.Count == 0) return;
        if (MessageBox.Show(this, "Supprimer tous les nœuds et toutes les flèches ?", "Vider le canevas", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        model.Nodes.Clear();
        model.Arrows.Clear();
        selectedNode = null;
        selectedArrow = null;
        canvas.ClearSelection();
        canvas.Invalidate();
        UpdateInspector();
        UpdateStatus("Canevas vidé.");
    }

    private void ExportPng()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "Image PNG (*.png)|*.png",
            FileName = "logigramme.png",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        using var image = DiagramRenderer.Render(model);
        image.Save(dialog.FileName, ImageFormat.Png);
        UpdateStatus("PNG exporté : " + dialog.FileName);
    }

    private void UpdateStatus(string? message = null)
    {
        status.Text = message ?? $"{model.Nodes.Count} nœud(s), {model.Arrows.Count} flèche(s). Glissez les nœuds pour les déplacer.";
    }

}

internal sealed class NodeDialog : Form
{
    private readonly TextBox textBox = new();
    private readonly ComboBox kindBox = new();
    public string NodeText => textBox.Text.Trim();
    public DiagramNodeKind Kind => (DiagramNodeKind)kindBox.SelectedIndex;

    public NodeDialog(DiagramNodeKind defaultKind)
    {
        Text = "Ajouter un nœud";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(480, 260);
        MinimumSize = new Size(400, 220);
        Font = new Font("Segoe UI", 10F);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), RowCount = 5, ColumnCount = 1 };
        Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "Texte du nœud", AutoSize = true });
        textBox.Multiline = true;
        textBox.Height = 75;
        textBox.Dock = DockStyle.Top;
        textBox.Text = "Nouvelle étape";
        layout.Controls.Add(textBox);
        layout.Controls.Add(new Label { Text = "Forme", AutoSize = true, Margin = new Padding(0, 8, 0, 2) });
        kindBox.DropDownStyle = ComboBoxStyle.DropDownList;
        kindBox.Items.AddRange(new object[] { "Étape", "Décision", "Début / fin" });
        kindBox.SelectedIndex = (int)defaultKind;
        layout.Controls.Add(kindBox);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var ok = new Button { Text = "Ajouter", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Annuler", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}

internal sealed class ArrowDragEventArgs : EventArgs
{
    public DiagramNode From { get; }
    public DiagramNode To { get; }

    public ArrowDragEventArgs(DiagramNode from, DiagramNode to)
    {
        From = from;
        To = to;
    }
}

internal sealed class DiagramCanvas : ScrollableControl
{
    private const float MarginSize = 18;
    private readonly DiagramModel model;
    private bool dragging;
    private bool draggingArrow;
    private PointF dragOffset;
    private PointF arrowPreviewPoint;
    public float Zoom { get; } = .52F;
    public DiagramNode? SelectedNode { get; private set; }
    public DiagramArrow? SelectedArrow { get; private set; }
    public DiagramNode? ConnectionSource { get; set; }
    public PointF? ConnectionStartPoint { get; private set; }
    public bool ConnectMode { get; set; }
    public event EventHandler? SelectionChanged;
    public event EventHandler? ModelChanged;
    public event EventHandler<ArrowDragEventArgs>? ArrowDragCompleted;

    public DiagramCanvas(DiagramModel model)
    {
        this.model = model;
        DoubleBuffered = true;
        AutoScroll = true;
        BackColor = Color.FromArgb(238, 238, 238);
        AutoScrollMinSize = new Size((int)(DiagramModel.Width * Zoom + MarginSize * 2), (int)(DiagramModel.Height * Zoom + MarginSize * 2));
        TabStop = true;
    }

    public void SelectNode(DiagramNode node)
    {
        SelectedNode = node;
        SelectedArrow = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void ClearSelection()
    {
        SelectedNode = null;
        SelectedArrow = null;
        ConnectionSource = null;
        ConnectionStartPoint = null;
        ConnectionPreviewPoint = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public PointF? ConnectionPreviewPoint { get; private set; }

    public void CancelArrowDrag()
    {
        draggingArrow = false;
        ConnectionSource = null;
        ConnectionStartPoint = null;
        ConnectionPreviewPoint = null;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        e.Graphics.Clear(BackColor);
        e.Graphics.TranslateTransform(AutoScrollPosition.X + MarginSize, AutoScrollPosition.Y + MarginSize);
        e.Graphics.ScaleTransform(Zoom, Zoom);
        DiagramRenderer.Draw(e.Graphics, model, SelectedNode, SelectedArrow, ConnectionSource, ConnectionStartPoint, ConnectionPreviewPoint, ConnectMode);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left) return;
        var point = ToModel(e.Location);
        if (ConnectMode)
        {
            var connectNode = HitNode(point);
            var handle = connectNode is null ? null : HitHandle(connectNode, point);
            if (connectNode is not null && handle is PointF startPoint)
            {
                draggingArrow = true;
                ConnectionSource = connectNode;
                ConnectionStartPoint = startPoint;
                arrowPreviewPoint = point;
                ConnectionPreviewPoint = point;
                Invalidate();
                return;
            }
        }

        var arrow = HitArrow(point);
        if (arrow is not null)
        {
            SelectedNode = null;
            SelectedArrow = arrow;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
            return;
        }

        var node = HitNode(point);

        if (node is not null)
        {
            SelectNode(node);
            dragging = true;
            dragOffset = new PointF(point.X - node.Bounds.X, point.Y - node.Bounds.Y);
            return;
        }

        SelectedNode = null;
        SelectedArrow = HitArrow(point);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging && !draggingArrow)
        {
            var point = ToModel(e.Location);
            var node = HitNode(point);
            Cursor = ConnectMode && node is not null && HitHandle(node, point) is not null ? Cursors.Cross
                : ConnectMode && node is not null ? Cursors.SizeAll
                : !ConnectMode && HitArrow(point) is not null ? Cursors.Hand
                : Cursors.Default;
        }
        if (draggingArrow && ConnectionSource is not null && e.Button == MouseButtons.Left)
        {
            arrowPreviewPoint = ToModel(e.Location);
            ConnectionPreviewPoint = arrowPreviewPoint;
            Invalidate();
            return;
        }
        if (!dragging || SelectedNode is null || e.Button != MouseButtons.Left) return;
        var modelPoint = ToModel(e.Location);
        var bounds = SelectedNode.Bounds;
        bounds.X = Math.Clamp(modelPoint.X - dragOffset.X, 0, DiagramModel.Width - bounds.Width);
        bounds.Y = Math.Clamp(modelPoint.Y - dragOffset.Y, 0, DiagramModel.Height - bounds.Height);
        SelectedNode.Bounds = bounds;
        Invalidate();
        ModelChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (draggingArrow)
        {
            var source = ConnectionSource;
            var target = HitNode(ToModel(e.Location));
            draggingArrow = false;
            ConnectionSource = null;
            ConnectionStartPoint = null;
            ConnectionPreviewPoint = null;
            if (source is not null && target is not null && source.Id != target.Id)
                ArrowDragCompleted?.Invoke(this, new ArrowDragEventArgs(source, target));
            Invalidate();
            return;
        }
        dragging = false;
    }

    private PointF ToModel(Point point) => new(
        (point.X - AutoScrollPosition.X - MarginSize) / Zoom,
        (point.Y - AutoScrollPosition.Y - MarginSize) / Zoom);

    private DiagramNode? HitNode(PointF point) => model.Nodes.AsEnumerable().Reverse().FirstOrDefault(n => DiagramRenderer.Contains(n, point));

    private static PointF? HitHandle(DiagramNode node, PointF point)
    {
        foreach (var handle in DiagramRenderer.ConnectionHandles(node))
        {
            var dx = point.X - handle.X;
            var dy = point.Y - handle.Y;
            if (dx * dx + dy * dy <= 22 * 22) return handle;
        }
        return null;
    }

    private DiagramArrow? HitArrow(PointF point)
    {
        foreach (var arrow in model.Arrows.AsEnumerable().Reverse())
        {
            var segment = DiagramRenderer.ArrowSegment(model, arrow);
            if (segment is null) continue;
            if (DistanceToSegment(point, segment.Value.Start, segment.Value.End) <= 30) return arrow;
        }
        return null;
    }

    private static float DistanceToSegment(PointF p, PointF a, PointF b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        if (dx == 0 && dy == 0) return Distance(p, a);
        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy), 0, 1);
        return Distance(p, new PointF(a.X + t * dx, a.Y + t * dy));
    }

    private static float Distance(PointF a, PointF b) => MathF.Sqrt(MathF.Pow(a.X - b.X, 2) + MathF.Pow(a.Y - b.Y, 2));
}

internal static class DiagramRenderer
{
    private static readonly Color Purple = Color.FromArgb(112, 48, 160);
    private static readonly Color PalePurple = Color.FromArgb(232, 220, 246);
    private static readonly Color Selection = Color.FromArgb(235, 130, 35);

    public static Bitmap Render(DiagramModel model)
    {
        var bitmap = new Bitmap(DiagramModel.Width, DiagramModel.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        g.Clear(Color.White);
        Draw(g, model, null, null, null, null, null, false);
        return bitmap;
    }

    public static void Draw(Graphics g, DiagramModel model, DiagramNode? selectedNode, DiagramArrow? selectedArrow, DiagramNode? connectionSource, PointF? connectionStartPoint, PointF? connectionPreviewPoint, bool showConnectionHandles)
    {
        using var normalPen = new Pen(Purple, 3F) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var selectedPen = new Pen(Selection, 5F) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var fill = new SolidBrush(PalePurple);
        using var white = new SolidBrush(Color.White);
        using var text = new SolidBrush(Color.FromArgb(35, 35, 35));
        using var purple = new SolidBrush(Purple);

        foreach (var arrow in model.Arrows)
        {
            var segment = ArrowSegment(model, arrow);
            if (segment is null) continue;
            var pen = selectedArrow == arrow ? selectedPen : normalPen;
            DrawArrow(g, pen, segment.Value.Start, segment.Value.End);
        }

        if (connectionSource is not null && connectionPreviewPoint is PointF preview)
        {
            var center = Center(connectionSource.Bounds);
            var direction = new PointF(preview.X - center.X, preview.Y - center.Y);
            var length = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            if (length > 1)
            {
                direction.X /= length;
                direction.Y /= length;
                using var previewPen = new Pen(Color.FromArgb(150, Purple), 3F) { DashStyle = DashStyle.Dash, StartCap = LineCap.Round, EndCap = LineCap.Round };
                DrawArrow(g, previewPen, connectionStartPoint ?? EdgePoint(connectionSource, direction), preview);
            }
        }

        foreach (var node in model.Nodes)
        {
            var nodePen = node == selectedNode || node == connectionSource ? selectedPen : normalPen;
            DrawNode(g, nodePen, node.Kind == DiagramNodeKind.Process ? white : fill, text, node);
        }

        if (showConnectionHandles)
        {
            using var handleFill = new SolidBrush(Color.White);
            using var handlePen = new Pen(Purple, 3F);
            foreach (var node in model.Nodes)
            {
                foreach (var handle in ConnectionHandles(node))
                {
                    g.FillEllipse(handleFill, handle.X - 9, handle.Y - 9, 18, 18);
                    g.DrawEllipse(handlePen, handle.X - 9, handle.Y - 9, 18, 18);
                }
            }
        }

        if (selectedNode is not null)
        {
            using var outline = new Pen(Selection, 2F) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(outline, RectangleF.Inflate(selectedNode.Bounds, 7, 7));
        }
    }

    public static bool Contains(DiagramNode node, PointF point)
    {
        var b = node.Bounds;
        if (node.Kind == DiagramNodeKind.Process) return b.Contains(point);
        var dx = point.X - b.X - b.Width / 2;
        var dy = point.Y - b.Y - b.Height / 2;
        if (node.Kind == DiagramNodeKind.Terminator)
            return MathF.Pow(dx / (b.Width / 2), 2) + MathF.Pow(dy / (b.Height / 2), 2) <= 1;
        return MathF.Abs(dx) / (b.Width / 2) + MathF.Abs(dy) / (b.Height / 2) <= 1;
    }

    public static PointF[] ConnectionHandles(DiagramNode node)
    {
        var b = node.Bounds;
        return new[]
        {
            new PointF(b.Left, b.Top + b.Height / 2),
            new PointF(b.Right, b.Top + b.Height / 2),
            new PointF(b.Left + b.Width / 2, b.Top),
            new PointF(b.Left + b.Width / 2, b.Bottom)
        };
    }

    public static (PointF Start, PointF End)? ArrowSegment(DiagramModel model, DiagramArrow arrow)
    {
        var from = model.Nodes.FirstOrDefault(n => n.Id == arrow.From);
        var to = model.Nodes.FirstOrDefault(n => n.Id == arrow.To);
        if (from is null || to is null) return null;
        var source = Center(from.Bounds);
        var target = Center(to.Bounds);
        var direction = new PointF(target.X - source.X, target.Y - source.Y);
        var length = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        if (length < 1) return null;
        direction.X /= length;
        direction.Y /= length;
        return (EdgePoint(from, direction), EdgePoint(to, new PointF(-direction.X, -direction.Y)));
    }

    private static void DrawNode(Graphics g, Pen pen, Brush fill, Brush text, DiagramNode node)
    {
        var b = node.Bounds;
        if (node.Kind == DiagramNodeKind.Process)
        {
            using var path = Rounded(b, Math.Min(28, Math.Min(b.Width, b.Height) / 3));
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }
        else if (node.Kind == DiagramNodeKind.Terminator)
        {
            g.FillEllipse(fill, b);
            g.DrawEllipse(pen, b);
        }
        else
        {
            var points = Diamond(b);
            g.FillPolygon(fill, points);
            g.DrawPolygon(pen, points);
        }
        DrawFittedText(g, text, node.Text, RectangleF.Inflate(b, node.Kind == DiagramNodeKind.Decision ? -80 : -24, -16));
    }

    private static void DrawArrow(Graphics g, Pen pen, PointF start, PointF end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 1) return;
        dx /= length;
        dy /= length;
        var lineEnd = new PointF(end.X - dx * 13, end.Y - dy * 13);
        g.DrawLine(pen, start, lineEnd);
        var side = new PointF(-dy, dx);
        var left = new PointF(end.X - dx * 17 + side.X * 8, end.Y - dy * 17 + side.Y * 8);
        var right = new PointF(end.X - dx * 17 - side.X * 8, end.Y - dy * 17 - side.Y * 8);
        using var brush = new SolidBrush(pen.Color);
        g.FillPolygon(brush, new[] { end, left, right });
    }

    private static void DrawFittedText(Graphics g, Brush brush, string value, RectangleF bounds)
    {
        value = value.Trim();
        if (value.Length == 0) return;
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord };
        Font? font = null;
        try
        {
            for (var size = 25F; size >= 12F; size -= .5F)
            {
                font?.Dispose();
                font = new Font("Arial", size, FontStyle.Regular, GraphicsUnit.Pixel);
                if (g.MeasureString(value, font, new SizeF(bounds.Width, 10000), format).Height <= bounds.Height) break;
            }
            g.DrawString(value, font!, brush, bounds, format);
        }
        finally { font?.Dispose(); }
    }

    private static PointF EdgePoint(DiagramNode node, PointF direction)
    {
        var b = node.Bounds;
        var center = Center(b);
        var halfWidth = b.Width / 2;
        var halfHeight = b.Height / 2;
        float distance;
        if (node.Kind == DiagramNodeKind.Terminator)
            distance = 1 / MathF.Sqrt(MathF.Pow(direction.X / halfWidth, 2) + MathF.Pow(direction.Y / halfHeight, 2));
        else if (node.Kind == DiagramNodeKind.Decision)
            distance = 1 / (MathF.Abs(direction.X) / halfWidth + MathF.Abs(direction.Y) / halfHeight);
        else
            distance = Math.Min(MathF.Abs(halfWidth / (direction.X == 0 ? .0001F : direction.X)), MathF.Abs(halfHeight / (direction.Y == 0 ? .0001F : direction.Y)));
        return new PointF(center.X + direction.X * distance, center.Y + direction.Y * distance);
    }

    private static PointF Center(RectangleF bounds) => new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);

    private static PointF[] Diamond(RectangleF b) => new[]
    {
        new PointF(b.Left, b.Top + b.Height / 2),
        new PointF(b.Left + b.Width / 2, b.Top),
        new PointF(b.Right, b.Top + b.Height / 2),
        new PointF(b.Left + b.Width / 2, b.Bottom)
    };

    private static GraphicsPath Rounded(RectangleF b, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(b.Left, b.Top, d, d, 180, 90);
        path.AddArc(b.Right - d, b.Top, d, d, 270, 90);
        path.AddArc(b.Right - d, b.Bottom - d, d, d, 0, 90);
        path.AddArc(b.Left, b.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
