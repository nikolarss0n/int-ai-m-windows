using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using InterviewMaster.Domain;
using InterviewMaster.Support;

namespace InterviewMaster.Presentation;

/// Runs the Context tab: keeps the notes saved, carries a list on, formats the selection, searches,
/// and loads a template or a fresh note. A port of `NotesEditor.swift`, `SearchController.swift`,
/// `FormattingToolbar.swift`, `TemplateSelector.swift` and the note commands of `MenuBarSetup.swift`.
public sealed class NotesController
{
    private readonly NotesView view;
    private readonly IAppShell shell;
    private readonly InterviewController interview;
    /// Text the app put in itself. The change it causes is not the person's: it is not saved or carried on.
    private string? placedText;
    private bool placing;
    private int lastLength;
    private Delayed? firstMatch;
    private IPanel? templatePanel;

    public NotesController(NotesView view, IAppShell shell, InterviewController interview)
    {
        this.view = view;
        this.shell = shell;
        this.interview = interview;

        var saved = SettingsStore.Standard.GetString(NotesEditing.StorageKey);
        SetText(string.IsNullOrEmpty(saved) ? NotesEditing.DefaultNotes : saved);

        view.Editor.TextChanged += (_, _) => TextChanged();
        view.Editor.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBox.SelectionStartProperty || change.Property == TextBox.SelectionEndProperty || change.Property == TextBox.CaretIndexProperty)
            {
                SelectionChanged();
            }
        };
        view.Toolbar.Chosen += Format;
        view.Search.Submitted += PerformSearch;
        view.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape || !IsSearchVisible) return;
            e.Handled = true;
            ToggleSearch();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        interview.TabChanged += (_, next) =>
        {
            if (next != Tab.Notes) HideToolbar();
        };
    }

    public string Text => view.Editor.Text ?? "";

    public bool IsSearchVisible { get; private set; }

    public bool IsToolbarVisible { get; private set; }

    // MARK: - Text

    private void SetText(string text)
    {
        text = NotesEditing.NormalizedLineEnds(text);
        view.Editor.Highlights = Array.Empty<(int, int)>();
        Place(text, caret: 0);
    }

    private void Place(string text, int caret)
    {
        placing = true;
        if (Text != text) placedText = text;
        view.Editor.Text = text;
        view.Editor.CaretIndex = view.Editor.SelectionStart = view.Editor.SelectionEnd = caret;
        lastLength = text.Length;
        placing = false;
    }

    private void TextChanged()
    {
        var text = Text;
        if (text == placedText)
        {
            placedText = null;
            return;
        }
        placedText = null;
        if (text.Contains('\r'))
        {
            // Pasted from somewhere that ends lines the Windows way: Markdown is read by the line.
            var caret = Math.Min(view.Editor.CaretIndex, text.Length);
            text = NotesEditing.NormalizedLineEnds(text);
            Place(text, NotesEditing.NormalizedLineEnds(Text[..caret]).Length);
            placedText = null;
        }
        var grew = text.Length > lastLength;
        lastLength = text.Length;
        if (view.Editor.Highlights.Count > 0) view.Editor.Highlights = Array.Empty<(int, int)>();
        Save();
        // The caret moves after the text does; the list is carried on once both have settled.
        if (grew) Dispatcher.UIThread.Post(ContinueList);
    }

    private void ContinueList()
    {
        if (NotesEditing.ContinueList(Text, view.Editor.CaretIndex) is { } edit) view.Editor.Replace(edit);
    }

    public void Save() => SettingsStore.Standard.Set(NotesEditing.StorageKey, Text);

    // MARK: - Formatting

    private void SelectionChanged()
    {
        if (placing || interview.CurrentTab != Tab.Notes) return;
        ShowToolbar();
    }

    public void ShowToolbar()
    {
        if (IsToolbarVisible) return;
        IsToolbarVisible = true;
        Ui.Fade(view.Toolbar, show: true, opacity: FormattingToolbarView.ShownOpacity);
        view.Relayout();
    }

    public void HideToolbar()
    {
        if (!IsToolbarVisible) return;
        IsToolbarVisible = false;
        Ui.Fade(view.Toolbar, show: false, done: view.Relayout);
    }

    public void Format(FormattingCommand command)
    {
        var editor = view.Editor;
        var start = Math.Min(editor.SelectionStart, editor.SelectionEnd);
        var length = Math.Abs(editor.SelectionEnd - editor.SelectionStart);
        var text = Text;
        NotesEditing.Edit? edit = command switch
        {
            FormattingCommand.Heading => NotesEditing.Wrap(text, start, length, "## ", ""),
            FormattingCommand.Bold => NotesEditing.Wrap(text, start, length, "**", "**"),
            FormattingCommand.Italic => NotesEditing.Wrap(text, start, length, "*", "*"),
            FormattingCommand.Code => NotesEditing.Wrap(text, start, length, "`", "`"),
            FormattingCommand.CodeBlock => NotesEditing.Wrap(text, start, length, "```\n", "\n```"),
            FormattingCommand.List => NotesEditing.Bullet(text, start, length),
            FormattingCommand.Link => NotesEditing.Wrap(text, start, length, "[", "](url)"),
            FormattingCommand.Divider => NotesEditing.InsertDivider(text, start, length),
            _ => null,
        };
        if (edit is { } change) editor.Replace(change);
    }

    // MARK: - Search

    public void ToggleSearch()
    {
        IsSearchVisible = !IsSearchVisible;
        if (IsSearchVisible)
        {
            Ui.Fade(view.Search, show: true, done: () => view.Search.Field.Focus());
            view.Relayout();
            return;
        }
        Ui.Fade(view.Search, show: false, done: () =>
        {
            if (IsSearchVisible) return;
            view.Search.Reset();
            ClearSearch();
            view.Relayout();
        });
    }

    public void PerformSearch(string term)
    {
        firstMatch?.Dispose();
        if (term.Length == 0)
        {
            ClearSearch();
            view.Search.ShowCount(null);
            return;
        }
        var matches = NotesEditing.Matches(Text, term);
        view.Search.ShowCount(matches.Count);
        if (matches.Count == 0)
        {
            ClearSearch();
            return;
        }
        view.Editor.Highlights = matches;
        var first = matches[0];
        firstMatch = Delayed.After(0.1, () => view.Editor.Select(first.Start, first.Length));
    }

    private void ClearSearch() => view.Editor.Highlights = Array.Empty<(int, int)>();

    // MARK: - Notes as a whole

    public void NewNote()
    {
        interview.SwitchTab(Tab.Notes);
        SetText(NotesEditing.NewNote);
        Save();
    }

    public async Task ExportNotesAsync()
    {
        if (await shell.ChooseSaveFileAsync("Export Notes", "interview-notes.md") is not { } path) return;
        try
        {
            File.WriteAllText(path, Text);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            await shell.AskAsync(AppQuestion.Notice(
                "Couldn't export the notes",
                FeedbackCopy.Sentence(error.Message) + "\n\nChoose another location and try again.", critical: true));
        }
    }

    public void ShowTemplates()
    {
        templatePanel?.Close();
        var picker = new TemplatePickerView();
        picker.Loaded += LoadTemplate;
        templatePanel = shell.ShowPanel("Interview Templates", picker, TemplatePickerView.PanelWidth, TemplatePickerView.PanelHeight);
    }

    public void LoadTemplate(InterviewTemplate template)
    {
        SetText(NotesEditing.TemplateMarkdown(template));
        interview.SwitchTab(Tab.Notes);
        templatePanel?.Close();
        templatePanel = null;
    }
}
