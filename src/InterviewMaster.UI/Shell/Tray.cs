using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace InterviewMaster.Presentation;

/// The app's mark as a picture, for the tray and the windows' corner.
public static class AppIcon
{
    public static RenderTargetBitmap Bitmap(int side)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(side, side), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext()) AppIconView.Draw(context, side);
        return bitmap;
    }

    public static WindowIcon Make(int side) => new(Bitmap(side));
}

/// The icon in the notification area and its menu: the Windows place for the macOS status item.
public sealed class Tray : IDisposable
{
    private readonly TrayIcon icon;
    private readonly List<(MenuEntry Entry, NativeMenuItem Item)> rows = new();

    public Tray(AppHost host)
    {
        icon = new TrayIcon
        {
            ToolTipText = "Interview Master",
            Icon = AppIcon.Make(64),
            Menu = Build(host.Menu),
            IsVisible = true,
        };
        icon.Clicked += (_, _) => host.ToggleFromTray();
        host.MenuChanged += Sync;
        Sync();
    }

    private NativeMenu Build(IReadOnlyList<MenuEntry> entries)
    {
        var menu = new NativeMenu();
        foreach (var entry in entries)
        {
            if (entry.IsSeparator)
            {
                menu.Items.Add(new NativeMenuItemSeparator());
                continue;
            }
            var item = new NativeMenuItem(entry.Title);
            if (entry.Checked is not null) item.ToggleType = MenuItemToggleType.CheckBox;
            if (entry.Children.Count > 0) item.Menu = Build(entry.Children);
            else if (entry.Run is { } run) item.Click += (_, _) => run();
            rows.Add((entry, item));
            menu.Items.Add(item);
        }
        return menu;
    }

    private void Sync()
    {
        foreach (var (entry, item) in rows)
        {
            item.IsEnabled = entry.Enabled;
            if (entry.Checked is { } isChecked) item.IsChecked = isChecked;
        }
    }

    public void Dispose() => icon.Dispose();
}
