using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using InterviewMaster.Presentation;

namespace InterviewMaster.UiDriver;

/// What one state looks like: a picture, the words on it and where the main parts are.
public static class Snapshot
{
    public static void Save(Window window, MainView main, string directory, string name)
    {
        window.UpdateLayout();
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)(window.Width * 2), (int)(window.Height * 2)), new Vector(192, 192));
        bitmap.Render(window);
        using (var stream = File.Create(Path.Combine(directory, name + ".png"))) bitmap.Save(stream);
        File.WriteAllText(Path.Combine(directory, name + ".txt"), Geometry(window, main) + "\n---\n" + string.Join("\n", Texts(main)));
    }

    /// A panel the app would open in a window of its own.
    public static void SavePanel(Control content, Size size, string directory, string name)
    {
        var host = new Window { Width = size.Width, Height = size.Height, Content = content };
        host.Show();
        host.UpdateLayout();
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)(size.Width * 2), (int)(size.Height * 2)), new Vector(192, 192));
        bitmap.Render(host);
        using (var stream = File.Create(Path.Combine(directory, name + ".png"))) bitmap.Save(stream);
        File.WriteAllText(Path.Combine(directory, name + ".txt"), string.Join("\n", Texts(content)));
        host.Content = null;
        host.Close();
    }

    private static string Geometry(Window window, MainView main)
    {
        string Frame(Control control)
        {
            var origin = control.TranslatePoint(new Point(0, 0), main) ?? new Point(-1, -1);
            return $"{Math.Round(origin.X)},{Math.Round(origin.Y)} {Math.Round(control.Bounds.Width)}x{Math.Round(control.Bounds.Height)} hidden={!control.IsVisible}";
        }
        var focus = main.Interview;
        return string.Join("\n", new[]
        {
            $"window {window.Width}x{window.Height}",
            $"header {Frame(focus.Header)}",
            $"talkStrip {Frame(focus.TalkStrip)}",
            $"burstStrip {Frame(focus.Strip)}",
            $"answerScroll {Frame(focus.BodyScroll)}",
            $"briefing {Frame(focus.Briefing)}",
            $"answer {Frame(focus.Answer)}",
            $"codePanel {Frame(focus.CodePanel)}",
            $"commandBar {Frame(main.Bar)}",
        });
    }

    /// Every word a person could read, top of the tree first.
    public static List<string> Texts(Visual root)
    {
        var lines = new List<string>();
        Walk(root, lines);
        return lines;
    }

    private static void Walk(Visual visual, List<string> lines)
    {
        if (!visual.IsVisible) return;
        switch (visual)
        {
            case HoverButton button:
                if (!string.IsNullOrEmpty(button.Title)) lines.Add($"[btn] {button.Title}{(button.Opacity < 0.05 ? " (hidden)" : "")}");
                else if (!string.IsNullOrEmpty(button.Label)) lines.Add($"[icon] {button.Label}");
                return;
            case BriefingView briefing:
                foreach (var child in briefing.GetVisualChildren()) Walk(child, lines);
                lines.AddRange(briefing.HiddenPoints.Select(point => $"[clipped] {point}"));
                return;
            case CodeFileView file:
                foreach (var child in file.GetVisualChildren()) Walk(child, lines);
                lines.Add("[text] " + string.Join(" ⏎ ", file.Rows.Select(row => string.Concat(row.Pieces.Select(piece => piece.Text)))));
                return;
            case TextBox box:
                // A field that hides what is typed holds a secret: say that it is filled, never with what.
                if (box.PasswordChar != default) lines.Add($"[secret] {(string.IsNullOrEmpty(box.Text) ? "empty" : "filled")}");
                else if (!string.IsNullOrEmpty(box.Text)) lines.Add($"[text] {box.Text.Replace("\n", " ⏎ ")}");
                return;
            case TextBlock block:
                var text = TextOf(block);
                if (text.Length > 0) lines.Add($"[label] {text.Replace("\n", " ⏎ ")}");
                return;
        }
        foreach (var child in visual.GetVisualChildren()) Walk(child, lines);
    }

    private static string TextOf(TextBlock block)
    {
        if (!string.IsNullOrEmpty(block.Text)) return block.Text;
        if (block.Inlines is not { Count: > 0 } inlines) return "";
        var text = new StringBuilder();
        foreach (var inline in inlines)
        {
            if (inline is Run run) text.Append(run.Text);
            else if (inline is LineBreak) text.Append('\n');
        }
        return text.ToString();
    }
}
