using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.TextMate;
using Sextant.ViewModels;
using TextMateSharp.Grammars;

namespace Sextant.Views;

/// <summary>
/// One read-only AvaloniaEdit for a diff hunk or a blame file.
/// The list around it owns the vertical scroll, so a picture stays in that same scroll.
/// </summary>
public sealed class DiffTextHost : Grid
{
    public static readonly StyledProperty<DiffEditorRow?> EditorProperty =
        AvaloniaProperty.Register<DiffTextHost, DiffEditorRow?>(nameof(Editor));

    public static readonly StyledProperty<double> ShiftProperty =
        AvaloniaProperty.Register<DiffTextHost, double>(nameof(Shift));

    public static readonly StyledProperty<double> WindowOffsetProperty =
        AvaloniaProperty.Register<DiffTextHost, double>(nameof(WindowOffset));

    private readonly List<TextEditor> _editors = [];
    private readonly List<TextMate.Installation> _grammars = [];
    private DiffEditorRow? _built;
    private bool _scrollPosted;

    public DiffTextHost()
    {
        Margin = new Thickness(4, 0);
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
    }

    public DiffEditorRow? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    /// <summary>Shared sideways shift for a side-by-side diff. Line numbers stay put.</summary>
    public double Shift
    {
        get => GetValue(ShiftProperty);
        set => SetValue(ShiftProperty, value);
    }

    /// <summary>How far into this document the visible slice begins.</summary>
    public double WindowOffset
    {
        get => GetValue(WindowOffsetProperty);
        set => SetValue(WindowOffsetProperty, value);
    }

    public double LineHeight
    {
        get
        {
            foreach (var editor in _editors)
            {
                var height = editor.TextArea.TextView.DefaultLineHeight;
                if (height >= 1)
                    return height;
            }

            return 0;
        }
    }

