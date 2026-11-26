namespace InterviewMaster.Domain;

public static class ScreenshotPresentation
{
    /// Live Timeline hides the screenshot gallery. Analysis results must use the overlay even when the
    /// main window is visible.
    public static bool ShouldPresentScreenshotAnalysisOverlay(bool mainWindowVisible) => true;

    public static string LiveScreenshotStatusText(int count) => count switch
    {
        0 => "",
        1 => $"1 screenshot · {PlatformCopy.AnalyzeShortcut} analyze",
        _ => $"{count} screenshots · {PlatformCopy.AnalyzeShortcut} analyze"
    };
}
