using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;

namespace AVASvgMaker.Views;

/// <summary>
/// Puts a dialog in the middle of the window it belongs to, and keeps it on the screen.
///
/// Avalonia's own <c>CenterOwner</c> does the first half and not the second: it centres on
/// wherever the owner says it is, and a window manager is free to say something surprising.
/// Under Hyprland a window on a workspace you are not looking at is parked far off the
/// top-left corner, and the position the application is told can stay that way after the
/// workspace comes back - so a dialog centred on it is parked off the screen too. Modal, with
/// nothing to click, and no way to answer it.
///
/// That is not a hypothetical. It is why File -> Exit could appear to hang: the prompt asking
/// whether to save was open the whole time, 300 pixels off the top-left corner.
/// </summary>
public static class DialogPlacement
{
    public static Task ShowCentred(this Window dialog, Window owner)
    {
        Arrange(dialog, owner);
        return dialog.ShowDialog(owner);
    }

    public static Task<T> ShowCentred<T>(this Window dialog, Window owner)
    {
        Arrange(dialog, owner);
        return dialog.ShowDialog<T>(owner);
    }

    private static void Arrange(Window dialog, Window owner)
    {
        dialog.WindowStartupLocation = WindowStartupLocation.Manual;

        // Placed once it is open, because a dialog that grows to fit its words does not know
        // how tall it is before then - and half of these do.
        dialog.Opened += (_, _) => Place(dialog, owner);
    }

    private static void Place(Window dialog, Window owner)
    {
        var screen = dialog.Screens.ScreenFromWindow(owner)
                     ?? dialog.Screens.ScreenFromWindow(dialog)
                     ?? dialog.Screens.Primary;

        if (screen is null)
            return;

        var work = screen.WorkingArea;

        // The screen's scale, not the window's: a window that has only just opened still
        // reports a scale of 1, and a dialog measured at half its size is centred wrongly by
        // half of the difference.
        var size = Pixels(dialog, screen.Scaling);

        if (size.Width <= 0 || size.Height <= 0)
            return;

        var over = new PixelRect(owner.Position, Pixels(owner, screen.Scaling));
        var heart = new PixelPoint(over.X + over.Width / 2, over.Y + over.Height / 2);

        // Centred on the owner when the middle of the owner is somewhere you can see, and on
        // the screen when it is not. Merely overlapping the screen is not enough to centre on:
        // a window hanging off the top-left corner overlaps it, and a dialog centred on that
        // one then gets pinned into the corner by the clamp below, which is the same
        // unanswerable prompt in a different place.
        var middle = work.Contains(heart) ? over : work;

        var x = middle.X + (middle.Width - size.Width) / 2;
        var y = middle.Y + (middle.Height - size.Height) / 2;

        // Clamped as well as centred, so a dialog larger than the screen, or an owner sitting
        // at its very edge, still leaves something to click.
        dialog.Position = new PixelPoint(
            Math.Clamp(x, work.X, Math.Max(work.X, work.Right - size.Width)),
            Math.Clamp(y, work.Y, Math.Max(work.Y, work.Bottom - size.Height)));
    }

    /// <summary>A window's size in screen pixels, which is what a position is measured in.</summary>
    private static PixelSize Pixels(Window window, double scaling) => new(
        (int)Math.Round(window.Bounds.Width * scaling),
        (int)Math.Round(window.Bounds.Height * scaling));
}
