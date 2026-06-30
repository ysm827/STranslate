using STranslate.Controls;
using STranslate.Core;
using STranslate.Helpers;
using STranslate.Plugin;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace STranslate.Tests;

public class ImageTranslateOverlayTests
{
    [Fact]
    public void CreateTranslatedOverlayWithoutLocationsReturnsEmptyDocument()
    {
        var block = new OcrLayoutBlock { Text = "translated text" };

        var document = ImageTranslateRenderer.CreateTranslatedOverlay(
            [block],
            ImageTranslateOverlayTheme.Light);

        Assert.True(document.IsEmpty);
        Assert.Empty(document.Items);
        Assert.Empty(document.SelectableWords);
    }

    [Fact]
    public void CreateTranslatedOverlayKeepsSelectableWordsInSourceCoordinates()
    {
        var block = CreateBlock("ABC", left: 100, top: 50, width: 180, height: 40);

        var document = ImageTranslateRenderer.CreateTranslatedOverlay(
            [block],
            ImageTranslateOverlayTheme.Light);

        var item = Assert.Single(document.Items);
        Assert.False(document.IsEmpty);
        Assert.Equal("ABC", string.Concat(document.SelectableWords.Select(word => word.Text)));
        Assert.All(document.SelectableWords.Where(word => !word.BoundingBox.IsEmpty), word =>
        {
            Assert.True(word.BoundingBox.Left >= item.Plan.TextClipRect.Left);
            Assert.True(word.BoundingBox.Top >= item.Plan.TextClipRect.Top);
            Assert.True(word.BoundingBox.Right <= item.Plan.TextClipRect.Right);
            Assert.True(word.BoundingBox.Bottom <= item.Plan.TextClipRect.Bottom);
        });
    }

    [Fact]
    public void CreateTranslatedOverlayPreservesThemeColors()
    {
        var block = CreateBlock("译文", left: 10, top: 20, width: 160, height: 40);

        var light = ImageTranslateRenderer.CreateTranslatedOverlay(
            [block],
            ImageTranslateOverlayTheme.Light);
        var dark = ImageTranslateRenderer.CreateTranslatedOverlay(
            [block],
            ImageTranslateOverlayTheme.Dark);

        var lightItem = Assert.Single(light.Items);
        var darkItem = Assert.Single(dark.Items);
        Assert.Equal(Colors.Black, lightItem.Plan.ForegroundColor);
        Assert.Equal(Colors.White, darkItem.Plan.ForegroundColor);
        Assert.NotEqual(lightItem.Plan.OverlayBackgroundColor, darkItem.Plan.OverlayBackgroundColor);
        Assert.True(lightItem.BackgroundBrush.IsFrozen);
        Assert.True(darkItem.BackgroundBrush.IsFrozen);
    }

    [Fact]
    public void OverlayRendersDocumentAndBecomesTransparentWhenCleared()
    {
        RunOnStaThread(() =>
        {
            var document = ImageTranslateRenderer.CreateTranslatedOverlay(
                [CreateBlock("ABC", left: 20, top: 20, width: 160, height: 40)],
                ImageTranslateOverlayTheme.Light);
            var overlay = new ImageTranslateOverlay
            {
                Width = 220,
                Height = 100,
                Document = document
            };

            Assert.True(RenderHasVisiblePixels(overlay, 220, 100));

            overlay.Document = null;
            Assert.False(RenderHasVisiblePixels(overlay, 220, 100));
        });
    }

    [Fact]
    public void OverlayRendersLargeChineseTitleText()
    {
        RunOnStaThread(() =>
        {
            var document = ImageTranslateRenderer.CreateTranslatedOverlay(
                [
                    CreateBlock(
                        "我们穿越荆棘和玫瑰的旅程",
                        left: 149.648468,
                        top: 85.732590,
                        width: 1568.743286,
                        height: 78.889755)
                ],
                ImageTranslateOverlayTheme.Light);
            var overlay = new ImageTranslateOverlay
            {
                Width = 1920,
                Height = 240,
                Document = document
            };

            Assert.True(RenderHasDarkPixels(overlay, 1920, 240));
        });
    }

    private static bool RenderHasVisiblePixels(FrameworkElement element, int width, int height)
    {
        var pixels = RenderPixels(element, width, height);
        return pixels.Where((_, index) => index % 4 == 3).Any(alpha => alpha != 0);
    }

    private static bool RenderHasDarkPixels(FrameworkElement element, int width, int height)
    {
        var pixels = RenderPixels(element, width, height);
        for (var index = 0; index < pixels.Length; index += 4)
        {
            var blue = pixels[index];
            var green = pixels[index + 1];
            var red = pixels[index + 2];
            var alpha = pixels[index + 3];

            if (alpha > 120 && red < 80 && green < 80 && blue < 80)
                return true;
        }

        return false;
    }

    private static byte[] RenderPixels(FrameworkElement element, int width, int height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static OcrLayoutBlock CreateBlock(
        string text,
        double left,
        double top,
        double width,
        double height)
    {
        var box = Box(left, top, width, height);
        return new OcrLayoutBlock
        {
            Text = text,
            BoxPoints = box,
            LineBoxPoints = [box]
        };
    }

    private static List<BoxPoint> Box(double left, double top, double width, double height) =>
    [
        new((float)left, (float)top),
        new((float)(left + width), (float)top),
        new((float)(left + width), (float)(top + height)),
        new((float)left, (float)(top + height))
    ];

    private static void RunOnStaThread(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception != null)
            ExceptionDispatchInfo.Capture(exception).Throw();
    }
}
