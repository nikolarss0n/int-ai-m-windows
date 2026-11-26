using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace InterviewMaster.Presentation;

/// A window without a title bar is moved by its background and sized by its edges, as
/// `isMovableByWindowBackground` and a resizable window are on macOS.
public static class WindowDrag
{
    private const double EdgeWidth = 6;

    /// A press that no control took moves the window.
    public static void MoveByBackground(Window window)
    {
        window.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (e.Handled || !e.GetCurrentPoint(window).Properties.IsLeftButtonPressed) return;
            if (IsInteractive(e.Source as Visual, window)) return;
            window.BeginMoveDrag(e);
        }, RoutingStrategies.Bubble);
    }

    /// A press on the outer few points sizes the window instead of reaching what is under it.
    public static void ResizeByEdges(Window window)
    {
        window.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (!window.CanResize || !e.GetCurrentPoint(window).Properties.IsLeftButtonPressed) return;
            if (Edge(e.GetPosition(window), window.Bounds.Size) is not { } edge) return;
            e.Handled = true;
            window.BeginResizeDrag(edge, e);
        }, RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.PointerMovedEvent, (_, e) =>
        {
            if (!window.CanResize) return;
            var edge = Edge(e.GetPosition(window), window.Bounds.Size);
            window.Cursor = edge is { } found ? new Cursor(CursorFor(found)) : null;
        }, RoutingStrategies.Tunnel);
    }

    public static WindowEdge? Edge(Point point, Size size)
    {
        var left = point.X < EdgeWidth;
        var right = point.X >= size.Width - EdgeWidth;
        var top = point.Y < EdgeWidth;
        var bottom = point.Y >= size.Height - EdgeWidth;
        if (top && left) return WindowEdge.NorthWest;
        if (top && right) return WindowEdge.NorthEast;
        if (bottom && left) return WindowEdge.SouthWest;
        if (bottom && right) return WindowEdge.SouthEast;
        if (left) return WindowEdge.West;
        if (right) return WindowEdge.East;
        if (top) return WindowEdge.North;
        if (bottom) return WindowEdge.South;
        return null;
    }

    private static StandardCursorType CursorFor(WindowEdge edge) => edge switch
    {
        WindowEdge.NorthWest => StandardCursorType.TopLeftCorner,
        WindowEdge.NorthEast => StandardCursorType.TopRightCorner,
        WindowEdge.SouthWest => StandardCursorType.BottomLeftCorner,
        WindowEdge.SouthEast => StandardCursorType.BottomRightCorner,
        WindowEdge.West or WindowEdge.East => StandardCursorType.SizeWestEast,
        _ => StandardCursorType.SizeNorthSouth
    };

    /// Something the person presses for its own sake: a button, a field, a list they can select in.
    private static bool IsInteractive(Visual? source, Window window)
    {
        for (var visual = source; visual is not null && !ReferenceEquals(visual, window); visual = visual.GetVisualParent())
        {
            if (visual is InputElement { Focusable: true } or InputElement { Cursor: not null }) return true;
        }
        return false;
    }
}
