using ScreenGrab;
using System.Drawing;
using System.Windows;

namespace STranslate.Core;

public class Screenshot(Settings settings) : IScreenshot
{
    private const int DefaultCaptureDelayMs = 150;

    public Bitmap? GetScreenshot()
    {
        if (ScreenGrabber.IsCapturing)
            return default;
        var bitmap = ScreenGrabber.CaptureDialog(settings.ShowScreenshotAuxiliaryLines);
        if (bitmap == null)
            return default;
        return bitmap;
    }

    public async Task<Bitmap?> GetScreenshotAsync()
    {
        if (ScreenGrabber.IsCapturing)
            return default;

        if (App.Current.MainWindow.Visibility == Visibility.Visible &&
            !App.Current.MainWindow.Topmost)
            App.Current.MainWindow.Visibility = Visibility.Collapsed;

        // Allow UI to update before capturing
        await Task.Delay(DefaultCaptureDelayMs);

        var bitmap = await ScreenGrabber.CaptureAsync(settings.ShowScreenshotAuxiliaryLines);
        if (bitmap == null)
            return default;
        return bitmap;
    }
}
