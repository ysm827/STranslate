using STranslate.Core;
using STranslate.Plugin;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace STranslate.Tests;

public class OcrWordBuilderTests
{
    [Fact]
    public void CreateFromOcrContentsSplitsCharactersAndBuildsSequentialIndexes()
    {
        var contents = new[]
        {
            new OcrContent
            {
                Text = "BA",
                BoxPoints = Box(20, 0, 20, 10)
            },
            new OcrContent
            {
                Text = "C",
                BoxPoints = Box(0, 0, 10, 10)
            }
        };

        var words = OcrWordBuilder.CreateFromOcrContents(contents);

        Assert.Equal(["C", " ", "B", "A"], words.Select(word => word.Text));
        Assert.Equal([0, 1, 2, 3], words.Select(word => word.StartIndexInFullText));
        Assert.Equal(new Rect(20, 0, 10, 10), words[2].BoundingBox);
        Assert.Equal(new Rect(30, 0, 10, 10), words[3].BoundingBox);
    }

    [Fact]
    public void CreateFromOcrContentsSkipsEmptyTextAndMissingBoxes()
    {
        var contents = new[]
        {
            new OcrContent
            {
                Text = "",
                BoxPoints = Box(0, 0, 10, 10)
            },
            new OcrContent
            {
                Text = "A",
                BoxPoints = []
            },
            new OcrContent
            {
                Text = "B",
                BoxPoints = null!
            }
        };

        var words = OcrWordBuilder.CreateFromOcrContents(contents);

        Assert.Empty(words);
    }

    [Fact]
    public void CreateFromOcrContentsInsertsNewLineBetweenRows()
    {
        var contents = new[]
        {
            new OcrContent
            {
                Text = "Top",
                BoxPoints = Box(0, 0, 30, 10)
            },
            new OcrContent
            {
                Text = "Bottom",
                BoxPoints = Box(0, 30, 60, 10)
            }
        };

        var words = OcrWordBuilder.CreateFromOcrContents(contents);

        Assert.Equal($"Top{Environment.NewLine}Bottom", string.Concat(words.Select(word => word.Text)));
    }

    [Fact]
    public void CreateFromOcrContentsDoesNotDuplicateExistingWhitespace()
    {
        var contents = new[]
        {
            new OcrContent
            {
                Text = "Hello ",
                BoxPoints = Box(0, 0, 60, 10)
            },
            new OcrContent
            {
                Text = "World",
                BoxPoints = Box(80, 0, 50, 10)
            }
        };

        var words = OcrWordBuilder.CreateFromOcrContents(contents);

        Assert.Equal("Hello World", string.Concat(words.Select(word => word.Text)));
    }

    [Fact]
    public void CreateFromFormattedTextUsesHighlightGeometryAndScaleFactor()
    {
        const double scaleFactor = 2;
        var text = "ABC";
        var formattedText = CreateFormattedText(text, maxWidth: 200);
        var origin = new Point(10, 5);
        var clipRect = new Rect(10, 5, 200, 40);

        var words = OcrWordBuilder.CreateIndexedCollection(
            OcrWordBuilder.CreateFromFormattedText(text, formattedText, origin, clipRect, scaleFactor));

        Assert.Equal(["A", "B", "C"], words.Select(word => word.Text));
        Assert.Equal([0, 1, 2], words.Select(word => word.StartIndexInFullText));
        Assert.All(words, word =>
        {
            Assert.True(word.BoundingBox.Left >= clipRect.Left * scaleFactor);
            Assert.True(word.BoundingBox.Top >= clipRect.Top * scaleFactor);
            Assert.True(word.BoundingBox.Right <= clipRect.Right * scaleFactor);
            Assert.True(word.BoundingBox.Bottom <= clipRect.Bottom * scaleFactor);
        });
    }

    [Fact]
    public void CreateFromFormattedTextClipsCharacterBoxesToTextClipRect()
    {
        var text = "ABC";
        var formattedText = CreateFormattedText(text, maxWidth: 200);
        var origin = new Point(10, 5);
        var clipRect = new Rect(10, 5, 12, 40);

        var words = OcrWordBuilder.CreateFromFormattedText(text, formattedText, origin, clipRect, scaleFactor: 1);

        Assert.NotEmpty(words);
        Assert.All(words, word =>
        {
            Assert.True(word.BoundingBox.Left >= clipRect.Left);
            Assert.True(word.BoundingBox.Top >= clipRect.Top);
            Assert.True(word.BoundingBox.Right <= clipRect.Right);
            Assert.True(word.BoundingBox.Bottom <= clipRect.Bottom);
        });
    }

    [Fact]
    public void CreateFromFormattedTextPreservesWhitespaceInIndexedText()
    {
        var text = "A B";
        var formattedText = CreateFormattedText(text, maxWidth: 200);
        var origin = new Point(10, 5);
        var clipRect = new Rect(10, 5, 200, 40);

        var words = OcrWordBuilder.CreateIndexedCollection(
            OcrWordBuilder.CreateFromFormattedText(text, formattedText, origin, clipRect, scaleFactor: 1),
            preserveOrder: true);

        Assert.Equal(text, string.Concat(words.Select(word => word.Text)));
        Assert.Equal([0, 1, 2], words.Select(word => word.StartIndexInFullText));
    }

    private static FormattedText CreateFormattedText(string text, double maxWidth)
    {
        var formattedText = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Arial"),
            20,
            Brushes.Black,
            1);

        formattedText.MaxTextWidth = maxWidth;
        formattedText.MaxTextHeight = 40;
        return formattedText;
    }

    private static List<BoxPoint> Box(double left, double top, double width, double height) =>
    [
        new((float)left, (float)top),
        new((float)(left + width), (float)top),
        new((float)(left + width), (float)(top + height)),
        new((float)left, (float)(top + height))
    ];
}