    public double ContentHeight
    {
        get
        {
            var height = 0.0;
            foreach (var editor in _editors)
            {
                var document = editor.TextArea.TextView.DocumentHeight;
                if (document > height)
                    height = document;
            }

            return height;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EditorProperty)
            Rebuild();
        else if (change.Property == ShiftProperty)
            ApplyShift();
        else if (change.Property == WindowOffsetProperty)
            ApplyOffset();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (this.FindAncestorOfType<ListBoxItem>() is { } item)
        {
            item.Padding = new Thickness(0);
            item.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        }

        ActualThemeVariantChanged += OnTheme;
        if (_editors.Count == 0)
            Rebuild();
        ApplyTheme();
        Dispatcher.UIThread.Post(InvalidateMeasure, DispatcherPriority.Background);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ActualThemeVariantChanged -= OnTheme;
        ClearEditors();
        _built = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTheme(object? sender, EventArgs e) => ApplyTheme();

    private void Rebuild()
    {
        var row = Editor;
        if (ReferenceEquals(row, _built))
            return;
        ClearEditors();
        _built = row;
        if (row is null || row.LineCount == 0)
            return;

        if (row.SideBySide)
        {
            ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            var left = CreateEditor(row, row.Lines, NumberMode.Old);
            var rule = new Border
            {
                Width = 1,
                Background = new SolidColorBrush(Color.FromArgb(80, 205, 214, 244)),
            };
            var right = CreateEditor(row, row.RightLines, NumberMode.New);
            SetColumn(rule, 1);
            SetColumn(right, 2);
            Children.Add(left);
            Children.Add(rule);
            Children.Add(right);
        }
        else
        {
            Children.Add(CreateEditor(row, row.Lines, row.Blame ? NumberMode.Blame : NumberMode.Both));
        }

        ApplyShift();
        ApplyOffset();
        InvalidateMeasure();
    }

    private TextEditor CreateEditor(DiffEditorRow row, IReadOnlyList<EditorLine> lines, NumberMode numbers)
    {
        var editor = new TextEditor
        {
            IsReadOnly = true,
            ShowLineNumbers = false,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, DejaVu Sans Mono"),
            FontSize = 12,
            Background = Brushes.Transparent,
            Foreground = this.TryFindResource("SystemControlForegroundBaseHighBrush", ActualThemeVariant, out var foreground) && foreground is IBrush brush
                ? brush
                : Brushes.White,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            WordWrap = false,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
        };
        editor.Options.HighlightCurrentLine = false;
        editor.Options.EnableHyperlinks = false;
        editor.Options.EnableEmailHyperlinks = false;
        editor.Options.EnableTextDragDrop = false;
        editor.TextArea.TextView.BackgroundRenderers.Add(new LineBackground(lines));
        editor.TextArea.LeftMargins.Add(new NumberMargin(lines, NumberColumns(numbers, lines)));
        if (row.Blame)
            editor.TextArea.LeftMargins.Add(new MetaMargin(lines));
        else
            editor.TextArea.LeftMargins.Add(new MarkerMargin(lines));
        if (!row.Blame && !row.SideBySide && lines.Any(line => line.ShowAction))
            editor.TextArea.LeftMargins.Add(new ActionMargin(lines));

        var copy = new MenuItem { Header = "Copy" };
        copy.Click += (_, _) => editor.Copy();
        var all = new MenuItem { Header = "Select all" };
        all.Click += (_, _) =>
        {
            editor.Focus();
            editor.SelectAll();
        };
        editor.ContextMenu = new ContextMenu { Items = { copy, all } };

        TryInstallGrammar(editor, row.Path);
        editor.Document = new TextDocument(DiffEditorRow.Document(lines));
        _editors.Add(editor);
        return editor;
    }

    private void TryInstallGrammar(TextEditor editor, string? path)
    {
        try
        {
            var options = DiffGrammar.Options(IsDark);
            var install = editor.InstallTextMate(options, false, static _ => { });
            _grammars.Add(install);
            var scope = DiffGrammar.ScopeFor(options, path);
            if (!string.IsNullOrEmpty(scope))
                install.SetGrammar(scope);
        }
        catch (Exception)
        {
            // The lines still show. A missing grammar leaves them one color.
        }
    }

    private void ApplyTheme()
    {
        if (_grammars.Count == 0)
            return;
        try
        {
            var dark = IsDark;
            var theme = DiffGrammar.Options(dark).LoadTheme(dark ? ThemeName.DarkPlus : ThemeName.LightPlus);
            foreach (var install in _grammars)
                install.SetTheme(theme);
        }
        catch (Exception)
        {
            // Keep the theme the grammar was created with.
        }
    }

    private void ApplyShift() => ScrollEditors();

    private void ApplyOffset() => ScrollEditors();

    private void ScrollEditors()
    {
        foreach (var editor in _editors)
            Move(editor, Shift, WindowOffset);
        if (_scrollPosted)
            return;
        _scrollPosted = true;
        Dispatcher.UIThread.Post(() =>
        {
            _scrollPosted = false;
            foreach (var editor in _editors)
                Move(editor, Shift, WindowOffset);
        }, DispatcherPriority.Render);
    }

    private static void Move(TextEditor editor, double x, double y)
    {
        editor.ScrollToHorizontalOffset(x);
        editor.ScrollToVerticalOffset(y);
        if (editor.TextArea.TextView is IScrollable scrollable)
            scrollable.Offset = new Vector(x, y);
        if (editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } inner
            && (Math.Abs(inner.Offset.X - x) > 0.5 || Math.Abs(inner.Offset.Y - y) > 0.5))
            inner.Offset = new Vector(x, y);
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Handled)
            return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y))
            return;
        var outer = this.FindAncestorOfType<ScrollViewer>();
        if (outer is null)
            return;
        var line = LineHeight >= 1 ? LineHeight : 18;
        var next = outer.Offset.Y - e.Delta.Y * line * 3;
        var max = Math.Max(0, outer.Extent.Height - outer.Viewport.Height);
        outer.Offset = new Vector(outer.Offset.X, Math.Clamp(next, 0, max));
        e.Handled = true;
    }

    private void ClearEditors()
    {
        foreach (var install in _grammars)
            install.Dispose();
        _grammars.Clear();
        _editors.Clear();
        Children.Clear();
        ColumnDefinitions.Clear();
    }

    private bool IsDark =>
        (ActualThemeVariant ?? Application.Current?.ActualThemeVariant) == ThemeVariant.Dark;

    private static NumberMode NumberColumns(NumberMode mode, IReadOnlyList<EditorLine> lines)
    {
        if (mode != NumberMode.Both)
            return mode;
        var anyOld = false;
        var anyNew = false;
        foreach (var line in lines)
        {
            anyOld |= line.OldNumber.Length > 0;
            anyNew |= line.NewNumber.Length > 0;
        }

        if (anyOld && !anyNew)
            return NumberMode.Old;
        if (anyNew && !anyOld)
            return NumberMode.New;
        return NumberMode.Both;
    }

    private enum NumberMode
    {
        Both,
        Old,
        New,
        Blame,
    }

    private static IBrush Ink(byte alpha)
    {
        var dark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        return new SolidColorBrush(dark
            ? Color.FromArgb(alpha, 205, 214, 244)
            : Color.FromArgb(alpha, 76, 79, 105));
    }

    private static IBrush? KindBrush(EditorLineKind kind) => kind switch
    {
        EditorLineKind.Added => DiffColors.Added,
        EditorLineKind.Removed => DiffColors.Removed,
        _ => null,
    };

    private static FormattedText Format(string text, IBrush brush, double size = 12)
    {
        return new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Cascadia Mono, Consolas, DejaVu Sans Mono")),
            size,
            brush);
    }

    private sealed class LineBackground : IBackgroundRenderer
    {
        private readonly IReadOnlyList<EditorLine> _lines;

        public LineBackground(IReadOnlyList<EditorLine> lines) => _lines = lines;

        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (!textView.VisualLinesValid || textView.VisualLines.Count == 0)
                return;
            var width = textView.Bounds.Width;
            foreach (var visualLine in textView.VisualLines)
            {
                var index = visualLine.FirstDocumentLine.LineNumber - 1;
                if ((uint)index >= (uint)_lines.Count)
                    continue;
                var brush = KindBrush(_lines[index].Kind);
                if (brush is null)
                    continue;
                var y = visualLine.VisualTop - textView.VerticalOffset;
                drawingContext.DrawRectangle(brush, null, new Rect(0, y, Math.Max(width, 1), visualLine.Height));
            }
        }
    }

    private sealed class NumberMargin : AbstractMargin
    {
        private readonly IReadOnlyList<EditorLine> _lines;
        private readonly NumberMode _mode;

        public NumberMargin(IReadOnlyList<EditorLine> lines, NumberMode mode)
        {
            _lines = lines;
            _mode = mode;
            IsHitTestVisible = false;
        }

        protected override Size MeasureOverride(Size available) => new(WidthOf(_mode), 0);

        public override void Render(DrawingContext context)
        {
            var view = TextView;
            if (view is null)
                return;
            var ink = Ink(140);
            foreach (var visualLine in view.VisualLines)
            {
                var index = visualLine.FirstDocumentLine.LineNumber - 1;
                if ((uint)index >= (uint)_lines.Count)
                    continue;
                var line = _lines[index];
                var y = visualLine.VisualTop - view.VerticalOffset;
                var textHeight = view.DefaultLineHeight;
                if (_mode is NumberMode.Both or NumberMode.Old)
                    Draw(context, line.OldNumber, 0, ink, y, textHeight);
                if (_mode is NumberMode.Both)
                    Draw(context, line.NewNumber, 56, ink, y, textHeight);
                if (_mode is NumberMode.New or NumberMode.Blame)
                    Draw(context, line.NewNumber, 0, ink, y, textHeight);
            }
        }

        protected override void OnTextViewVisualLinesChanged() => InvalidateVisual();

        private static void Draw(DrawingContext context, string text, double x, IBrush brush, double y, double height)
        {
            if (text.Length == 0)
                return;
            var formatted = Format(text, brush);
            var top = y + Math.Max(0, (height - formatted.Height) / 2);
            context.DrawText(formatted, new Point(x + 48 - formatted.Width, top));
        }

        private static double WidthOf(NumberMode mode) => mode == NumberMode.Both ? 112 : 56;
    }

    private sealed class MetaMargin : AbstractMargin
    {
        private readonly IReadOnlyList<EditorLine> _lines;

        public MetaMargin(IReadOnlyList<EditorLine> lines)
        {
            _lines = lines;
            IsHitTestVisible = false;
        }

        protected override Size MeasureOverride(Size available) => new(180, 0);

        public override void Render(DrawingContext context)
        {
            var view = TextView;
            if (view is null)
                return;
            var ink = Ink(190);
            foreach (var visualLine in view.VisualLines)
            {
                var index = visualLine.FirstDocumentLine.LineNumber - 1;
                if ((uint)index >= (uint)_lines.Count)
                    continue;
                var text = _lines[index].Meta;
                if (text.Length == 0)
                    continue;
                var formatted = Format(text, ink);
                if (formatted.Width > 172)
                {
                    var keep = Math.Max(1, (int)(text.Length * 172 / formatted.Width));
                    formatted = Format(text[..Math.Min(text.Length, keep)], ink);
                }

                var y = visualLine.VisualTop - view.VerticalOffset;
                var top = y + Math.Max(0, (view.DefaultLineHeight - formatted.Height) / 2);
                context.DrawText(formatted, new Point(0, top));
            }
        }

        protected override void OnTextViewVisualLinesChanged() => InvalidateVisual();
    }

    private sealed class MarkerMargin : AbstractMargin
    {
        private readonly IReadOnlyList<EditorLine> _lines;

        public MarkerMargin(IReadOnlyList<EditorLine> lines)
        {
            _lines = lines;
            IsHitTestVisible = false;
        }

        protected override Size MeasureOverride(Size available) => new(16, 0);

        public override void Render(DrawingContext context)
        {
            var view = TextView;
            if (view is null)
                return;
            foreach (var visualLine in view.VisualLines)
            {
                var index = visualLine.FirstDocumentLine.LineNumber - 1;
                if ((uint)index >= (uint)_lines.Count)
                    continue;
                var kind = _lines[index].Kind;
                var mark = kind switch
                {
                    EditorLineKind.Added => "+",
                    EditorLineKind.Removed => "-",
                    _ => "",
                };
                if (mark.Length == 0)
                    continue;
                var brush = kind == EditorLineKind.Added
                    ? new SolidColorBrush(Color.Parse("#A6E3A1"))
                    : new SolidColorBrush(Color.Parse("#F38BA8"));
                var formatted = Format(mark, brush);
                var y = visualLine.VisualTop - view.VerticalOffset;
                var top = y + Math.Max(0, (view.DefaultLineHeight - formatted.Height) / 2);
                context.DrawText(formatted, new Point(2, top));
            }
        }

        protected override void OnTextViewVisualLinesChanged() => InvalidateVisual();
    }

    private sealed class ActionMargin : AbstractMargin
    {
        private readonly IReadOnlyList<EditorLine> _lines;

        public ActionMargin(IReadOnlyList<EditorLine> lines)
        {
            _lines = lines;
            Cursor = new Cursor(StandardCursorType.Hand);
        }

        protected override Size MeasureOverride(Size available) => new(96, 0);

        public override void Render(DrawingContext context)
        {
            var view = TextView;
            if (view is null)
                return;
            var brush = new SolidColorBrush(Color.Parse("#89B4FA"));
            foreach (var visualLine in view.VisualLines)
            {
                var index = visualLine.FirstDocumentLine.LineNumber - 1;
                if ((uint)index >= (uint)_lines.Count || !_lines[index].ShowAction)
                    continue;
                var label = _lines[index].ActionLabel;
                if (label.Length == 0)
                    continue;
                var formatted = Format(label, brush, 11);
                var y = visualLine.VisualTop - view.VerticalOffset;
                var top = y + Math.Max(0, (view.DefaultLineHeight - formatted.Height) / 2);
                context.DrawText(formatted, new Point(4, top));
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var view = TextView;
            if (view is null)
                return;
            var y = e.GetPosition(this).Y + view.VerticalOffset;
            var documentLine = view.GetDocumentLineByVisualTop(y);
            if (documentLine is null)
                return;
            var index = documentLine.LineNumber - 1;
            if ((uint)index >= (uint)_lines.Count)
                return;
            var command = _lines[index].ActionCommand;
            if (command.CanExecute(null))
                command.Execute(null);
            e.Handled = true;
        }

        protected override void OnTextViewVisualLinesChanged() => InvalidateVisual();
    }

    private static class DiffGrammar
    {
        private static RegistryOptions? _dark;
        private static RegistryOptions? _light;

        public static RegistryOptions Options(bool dark) =>
            dark
                ? _dark ??= new RegistryOptions(ThemeName.DarkPlus)
                : _light ??= new RegistryOptions(ThemeName.LightPlus);

        internal static string? ScopeFor(RegistryOptions options, string? path)
        {
            var extension = Path.GetExtension(path ?? "");
            if (extension.Length == 0)
                return null;
            var language = options.GetLanguageByExtension(extension);
            var byLanguage = language is null ? null : options.GetScopeByLanguageId(language.Id);
            return string.IsNullOrEmpty(byLanguage) ? options.GetScopeByExtension(extension) : byLanguage;
        }
    }
}
