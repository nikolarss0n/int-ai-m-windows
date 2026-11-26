using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using InterviewMaster.Domain;

namespace InterviewMaster.Presentation;

/// The code for the interview: each request is a version, each version reads as files, and each file
/// as steps to type in order. It keeps the last few versions so an earlier one can be looked at again.
public sealed class CodePanelView : FrameView
{
    public sealed record Version(Guid Id, string Title, string Markdown, CodingTaskOutcome Outcome, bool IsStreaming, bool AllowsChanges, DateTime StartedAt);

    public const int MaxVersions = 10;
    private const double HeaderHeight = 66;
    private const double QuickActionsZone = 88;
    private const double FooterPadding = 11;
    private static readonly double ChipRowGap = Token.Space.Sm;
    private static readonly double CardSpacing = Token.Space.Md;
    private static readonly double ContentInset = Token.Space.Inset;

    /// The clock a wait is counted on. The UI driver replaces it.
    public static Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

    private readonly Border surface = new();
    private readonly TextBlock titleLabel = Ui.Overline("Code", Token.Hue.Accent.Text);
    private readonly TextBlock subtitleLabel = Ui.Label("", Token.Font.Title, Token.Font.Semibold, Token.Colors.TextPrimary, digits: true);
    private readonly HoverButton previousButton = HoverButton.Glyph(Icons.ChevronLeft, "Previous version", 24, 12);
    private readonly HoverButton nextButton = HoverButton.Glyph(Icons.ChevronRight, "Next version", 24, 12);
    private readonly TextBlock positionLabel = Ui.Label("", Token.Font.Caption, Token.Font.Medium, Token.Colors.TextMuted, digits: true);
    private readonly HoverButton originButton = new() { IsVisible = false, Padding = new Thickness(8, 0), CornerRadius = new CornerRadius(Token.Radius.Full(Token.Sizing.ControlSmall)) };
    private readonly TextBlock copiedLabel = Ui.Label("Copied", Token.Font.Caption, Token.Font.Medium, Token.Hue.Success.Text);
    private readonly HoverButton copyAllButton = HoverButton.Glyph(Icons.Copy, "Copy all code", Token.Sizing.ControlSmall, 13);
    private readonly HoverButton closeButton = HoverButton.Glyph(Icons.Close, "Hide code panel", Token.Sizing.ControlSmall, 12);
    private readonly ScrollViewer scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Document document;
    private readonly SelectableTextBlock fallbackView = new()
    {
        IsVisible = false,
        TextWrapping = TextWrapping.Wrap,
        FontFamily = Token.Font.Family,
        FontSize = Token.Font.Body,
        LineHeight = Math.Round(Token.Font.Body * Token.Leading.Compact),
        SelectionBrush = Token.Colors.FocusRing.WithAlpha(0.45).Brush(),
    };
    private string fallbackText = "";
    private readonly CodeStepSkeletonView stepSkeleton = new() { IsVisible = false };
    private readonly TextBlock emptyTitle = Ui.Label("Code appears here", Token.Font.Lead, Token.Font.Semibold, Token.Colors.TextBody);
    private readonly TextBlock emptyBody = Ui.Wrapping("When the interviewer asks for code, you get it here one step at a time.", Token.Font.Body, color: Token.Colors.TextBody);
    private readonly Border banner = new() { IsVisible = false };
    private readonly TextBlock bannerLabel = Ui.Wrapping("", Token.Font.Caption, Token.Font.Medium, Token.Colors.TextStrong, maxLines: 2);
    private readonly HoverButton retryButton = HoverButton.Chip("Try again", Token.ControlStyle.Chip(Token.Hue.Warning), Token.Hue.Warning.Text, height: 24);
    private readonly TextBlock viewingLabel = Ui.Label("", Token.Font.Caption, Token.Font.Medium, Token.Colors.TextMuted, digits: true);
    private readonly HoverButton latestButton = HoverButton.Chip("Show latest", Token.ControlStyle.Chip());
    private readonly DispatcherTimer waitTimer = new() { Interval = TimeSpan.FromSeconds(Token.Motion.ClockTick) };
    private readonly List<CodeFileView> fileViews = new();
    private readonly List<HoverButton> chips = new();
    private readonly List<Version> versions = new();
    private DispatcherTimer? copyReset;
    private Guid? steppedVersionId;
    private bool usesFallback;
    private bool isLive;
    private bool quickActionsRevealed;
    private bool footerShown;
    private bool attached;
    private double documentContentHeight;
    private double chipOpacity = 1;

