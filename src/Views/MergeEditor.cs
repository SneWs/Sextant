using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.TextMate;
using Sextant;
using Sextant.Git.Parsing;
using TextMateSharp.Grammars;

namespace Sextant.Views;

/// <summary>
/// Ours, the editable result, and theirs for one conflicted file. The base column is optional.
/// The result editor is the text that Save and stage writes.
/// </summary>
public sealed class MergeEditor : Grid
{
    public static readonly StyledProperty<MergeSession?> SessionProperty =
        AvaloniaProperty.Register<MergeEditor, MergeSession?>(nameof(Session));

    public static readonly StyledProperty<string?> FilePathProperty =
        AvaloniaProperty.Register<MergeEditor, string?>(nameof(FilePath));

    public static readonly StyledProperty<bool> ShowBaseProperty =
        AvaloniaProperty.Register<MergeEditor, bool>(nameof(ShowBase));

    public static readonly StyledProperty<int> ConflictIndexProperty =
        AvaloniaProperty.Register<MergeEditor, int>(nameof(ConflictIndex), defaultBindingMode: BindingMode.TwoWay);

    private readonly TextEditor _ours;
    private readonly TextEditor _result;
    private readonly TextEditor _theirs;
    private readonly TextEditor _base;
    private readonly TextBlock _baseHeader;
    private readonly Border _baseRule;
    private readonly List<TextMate.Installation> _grammars = [];
    private Dispatcher? _fontDispatcher;
    private readonly List<Mark> _oursMarks = [];
    private readonly List<Mark> _resultMarks = [];
    private readonly List<Mark> _theirsMarks = [];
    private readonly List<Mark> _baseMarks = [];
    private TextDocument? _resultDocument;
    private MergeSession? _listening;
    private bool _applying;
    private bool _movingCaret;
    private bool _syncing;

    public MergeEditor()
    {
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        MinHeight = 120;

        _ours = CreateEditor("MergeOurs", readOnly: true);
        _result = CreateEditor("MergeResult", readOnly: false);
        _theirs = CreateEditor("MergeTheirs", readOnly: true);
        _base = CreateEditor("MergeBase", readOnly: true);
        _result.ShowLineNumbers = true;
        _result.TextArea.Caret.PositionChanged += OnCaret;

        Add(Header("Ours"), 0, 0);
        Add(Rule(), 0, 1);
        Add(Header("Result"), 0, 2);
        Add(Rule(), 0, 3);
        Add(Header("Theirs"), 0, 4);
        _baseRule = Rule();
        _baseHeader = Header("Base");
        Add(_baseRule, 0, 5);
        Add(_baseHeader, 0, 6);
        Place(_ours, 1, 0);
        Place(_result, 1, 2);
        Place(_theirs, 1, 4);
        Place(_base, 1, 6);
        _ours.TextArea.TextView.BackgroundRenderers.Add(new RangeBackground(_oursMarks));
        _result.TextArea.TextView.BackgroundRenderers.Add(new RangeBackground(_resultMarks));
        _theirs.TextArea.TextView.BackgroundRenderers.Add(new RangeBackground(_theirsMarks));
        _base.TextArea.TextView.BackgroundRenderers.Add(new RangeBackground(_baseMarks));
        ApplyBaseColumn();
    }

