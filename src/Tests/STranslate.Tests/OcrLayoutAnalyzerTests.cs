using STranslate.Core;
using STranslate.Plugin;
using System.Text.Json;

namespace STranslate.Tests;

public class OcrLayoutAnalyzerTests
{
    [Fact]
    public void SmartMergesParagraphLines()
    {
        var result = AnalyzeSmart(
            Box("This is the first line", 0, 0, 180, 20),
            Box("continued on the next line", 0, 24, 210, 20));

        Assert.Single(result);
        Assert.Equal("This is the first line continued on the next line", result[0].Text);
    }

    [Fact]
    public void SmartKeepsColumnsSeparate()
    {
        var result = AnalyzeSmart(
            Box("Left column starts here", 0, 0, 180, 20),
            Box("Right column starts here", 300, 0, 190, 20),
            Box("and continues below", 0, 24, 160, 20),
            Box("with its own text", 300, 24, 150, 20));

        Assert.Equal(2, result.Count);
        Assert.Equal("Left column starts here and continues below", result[0].Text);
        Assert.Equal("Right column starts here with its own text", result[1].Text);
    }

    [Fact]
    public void SmartDoesNotMergeUiLabelsOnSameRow()
    {
        var result = AnalyzeSmart(
            Box("File", 0, 0, 32, 20),
            Box("Edit", 66, 0, 32, 20),
            Box("View", 132, 0, 36, 20));

        Assert.Equal(["File", "Edit", "View"], result.Select(x => x.Text));
    }

    [Fact]
    public void SmartKeepsListItemsAndMergesContinuation()
    {
        var result = AnalyzeSmart(
            Box("- First item", 0, 0, 90, 20),
            Box("continued detail", 24, 24, 130, 20),
            Box("- Second item", 0, 48, 105, 20));

        Assert.Equal(2, result.Count);
        Assert.Equal("- First item continued detail", result[0].Text);
        Assert.Equal("- Second item", result[1].Text);
    }

    [Fact]
    public void SmartAddsSpacesForLatinWordLevelOcr()
    {
        var result = AnalyzeSmart(
            Box("Hello", 0, 0, 42, 20),
            Box("world", 50, 0, 45, 20));

        Assert.Single(result);
        Assert.Equal("Hello world", result[0].Text);
    }

    [Fact]
    public void SmartAvoidsSpacesForCjkWordLevelOcr()
    {
        var result = AnalyzeSmart(
            Box("你", 0, 0, 20, 20),
            Box("好", 22, 0, 20, 20));

        Assert.Single(result);
        Assert.Equal("你好", result[0].Text);
    }

    [Fact]
    public void ApplyLeavesContentsWithoutBoxPointsUnchanged()
    {
        var ocrResult = new OcrResult
        {
            OcrContents =
            [
                new() { Text = "plain text" },
                new() { Text = "second line" }
            ]
        };

        OcrLayoutAnalyzer.Apply(ocrResult, LayoutAnalysisMode.Smart);

        Assert.Equal(["plain text", "second line"], ocrResult.OcrContents.Select(x => x.Text));
    }

    [Fact]
    public void NoMergePreservesOriginalBlocks()
    {
        var result = OcrLayoutAnalyzer.Analyze(
            [
                Box("One", 0, 0, 40, 20),
                Box("Two", 0, 24, 40, 20)
            ],
            LayoutAnalysisMode.NoMerge);

        Assert.Equal(["One", "Two"], result.Select(x => x.Text));
    }

    [Fact]
    public void SettingsReadsLegacyLayoutAnalysisModeAsSmart()
    {
        var settings = JsonSerializer.Deserialize<Settings>(
            """{"LayoutAnalysisMode":"standardDocument"}""")!;

        Assert.Equal(LayoutAnalysisMode.Smart, settings.LayoutAnalysisMode);
    }

    [Fact]
    public void SettingsKeepsNoMergeLayoutAnalysisMode()
    {
        var settings = new Settings { LayoutAnalysisMode = LayoutAnalysisMode.NoMerge };

        settings.NormalizeLayoutAnalysisMode();

        Assert.Equal(LayoutAnalysisMode.NoMerge, settings.LayoutAnalysisMode);
    }

    private static List<OcrContent> AnalyzeSmart(params OcrContent[] contents) =>
        OcrLayoutAnalyzer.Analyze(contents, LayoutAnalysisMode.Smart);

    private static OcrContent Box(string text, float left, float top, float width, float height) =>
        new()
        {
            Text = text,
            BoxPoints =
            [
                new(left, top),
                new(left + width, top),
                new(left + width, top + height),
                new(left, top + height)
            ]
        };
}