    public event Action? Close;
    public event Action<string>? QuickAction;
    public event Action? RetryRequested;
    public event Action? OriginClicked;
    public event Action? Rendered;

    /// The language named when the code itself does not say.
    public Func<string> DefaultLanguageName { get; set; } = () => AppSettings.Shared.ProgrammingLanguage.DisplayName();

    public CodePanelView()
    {
        document = new Document(this);
        AutomationProperties.SetName(this, "Code panel");
        subtitleLabel.VerticalAlignment = VerticalAlignment.Center;
        titleLabel.VerticalAlignment = VerticalAlignment.Center;
        positionLabel.TextAlignment = TextAlignment.Center;
        positionLabel.VerticalAlignment = VerticalAlignment.Center;
        copiedLabel.TextAlignment = TextAlignment.Right;
        copiedLabel.VerticalAlignment = VerticalAlignment.Center;
        copiedLabel.IsVisible = false;
        emptyTitle.TextAlignment = TextAlignment.Center;
        emptyBody.TextAlignment = TextAlignment.Center;
        viewingLabel.VerticalAlignment = VerticalAlignment.Center;
        originButton.TitleSize = Token.Font.Caption;
        originButton.TitleWeight = Token.Font.Medium;
        originButton.TitleDigits = true;
        originButton.TitleColor = Token.Colors.TextMuted;
        retryButton.Help = "Ask for this code again";
        latestButton.Help = "Go to the newest version";

        previousButton.Click += () => StepVersion(-1);
        nextButton.Click += () => StepVersion(1);
        copyAllButton.Click += () => CopyDisplayedCode();
        closeButton.Click += () => Close?.Invoke();
        originButton.Click += () => OriginClicked?.Invoke();
        retryButton.Click += () => RetryRequested?.Invoke();
        latestButton.Click += ShowLatestVersion;
        waitTimer.Tick += (_, _) => RefreshSubtitle();

        scroll.Content = document;
        scroll.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromSeconds(Token.Motion.FadeIn) } };
        Children.Add(surface);
        Children.Add(titleLabel);
        Children.Add(subtitleLabel);
        Children.Add(positionLabel);
        Children.Add(previousButton);
        Children.Add(nextButton);
        Children.Add(copyAllButton);
        Children.Add(closeButton);
        Children.Add(copiedLabel);
        Children.Add(originButton);
        Children.Add(scroll);
        Children.Add(stepSkeleton);
        Children.Add(emptyTitle);
        Children.Add(emptyBody);
        Children.Add(banner);
        Children.Add(bannerLabel);
        Children.Add(retryButton);
        Children.Add(viewingLabel);
        Children.Add(latestButton);
        ApplyTransparency();
        Render();
    }

    public IReadOnlyList<Version> Versions => versions;

    public int? DisplayedIndex { get; private set; }

    public IReadOnlyList<CodingInterviewStep> Steps { get; private set; } = Array.Empty<CodingInterviewStep>();

    public IReadOnlyList<CodeFileSection> Sections { get; private set; } = Array.Empty<CodeFileSection>();

    public IReadOnlyList<CodeFileView> FileViews => fileViews;

    public int CurrentStep { get; private set; } = 1;

    public bool ChangesLocked { get; private set; }

    public Version? Displayed => DisplayedIndex is { } index && index >= 0 && index < versions.Count ? versions[index] : null;

    private bool IsShowingLatest => DisplayedIndex == versions.Count - 1;

    public bool IsStreaming => versions.Count > 0 && versions[^1].IsStreaming;

    public string AllCode => string.Join("\n\n", Sections.Select(section => section.Code));

    public bool CanStep => Steps.Count > 1 && !usesFallback;

    // What is on screen, for the UI driver.
    public string Subtitle => subtitleLabel.Text ?? "";
    public string? Origin => originButton.IsVisible ? originButton.Title : null;
    public string? VersionPosition => positionLabel.IsVisible ? positionLabel.Text : null;
    public string? Banner => banner.IsVisible ? bannerLabel.Text : null;
    public bool ShowsRetry => banner.IsVisible && retryButton.IsVisible;
    public bool ShowsSkeleton => stepSkeleton.IsVisible;
    public bool ShowsEmpty => emptyTitle.IsVisible;
    public string? FallbackText => usesFallback ? fallbackText : null;
    public string? Viewing => viewingLabel.IsVisible ? viewingLabel.Text : null;
    public IReadOnlyList<HoverButton> Chips => chips;
    public HoverButton CopyAllButton => copyAllButton;
    public HoverButton CloseButton => closeButton;
    public HoverButton OriginButton => originButton;
    public ScrollViewer Scroll => scroll;

    /// While the interview runs the panel is for typing from, so the copy controls stay out of the way.
    public bool IsLive
    {
        get => isLive;
        set
        {
            if (isLive == value) return;
            isLive = value;
            if (Steps.Count > 0 && !usesFallback) ShowSteps(Steps);
            RenderHeaderAndFooter();
        }
    }

    public void BeginVersion(CodingTaskInfo info)
    {
        ChangesLocked = false;
        var index = versions.FindIndex(version => version.Id == info.TurnId);
        if (index >= 0)
        {
            var version = versions[index];
            if (!info.IsRefinement) version = version with { Markdown = "", Outcome = CodingTaskOutcome.Complete };
            versions[index] = version with { IsStreaming = true, StartedAt = Now() };
            DisplayedIndex = index;
        }
        else
        {
            versions.Add(new Version(info.TurnId, info.Request, "", CodingTaskOutcome.Complete, true, info.AllowsChanges, Now()));
            if (versions.Count > MaxVersions) versions.RemoveAt(0);
            DisplayedIndex = versions.Count - 1;
        }
        Render();
    }

    public void UpdateLatest(string markdown)
    {
        if (versions.Count == 0)
        {
            versions.Add(new Version(Guid.NewGuid(), "", "", CodingTaskOutcome.Complete, false, false, Now()));
            DisplayedIndex = 0;
        }
        versions[^1] = versions[^1] with { Markdown = markdown };
        if (IsShowingLatest) Render();
        else RenderHeaderAndFooter();
    }

    public void FinishLatest(CodingTaskOutcome outcome)
    {
        if (versions.Count == 0) return;
        versions[^1] = versions[^1] with { IsStreaming = false, Outcome = outcome };
        Render();
    }

    public bool SelectVersion(Guid id)
    {
        var index = versions.FindIndex(version => version.Id == id);
        if (index < 0) return false;
        if (DisplayedIndex == index) return true;
        DisplayedIndex = index;
        Render();
        return true;
    }

    public void Clear()
    {
        versions.Clear();
        DisplayedIndex = null;
        ChangesLocked = false;
        Render();
    }

    /// After the interview ends nothing more can be asked for, so the follow-up actions go.
    public void LockChanges()
    {
        if (ChangesLocked) return;
        ChangesLocked = true;
        Render();
    }

    private void ShowLatestVersion()
    {
        if (versions.Count == 0) return;
        DisplayedIndex = versions.Count - 1;
        Render();
    }

    private void StepVersion(int delta)
    {
        if (DisplayedIndex is not { } current) return;
        var target = current + delta;
        if (target < 0 || target >= versions.Count) return;
        DisplayedIndex = target;
        Render();
    }

    private void Render()
    {
        var markdown = Displayed?.Markdown ?? "";
        var trimmed = markdown.Trim();
        var progress = CodingSteps.ParseStreaming(markdown);
        if (progress.Steps.Count == 0 && trimmed.Length > 0 && !LooksLikeStepHeader(trimmed)) ShowFallback(trimmed);
        else ShowSteps(progress.Steps);
        RenderHeaderAndFooter();
        Relayout();
        Rendered?.Invoke();
    }

    private void RenderHeaderAndFooter()
    {
        RefreshSubtitle();
        RefreshStatus();
        RefreshBannerAndFooter();
        copyAllButton.IsEnabled = Steps.Count > 0;
        copyAllButton.IsVisible = Steps.Count > 0 && !isLive;
        var copyLabel = Sections.Count > 1 ? "Copy all files" : "Copy code";
        copyAllButton.Label = copyLabel;
        copyAllButton.Help = $"{copyLabel} (Ctrl+Alt+C)";
        Relayout();
    }

    /// The first word of a reply that is still arriving is not yet proof it is not steps.
    private static bool LooksLikeStepHeader(string text) =>
        System.Text.RegularExpressions.Regex.IsMatch(text, @"^\s*STEP\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
        || "step".StartsWith(text.ToLowerInvariant(), StringComparison.Ordinal);

    private void ShowSteps(IReadOnlyList<CodingInterviewStep> newSteps)
    {
        usesFallback = false;
        fallbackView.IsVisible = false;
        Steps = newSteps;
        Sections = CodeFiles.GroupCodeSteps(newSteps);
        var numbersSteps = newSteps.Count > 1;
        if (Displayed?.Id != steppedVersionId)
        {
            steppedVersionId = Displayed?.Id;
            CurrentStep = 1;
        }
        CurrentStep = Math.Min(Math.Max(1, CurrentStep), Math.Max(1, newSteps.Count));
        int? focused = CanStep ? CurrentStep : null;
        var showsCopy = Sections.Count > 1 && !isLive;
        var previous = PreviousVersionSections();
        for (var index = 0; index < Sections.Count; index++)
        {
            var section = Sections[index];
            var changes = LineChanges(section, previous);
            if (index < fileViews.Count)
            {
                fileViews[index].Update(section, numbersSteps, showsCopy, changes, focused);
            }
            else
            {
                var view = new CodeFileView(section, numbersSteps, showsCopy, changes, focused);
                document.Children.Add(view);
                fileViews.Add(view);
            }
        }
        while (fileViews.Count > Sections.Count)
        {
            document.Children.Remove(fileViews[^1]);
            fileViews.RemoveAt(fileViews.Count - 1);
        }
        document.Relayout();
    }

    /// Moves the step being typed. Returns false when there is nothing to step through.
    public bool MoveStep(bool forward)
    {
        if (!CanStep) return false;
        var next = Math.Min(Math.Max(1, CurrentStep + (forward ? 1 : -1)), Steps.Count);
        if (next == CurrentStep) return true;
        CurrentStep = next;
        ShowSteps(Steps);
        RefreshSubtitle();
        Relayout();
        UpdateLayout();
        foreach (var view in fileViews)
        {
            if (view.RectForStep(next) is not { } rect) continue;
            var top = document.FrameOf(view).Y + rect.Y - Token.Space.Md;
            var furthest = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
            scroll.Offset = new Vector(0, Math.Min(Math.Max(0, top), furthest));
            break;
        }
        return true;
    }

    private List<CodeFileSection> PreviousVersionSections()
    {
        if (DisplayedIndex is not { } index || index <= 0 || index >= versions.Count || versions[index].IsStreaming || versions[index - 1].IsStreaming)
        {
            return new List<CodeFileSection>();
        }
        return CodeFiles.GroupCodeSteps(CodingSteps.ParseStreaming(versions[index - 1].Markdown).Steps);
    }

    private CodeLineChanges? LineChanges(CodeFileSection section, List<CodeFileSection> previous)
    {
        var earlier = previous.FirstOrDefault(candidate => candidate.FileName == section.FileName)
            ?? (previous.Count == 1 && Sections.Count == 1 ? previous[0] : null);
        return earlier is null ? null : CodeFiles.LineChanges(earlier.Code, section.Code);
    }

    private void ShowFallback(string text)
    {
        usesFallback = true;
        Steps = Array.Empty<CodingInterviewStep>();
        Sections = Array.Empty<CodeFileSection>();
        foreach (var view in fileViews) document.Children.Remove(view);
        fileViews.Clear();
        fallbackView.IsVisible = true;
        fallbackText = text;
        MarkdownText.Fill(fallbackView.Inlines!, text, MarkdownLook.Panel);
        document.Relayout();
    }

    private void RefreshSubtitle()
    {
        if (Displayed is not { } version)
        {
            Ui.SetText(subtitleLabel, "");
            return;
        }
        string summary;
        if (usesFallback)
        {
            summary = version.IsStreaming ? "Working…" : "Analysis";
        }
        else if (Steps.Count == 0)
        {
            summary = version.IsStreaming ? WaitProgress.CodeStatus((Now() - version.StartedAt).TotalSeconds, Token.Text.Separator) : "";
        }
        else
        {
            var language = Steps.Select(step => CodingSteps.LanguageDisplayName(step.Language)).FirstOrDefault(name => name is not null) ?? DefaultLanguageName();
            summary = Steps.Count > 1 ? $"{language}{Token.Text.Separator}step {CurrentStep} of {Steps.Count}" : language;
            if (version.IsStreaming) summary += $"{Token.Text.Separator}writing";
        }
        Ui.SetText(subtitleLabel, summary);
        var request = version.Title.Trim();
        Ui.Tip(subtitleLabel, request.Length == 0 ? summary : request);
        Relayout();
    }

    /// The question the displayed code answers, as a way back to it. Null hides it.
    public void SetOrigin(string? text)
    {
        if (originButton.Title == (text ?? "") && originButton.IsVisible == (text is not null)) return;
        originButton.IsVisible = text is not null;
        if (text is not null)
        {
            originButton.Title = text;
            originButton.Apply(Token.ControlStyle.Ghost);
            originButton.Label = text;
            originButton.Help = $"Go to the question this code answers: {text}";
        }
        Relayout();
    }

    private void RefreshStatus()
    {
        var showsStatus = Steps.Count == 0 && !usesFallback && (Displayed?.IsStreaming ?? false);
        var showsEmpty = Displayed is null;
        var wasShowingStatus = stepSkeleton.IsVisible;
        var wasHidden = !scroll.IsVisible;
        emptyTitle.IsVisible = showsEmpty;
        emptyBody.IsVisible = showsEmpty;
        scroll.IsVisible = !(showsStatus || showsEmpty);
        stepSkeleton.IsVisible = showsStatus;
        if (!showsStatus && wasShowingStatus && wasHidden && scroll.IsVisible && !Token.Motion.ReducesMotion())
        {
            var transitions = scroll.Transitions;
            scroll.Transitions = null;
            scroll.Opacity = 0;
            scroll.Transitions = transitions;
            scroll.Opacity = 1;
        }
        UpdateWaitTimer();
    }

    private void UpdateWaitTimer()
    {
        if (stepSkeleton.IsVisible && attached) waitTimer.Start();
        else waitTimer.Stop();
    }

    private void RefreshBannerAndFooter()
    {
        var version = Displayed;
        if (version is not null && IsShowingLatest && !version.Outcome.IsComplete && !version.IsStreaming)
        {
            Ui.SetText(bannerLabel, $"Incomplete. {version.Outcome.Reason}");
            banner.IsVisible = true;
            bannerLabel.IsVisible = true;
            retryButton.IsVisible = !ChangesLocked;
        }
        else
        {
            banner.IsVisible = false;
            bannerLabel.IsVisible = false;
            retryButton.IsVisible = false;
        }

        foreach (var chip in chips) Children.Remove(chip);
        chips.Clear();
        chipOpacity = 1;
        viewingLabel.IsVisible = false;
        latestButton.IsVisible = false;
        footerShown = false;
        if (version is null) return;
        if (!IsShowingLatest)
        {
            Ui.SetText(viewingLabel, $"Version {(DisplayedIndex ?? 0) + 1} of {versions.Count}");
            viewingLabel.IsVisible = true;
            latestButton.IsVisible = true;
            footerShown = true;
        }
        else if (version.AllowsChanges && !ChangesLocked && !version.IsStreaming && Steps.Count > 0 && version.Outcome.IsComplete)
        {
            var language = Steps.Select(step => CodingSteps.LanguageDisplayName(step.Language)).FirstOrDefault(name => name is not null);
            foreach (var action in CodeQuickActions.AllCases)
            {
                var instruction = action.Instruction(language);
                var chip = HoverButton.Chip(action.Title(language), Token.ControlStyle.FollowUp, click: () => QuickAction?.Invoke(instruction));
                chip.Help = $"Ask for: {instruction}";
                chip.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromSeconds(Token.Motion.FadeIn) } };
                chips.Add(chip);
                Children.Add(chip);
            }
            footerShown = true;
            chipOpacity = quickActionsRevealed ? 1 : 0;
            ApplyChipOpacity();
        }
    }

    /// The follow-up actions show while the pointer is near them, so they do not sit over the code.
    public void SetQuickActionsRevealed(bool revealed)
    {
        quickActionsRevealed = revealed;
        if (chips.Count == 0) return;
        chipOpacity = revealed ? 1 : 0;
        ApplyChipOpacity();
    }

    private void ApplyChipOpacity()
    {
        foreach (var chip in chips)
        {
            var transitions = chip.Transitions;
            if (Token.Motion.ReducesMotion()) chip.Transitions = null;
            chip.Opacity = chipOpacity;
            chip.Transitions = transitions;
            chip.IsHitTestVisible = chipOpacity > 0;
        }
    }

    public void ApplyTransparency()
    {
        Ui.Surface(surface, Token.SurfaceStyle.CodePanel);
        Ui.Surface(banner, Token.SurfaceStyle.Banner);
        foreach (var view in fileViews) view.ApplyTransparency();
        stepSkeleton.ApplyTransparency();
    }

    public bool CopyDisplayedCode()
    {
        if (AllCode.Length == 0) return false;
        Ui.CopyText(this, AllCode);
        copyAllButton.Icon = Icons.Checkmark;
        copyAllButton.GlyphColor = Token.Hue.Success.Text;
        copiedLabel.IsVisible = true;
        Relayout();
        copyReset?.Stop();
        copyReset = new DispatcherTimer(TimeSpan.FromSeconds(Token.Motion.FeedbackHold), DispatcherPriority.Normal, (_, _) =>
        {
            copyReset?.Stop();
            copyAllButton.Icon = Icons.Copy;
            copyAllButton.GlyphColor = null;
            copiedLabel.IsVisible = false;
            Relayout();
        });
        copyReset.Start();
        return true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        attached = true;
        UpdateWaitTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        attached = false;
        UpdateWaitTimer();
    }

    private double footerMidY;

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var y = e.GetPosition(this).Y;
        var near = chips.Count > 0 && Math.Abs(y - footerMidY) <= QuickActionsZone / 2;
        if (near != quickActionsRevealed) SetQuickActionsRevealed(near);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (quickActionsRevealed) SetQuickActionsRevealed(false);
    }

    private static double ChipWidth(HoverButton chip) => Ui.TextWidth(chip.Title, Token.Font.Caption, Token.Font.Semibold) + 24;

    private List<List<HoverButton>> ChipRows(double width)
    {
        var rows = new List<List<HoverButton>> { new() };
        var x = 0.0;
        foreach (var chip in chips)
        {
            var chipWidth = ChipWidth(chip);
            if (x > 0 && x + chipWidth > width)
            {
                rows.Add(new List<HoverButton>());
                x = 0;
            }
            rows[^1].Add(chip);
            x += chipWidth + Token.Space.Md;
        }
        return rows;
    }

    protected override void LayoutFrames(Size size)
    {
        Background ??= Brushes.Transparent;
        Place(surface, 0, 0, size.Width, size.Height);
        var inset = ContentInset;
        var small = Token.Sizing.ControlSmall;
        var closeX = size.Width - inset - small;
        var copyX = closeX - small - Token.Space.Sm;
        const double buttonsTop = 15;
        Place(closeButton, closeX, buttonsTop, small, small);
        if (copyAllButton.IsVisible) Place(copyAllButton, copyX, buttonsTop, small, small);

        var showsStepper = versions.Count > 1;
        var headingOnly = Steps.Count > 0 && !showsStepper;
        titleLabel.IsVisible = !headingOnly;
        var titleWidth = WidthOf(titleLabel) + 6;
        if (titleLabel.IsVisible) Place(titleLabel, inset, 17, titleWidth, 16);
        previousButton.IsVisible = showsStepper;
        nextButton.IsVisible = showsStepper;
        positionLabel.IsVisible = showsStepper;
        if (showsStepper)
        {
            var index = DisplayedIndex ?? 0;
            Ui.SetText(positionLabel, $"{index + 1}/{versions.Count}");
            var stepperX = inset + titleWidth + Token.Space.Xs;
            Place(previousButton, stepperX, 12, 24, 24);
            Place(positionLabel, stepperX + 24, 17, 44, 16);
            Place(nextButton, stepperX + 68, 12, 24, 24);
            previousButton.IsEnabled = index > 0;
            previousButton.Opacity = index > 0 ? 1 : Token.State.DisabledAlpha;
            nextButton.IsEnabled = index < versions.Count - 1;
            nextButton.Opacity = index < versions.Count - 1 ? 1 : Token.State.DisabledAlpha;
        }

        var copiedWidth = copiedLabel.IsVisible ? 52.0 : 0;
        if (copiedLabel.IsVisible) Place(copiedLabel, copyX - Token.Space.Xs - copiedWidth, buttonsTop + small / 2 - 8, copiedWidth, 16);
        var buttonsX = copyX - Token.Space.Sm - copiedWidth;
        var subtitleY = headingOnly ? buttonsTop + small / 2 - 11.5 : 37;
        var subtitleNatural = WidthOf(subtitleLabel) + 6;
        var originRoom = buttonsX - inset - subtitleNatural - Token.Space.Xs;
        var originNatural = Ui.TextWidth(originButton.Title, Token.Font.Caption, Token.Font.Medium, digits: true) + 16;
        var originWidth = !originButton.IsVisible || originRoom < 60 ? 0 : Math.Min(originNatural, originRoom);
        var subtitleSpace = Math.Max(40, buttonsX - inset - (originWidth > 0 ? originWidth + Token.Space.Xs : 0));
        var subtitleWidth = originWidth > 0 ? Math.Min(subtitleSpace, subtitleNatural) : subtitleSpace;
        Place(subtitleLabel, inset, subtitleY, subtitleWidth, 23);
        if (originWidth > 0) Place(originButton, inset + subtitleWidth + Token.Space.Xs, subtitleY, originWidth, 23);

        var top = headingOnly ? HeaderHeight - 17 - Token.Space.Xs : HeaderHeight;
        if (banner.IsVisible)
        {
            var labelWidth = size.Width - inset * 2 - inset - 58 - Token.Space.Md * 2;
            var labelHeight = HeightOf(bannerLabel, labelWidth);
            var bannerHeight = Math.Max(36, labelHeight + Token.Space.Md * 2);
            var bannerTop = top + Token.Space.Xs;
            Place(banner, inset, bannerTop, size.Width - inset * 2, bannerHeight);
            Place(bannerLabel, inset * 2, bannerTop + (bannerHeight - labelHeight) / 2, labelWidth, labelHeight);
            if (retryButton.IsVisible) Place(retryButton, size.Width - inset - 58 - Token.Space.Md, bannerTop + (bannerHeight - 24) / 2, 58, 24);
            top += Token.Space.Xs + bannerHeight + Token.Space.Xs;
        }

        var chipRows = chips.Count > 0 ? ChipRows(size.Width - inset * 2) : new List<List<HoverButton>>();
        var rowCount = latestButton.IsVisible ? 1 : chipRows.Count;
        var footerHeight = footerShown ? FooterPadding * 2 + rowCount * small + Math.Max(0, rowCount - 1) * ChipRowGap : 0;
        var bottom = footerShown ? footerHeight : 6;
        var bodyHeight = Math.Max(40, size.Height - top - bottom);

        var skeletonHeight = Math.Min(CodeStepSkeletonView.PreferredHeight, Math.Max(0, bodyHeight - inset * 0.5));
        if (stepSkeleton.IsVisible) Place(stepSkeleton, inset, top + inset * 0.5, size.Width - inset * 2, skeletonHeight);

        if (emptyTitle.IsVisible)
        {
            var emptyWidth = Math.Min(size.Width - 48, 300);
            var emptyBodyHeight = HeightOf(emptyBody, emptyWidth);
            var blockHeight = 20 + Token.Space.Sm + emptyBodyHeight;
            var blockTop = top + bodyHeight / 2 - blockHeight / 2;
            Place(emptyTitle, (size.Width - emptyWidth) / 2, blockTop, emptyWidth, 20);
            Place(emptyBody, (size.Width - emptyWidth) / 2, blockTop + 20 + Token.Space.Sm, emptyWidth, emptyBodyHeight);
        }

        documentContentHeight = document.ContentHeight(size.Width);
        // The follow-up actions sit right under the code, not at the foot of an empty panel.
        var slack = footerShown && !latestButton.IsVisible ? Math.Max(0, bodyHeight - documentContentHeight) : 0;
        if (scroll.IsVisible) Place(scroll, 0, top, size.Width, bodyHeight - slack);

        if (!footerShown) return;
        var footerTop = size.Height - footerHeight - slack;
        footerMidY = footerTop + footerHeight / 2;
        var rowTop = footerTop + FooterPadding;
        if (latestButton.IsVisible)
        {
            Place(viewingLabel, inset, rowTop, WidthOf(viewingLabel) + 4, small);
            var width = Ui.TextWidth(latestButton.Title, Token.Font.Caption, Token.Font.Semibold) + 24;
            Place(latestButton, size.Width - inset - width, rowTop, width, small);
            return;
        }
        for (var row = 0; row < chipRows.Count; row++)
        {
            var x = inset;
            foreach (var chip in chipRows[row])
            {
                var width = ChipWidth(chip);
                Place(chip, x, rowTop + row * (small + ChipRowGap), width, small);
                x += width + Token.Space.Md;
            }
        }
    }

    /// The scrolling content: the files one under another, or the analysis when the reply is not steps.
    private sealed class Document : FrameView
    {
        private readonly CodePanelView panel;

        public Document(CodePanelView panel)
        {
            this.panel = panel;
            Children.Add(panel.fallbackView);
        }

        private static double InnerWidth(double width) => Math.Max(120, width - ContentInset * 2);

        public double ContentHeight(double width)
        {
            var y = ContentInset * 0.5;
            if (panel.usesFallback)
            {
                panel.fallbackView.Measure(new Size(InnerWidth(width), double.PositiveInfinity));
                return y + Math.Max(20, panel.fallbackView.DesiredSize.Height) + ContentInset;
            }
            foreach (var view in panel.fileViews) y += view.PreferredHeight + CardSpacing;
            return y + ContentInset - CardSpacing;
        }

        protected override Size PreferredSize(Size available) => new(available.Width, ContentHeight(available.Width));

        protected override void LayoutFrames(Size size)
        {
            var inner = InnerWidth(size.Width);
            var y = ContentInset * 0.5;
            if (panel.usesFallback)
            {
                panel.fallbackView.Measure(new Size(inner, double.PositiveInfinity));
                Place(panel.fallbackView, ContentInset, y, inner, Math.Max(20, panel.fallbackView.DesiredSize.Height));
                return;
            }
            foreach (var view in panel.fileViews)
            {
                Place(view, ContentInset, y, inner, view.PreferredHeight);
                y += view.PreferredHeight + CardSpacing;
            }
        }
    }
}