    public MergeSession? Session
    {
        get => GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    public string? FilePath
    {
        get => GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    public bool ShowBase
    {
        get => GetValue(ShowBaseProperty);
        set => SetValue(ShowBaseProperty, value);
    }

    public int ConflictIndex
    {
        get => GetValue(ConflictIndexProperty);
        set => SetValue(ConflictIndexProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SessionProperty)
        {
            Retarget(Session);
            LoadFromSession(resetDocuments: false);
        }
        else if (change.Property == FilePathProperty)
            ApplyGrammar();
        else if (change.Property == ShowBaseProperty)
            ApplyBaseColumn();
        else if (change.Property == ConflictIndexProperty)
            RevealConflict();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _fontDispatcher = Dispatcher.UIThread;
        ActualThemeVariantChanged += OnTheme;
        DiffFont.Changed += OnFontChanged;
        ApplyChrome();
        InstallGrammars();
        Retarget(Session);
        LoadFromSession(resetDocuments: true);
        RevealConflict();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ActualThemeVariantChanged -= OnTheme;
        DiffFont.Changed -= OnFontChanged;
        Retarget(null);
        DisposeGrammars();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTheme(object? sender, EventArgs e)
    {
        ApplyChrome();
        Restyle();
    }

    private void OnFontChanged()
    {
        // Ignore changes raised while a different (torn-down) session's dispatcher is current.
        if (!ReferenceEquals(Dispatcher.UIThread, _fontDispatcher))
            return;
        var family = DiffFont.Current;
        foreach (var editor in Editors)
        {
            editor.FontFamily = family;
            editor.FontSize = DiffFont.Size;
        }
    }

    private void OnSessionChanged() => LoadFromSession(resetDocuments: false);

    private void LoadFromSession(bool resetDocuments)
    {
        var session = Session;
        var offset = Vertical(_result);
        _applying = true;
        try
        {
            SetEditor(_result, session?.Result ?? "", resetDocuments);
            SetEditor(_ours, session?.Ours ?? "", resetDocuments);
            SetEditor(_theirs, session?.Theirs ?? "", resetDocuments);
            SetEditor(_base, session?.Base ?? "", resetDocuments);
            SetVertical(_result, offset);
            SyncFrom(_result);
        }
        finally
        {
            _applying = false;
        }

        RefreshHighlight();
    }

    private void SetEditor(TextEditor editor, string text, bool reset)
    {
        if (!reset && editor.Document is { } current)
        {
            if (current.Text != text)
                current.Replace(0, current.TextLength, text);
            return;
        }

        var created = new TextDocument(text);
        if (ReferenceEquals(editor, _result))
            BindResult(created);
        else
            editor.Document = created;
    }

    private void BindResult(TextDocument document)
    {
        if (_resultDocument is not null)
            _resultDocument.Changed -= OnResultChanged;
        _resultDocument = document;
        _result.Document = document;
        document.Changed += OnResultChanged;
    }

    private void OnResultChanged(object? sender, DocumentChangeEventArgs change)
    {
        if (_applying || Session is null)
            return;
        Session.ApplyEdit(change.Offset, change.RemovalLength, Inserted(change));
    }

    private static string Inserted(DocumentChangeEventArgs change)
    {
        var text = change.InsertedText;
        return text is null ? "" : text.Text;
    }

    private void OnCaret(object? sender, EventArgs e)
    {
        if (_movingCaret || _applying || Session is null)
            return;
        var index = Session.ConflictAt(_result.CaretOffset);
        if (index >= 0 && index != ConflictIndex)
            ConflictIndex = index;
        else
            RefreshHighlight();
    }

    private void RevealConflict()
    {
        if (_applying || Session is null || _result.Document is null)
        {
            RefreshHighlight();
            return;
        }

        if (!Session.TryRange(ConflictIndex, out var range))
        {
            RefreshHighlight();
            return;
        }

        var end = range.ResultOffset + range.ResultLength;
        var caret = _result.CaretOffset;
        if (caret >= range.ResultOffset && caret <= end)
        {
            RefreshHighlight();
            return;
        }

        _movingCaret = true;
        try
        {
            var offset = Math.Clamp(range.ResultOffset, 0, _result.Document.TextLength);
            _result.CaretOffset = offset;
            var line = _result.Document.GetLineByOffset(Math.Clamp(offset, 0, Math.Max(0, _result.Document.TextLength)));
            _result.ScrollTo(line.LineNumber, 1);
            SyncFrom(_result);
        }
        finally
        {
            _movingCaret = false;
        }

        RefreshHighlight();
    }

    private void RefreshHighlight()
    {
        Fill(_resultMarks, static range => (range.ResultOffset, range.ResultLength));
        Fill(_oursMarks, static range => (range.OursOffset, range.OursLength));
        Fill(_theirsMarks, static range => (range.TheirsOffset, range.TheirsLength));
        Fill(_baseMarks, static range => (range.BaseOffset, range.BaseLength));
        Invalidate(_ours);
        Invalidate(_result);
        Invalidate(_theirs);
        Invalidate(_base);
    }

    private void Fill(List<Mark> marks, Func<MergeRange, (int Offset, int Length)> pick)
    {
        marks.Clear();
        if (Session is null)
            return;
        foreach (var range in Session.Conflicts)
        {
            var (offset, length) = pick(range);
            marks.Add(new Mark(offset, length, range.Index == ConflictIndex));
        }
    }

    private static void Invalidate(TextEditor editor) =>
        editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);

    private void OnScrolled(object? sender, EventArgs e)
    {
        if (_syncing || sender is not TextView view)
            return;
        TextEditor? source = null;
        foreach (var editor in Editors)
        {
            if (ReferenceEquals(editor.TextArea.TextView, view))
                source = editor;
        }

        if (source is null)
            return;
        SyncFrom(source);
    }

    private void SyncFrom(TextEditor source)
    {
        if (_syncing)
            return;
        _syncing = true;
        try
        {
            var offset = Vertical(source);
            foreach (var editor in Editors)
            {
                if (!ReferenceEquals(editor, source))
                    SetVertical(editor, offset);
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private void ApplyBaseColumn()
    {
        var show = ShowBase;
        _baseHeader.IsVisible = show;
        _base.IsVisible = show;
        _baseRule.IsVisible = show;
        ColumnDefinitions[5].Width = show ? GridLength.Auto : new GridLength(0);
        ColumnDefinitions[6].Width = show ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        _theirs.VerticalScrollBarVisibility = show ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto;
        _base.VerticalScrollBarVisibility = show ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
    }

    private void ApplyChrome()
    {
        var ink = this.TryFindResource("SystemControlForegroundBaseHighBrush", ActualThemeVariant, out var foreground) && foreground is IBrush brush
            ? brush
            : Brushes.White;
        var rule = this.TryFindResource("SystemControlForegroundBaseMediumLowBrush", ActualThemeVariant, out var line) && line is IBrush lineBrush
            ? lineBrush
            : new SolidColorBrush(Color.FromArgb(80, 128, 128, 128));
        foreach (var editor in Editors)
            editor.Foreground = ink;
        foreach (var border in Children.OfType<Border>())
            border.Background = rule;
    }

    private void InstallGrammars()
    {
        DisposeGrammars();
        foreach (var editor in Editors)
            TryInstall(editor);
    }

    private void TryInstall(TextEditor editor)
    {
        try
        {
            var options = Grammar.Options(IsDark);
            var install = editor.InstallTextMate(options, false, static _ => { });
            _grammars.Add(install);
            var scope = Grammar.ScopeFor(options, FilePath);
            if (!string.IsNullOrEmpty(scope))
                install.SetGrammar(scope);
        }
        catch (Exception)
        {
            // The text still shows. A missing grammar leaves it one color.
        }
    }

    private void ApplyGrammar()
    {
        if (_grammars.Count == 0)
            return;
        try
        {
            var options = Grammar.Options(IsDark);
            var scope = Grammar.ScopeFor(options, FilePath);
            if (string.IsNullOrEmpty(scope))
                return;
            foreach (var install in _grammars)
                install.SetGrammar(scope);
        }
        catch (Exception)
        {
            // Keep the previous grammar.
        }
    }

    private void Restyle()
    {
        if (_grammars.Count == 0)
            return;
        try
        {
            var dark = IsDark;
            var theme = Grammar.Options(dark).LoadTheme(dark ? ThemeName.DarkPlus : ThemeName.LightPlus);
            foreach (var install in _grammars)
                install.SetTheme(theme);
        }
        catch (Exception)
        {
            // Keep the theme the grammar was created with.
        }
    }

    private void DisposeGrammars()
    {
        foreach (var install in _grammars)
            install.Dispose();
        _grammars.Clear();
    }

    private void Retarget(MergeSession? session)
    {
        if (ReferenceEquals(_listening, session))
            return;
        if (_listening is not null)
            _listening.Changed -= OnSessionChanged;
        _listening = session;
        if (_listening is not null)
            _listening.Changed += OnSessionChanged;
    }

    private IEnumerable<TextEditor> Editors
    {
        get
        {
            yield return _ours;
            yield return _result;
            yield return _theirs;
            yield return _base;
        }
    }

    private bool IsDark =>
        (ActualThemeVariant ?? Application.Current?.ActualThemeVariant) == ThemeVariant.Dark;

    private TextEditor CreateEditor(string name, bool readOnly)
    {
        var editor = new TextEditor
        {
            Name = name,
            IsReadOnly = readOnly,
            ShowLineNumbers = false,
            FontFamily = DiffFont.Current,
            FontSize = DiffFont.Size,
            Background = Brushes.Transparent,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            WordWrap = false,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
        };
        editor.Options.HighlightCurrentLine = !readOnly;
        editor.Options.EnableHyperlinks = false;
        editor.Options.EnableEmailHyperlinks = false;
        editor.Options.EnableTextDragDrop = false;
        editor.Options.ConvertTabsToSpaces = false;
        editor.TextArea.TextView.ScrollOffsetChanged += OnScrolled;
        var copy = new MenuItem { Header = "Copy" };
        copy.Click += (_, _) => editor.Copy();
        var all = new MenuItem { Header = "Select all" };
        all.Click += (_, _) =>
        {
            editor.Focus();
            editor.SelectAll();
        };
        editor.ContextMenu = new ContextMenu { Items = { copy, all } };
        return editor;
    }

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        Opacity = 0.75,
        Margin = new Thickness(8, 4, 8, 4),
    };

    private static Border Rule() => new()
    {
        Width = 1,
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
    };

    private void Add(Control control, int row, int column)
    {
        SetRow(control, row);
        SetColumn(control, column);
        Children.Add(control);
    }

    private void Place(Control control, int row, int column) => Add(control, row, column);

    private static double Vertical(TextEditor editor) => editor.TextArea.TextView.VerticalOffset;

    private static void SetVertical(TextEditor editor, double offset)
    {
        editor.ScrollToVerticalOffset(offset);
        if (editor.TextArea.TextView is IScrollable scrollable)
            scrollable.Offset = new Vector(scrollable.Offset.X, offset);
    }

    private readonly record struct Mark(int Offset, int Length, bool Current);

    private sealed class RangeBackground : IBackgroundRenderer
    {
        private static readonly IBrush CurrentBrush = new ImmutableSolidColorBrush(Color.FromArgb(64, 224, 161, 0));
        private static readonly IBrush OtherBrush = new ImmutableSolidColorBrush(Color.FromArgb(28, 224, 161, 0));
        private readonly IReadOnlyList<Mark> _marks;

        public RangeBackground(IReadOnlyList<Mark> marks) => _marks = marks;

        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (!textView.VisualLinesValid || textView.VisualLines.Count == 0 || _marks.Count == 0)
                return;
            var width = Math.Max(textView.Bounds.Width, 1);
            foreach (var visualLine in textView.VisualLines)
            {
                var start = visualLine.FirstDocumentLine.Offset;
                var end = visualLine.LastDocumentLine.EndOffset;
                var current = false;
                var hit = false;
                foreach (var mark in _marks)
                {
                    if (!Overlaps(start, end, mark.Offset, mark.Length))
                        continue;
                    hit = true;
                    current |= mark.Current;
                }

                if (!hit)
                    continue;
                var y = visualLine.VisualTop - textView.VerticalOffset;
                drawingContext.DrawRectangle(current ? CurrentBrush : OtherBrush, null, new Rect(0, y, width, visualLine.Height));
            }
        }

        private static bool Overlaps(int lineStart, int lineEnd, int offset, int length)
        {
            if (length <= 0)
                return offset >= lineStart && offset <= lineEnd;
            return offset < lineEnd && offset + length > lineStart;
        }
    }

    private static class Grammar
    {
        private static RegistryOptions? _dark;
        private static RegistryOptions? _light;

        public static RegistryOptions Options(bool dark) =>
            dark
                ? _dark ??= new RegistryOptions(ThemeName.DarkPlus)
                : _light ??= new RegistryOptions(ThemeName.LightPlus);

        public static string? ScopeFor(RegistryOptions options, string? path)
        {
            var extension = DiffSyntax.GrammarExtension(path);
            if (string.IsNullOrEmpty(extension))
                return null;
            var language = options.GetLanguageByExtension(extension);
            var byLanguage = language is null ? null : options.GetScopeByLanguageId(language.Id);
            return string.IsNullOrEmpty(byLanguage) ? options.GetScopeByExtension(extension) : byLanguage;
        }
    }
}
