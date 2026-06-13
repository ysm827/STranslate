using STranslate.Plugin;

namespace STranslate.Core;

internal static class OcrLayoutAnalyzer
{
    internal static void Apply(OcrResult ocrResult, LayoutAnalysisMode mode)
    {
        if (ocrResult.OcrContents.Count == 0 || !Utilities.HasBoxPoints(ocrResult))
            return;

        var contents = Analyze(ocrResult.OcrContents, mode);
        ocrResult.OcrContents.Clear();
        ocrResult.OcrContents.AddRange(contents);
    }

    internal static List<OcrContent> Analyze(IReadOnlyList<OcrContent> contents, LayoutAnalysisMode mode)
    {
        if (mode == LayoutAnalysisMode.NoMerge)
            return CloneContents(contents);

        var items = CreateLayoutItems(contents);
        if (items.Count == 0)
            return CloneContents(contents);

        return AnalyzeSmart(items);
    }

    private static List<OcrContent> AnalyzeSmart(List<LayoutItem> items)
    {
        var lineSegments = BuildLineSegments(items);
        if (lineSegments.Count == 0)
            return [];

        var metrics = LayoutMetrics.From(lineSegments);
        var paragraphs = new List<ParagraphGroup>();

        foreach (var line in lineSegments.OrderBy(x => x.Bounds.Top).ThenBy(x => x.Bounds.Left))
        {
            var target = FindBestParagraph(line, paragraphs, metrics);
            if (target == null)
            {
                paragraphs.Add(new ParagraphGroup(line));
            }
            else
            {
                target.Add(line);
            }
        }

        return paragraphs
            .OrderBy(x => x.Bounds.Top)
            .ThenBy(x => x.Bounds.Left)
            .Select(x => x.ToOcrContent())
            .ToList();
    }

    private static ParagraphGroup? FindBestParagraph(
        LineSegment line,
        List<ParagraphGroup> paragraphs,
        LayoutMetrics metrics)
    {
        ParagraphGroup? bestParagraph = null;
        var bestScore = double.NegativeInfinity;

        foreach (var paragraph in paragraphs)
        {
            var lastLine = paragraph.LastLine;
            if (line.Bounds.Top < lastLine.Bounds.Top)
                continue;

            if (!CanAppendToParagraph(lastLine, line, metrics))
                continue;

            var horizontalScore = HorizontalOverlapRatio(lastLine.Bounds, line.Bounds) * 3;
            var leftScore = 1 - Math.Min(1, Math.Abs(lastLine.Bounds.Left - line.Bounds.Left) / Math.Max(metrics.LineHeight, 1));
            var gapScore = 1 - Math.Min(1, VerticalGap(lastLine.Bounds, line.Bounds) / Math.Max(metrics.LineHeight, 1));
            var score = horizontalScore + leftScore + gapScore;

            if (score > bestScore)
            {
                bestScore = score;
                bestParagraph = paragraph;
            }
        }

        return bestParagraph;
    }

    private static bool CanAppendToParagraph(LineSegment previous, LineSegment current, LayoutMetrics metrics)
    {
        var verticalGap = VerticalGap(previous.Bounds, current.Bounds);
        if (verticalGap > metrics.LineHeight * 1.25)
            return false;

        if (IsListStart(current.Text))
            return false;

        if (IsListStart(previous.Text) && current.Bounds.Left > previous.Bounds.Left + metrics.LineHeight * 0.8)
            return true;

        if (Math.Max(previous.Bounds.Height, current.Bounds.Height) >
            Math.Min(previous.Bounds.Height, current.Bounds.Height) * 1.45)
        {
            return false;
        }

        var horizontalOverlap = HorizontalOverlapRatio(previous.Bounds, current.Bounds);
        var leftDelta = Math.Abs(previous.Bounds.Left - current.Bounds.Left);
        var hasColumnAffinity = horizontalOverlap >= 0.45 || leftDelta <= metrics.LineHeight * 1.2;
        if (!hasColumnAffinity)
            return false;

        if (LooksLikeStandaloneControl(previous) || LooksLikeStandaloneControl(current))
            return false;

        var indentDelta = current.Bounds.Left - previous.Bounds.Left;
        if (Math.Abs(indentDelta) > metrics.LineHeight * 2.5 && horizontalOverlap < 0.7)
            return false;

        return true;
    }

    private static List<LineSegment> BuildLineSegments(List<LayoutItem> items)
    {
        var visualLines = new List<VisualLine>();

        foreach (var item in items.OrderBy(x => x.Bounds.CenterY).ThenBy(x => x.Bounds.Left))
        {
            var line = visualLines
                .Where(x => IsSameVisualLine(x.Bounds, item.Bounds))
                .OrderByDescending(x => VerticalOverlapRatio(x.Bounds, item.Bounds))
                .ThenBy(x => Math.Abs(x.Bounds.CenterY - item.Bounds.CenterY))
                .FirstOrDefault();

            if (line == null)
                visualLines.Add(new VisualLine(item));
            else
                line.Add(item);
        }

        return visualLines
            .SelectMany(SplitVisualLine)
            .OrderBy(x => x.Bounds.Top)
            .ThenBy(x => x.Bounds.Left)
            .ToList();
    }

    private static IEnumerable<LineSegment> SplitVisualLine(VisualLine line)
    {
        var sortedItems = line.Items.OrderBy(x => x.Bounds.Left).ToList();
        var group = new List<LayoutItem>();
        var lineHeight = Median(sortedItems.Select(x => x.Bounds.Height));

        foreach (var item in sortedItems)
        {
            if (group.Count > 0)
            {
                var previous = group[^1];
                var gap = item.Bounds.Left - previous.Bounds.Right;
                var maxInlineGap = Math.Max(
                    lineHeight * 1.25,
                    Math.Min(lineHeight * 2.0, Math.Min(previous.Bounds.Width, item.Bounds.Width) * 0.75));

                if (gap > maxInlineGap)
                {
                    yield return LineSegment.From(group);
                    group = [];
                }
            }

            group.Add(item);
        }

        if (group.Count > 0)
            yield return LineSegment.From(group);
    }

    private static bool IsSameVisualLine(Bounds lineBounds, Bounds itemBounds)
    {
        if (VerticalOverlapRatio(lineBounds, itemBounds) >= 0.55)
            return true;

        var centerDelta = Math.Abs(lineBounds.CenterY - itemBounds.CenterY);
        return centerDelta <= Math.Min(lineBounds.Height, itemBounds.Height) * 0.45;
    }

    private static List<LayoutItem> CreateLayoutItems(IReadOnlyList<OcrContent> contents) =>
        contents
            .Select((content, index) => LayoutItem.TryCreate(content, index))
            .Where(x => x != null)
            .Select(x => x!)
            .ToList();

    private static OcrContent CreateMergedContent(string text, IReadOnlyList<LayoutItem> items)
    {
        var bounds = Bounds.Union(items.Select(x => x.Bounds));
        return new OcrContent
        {
            Text = text.Trim(),
            BoxPoints =
            [
                new((float)bounds.Left, (float)bounds.Top),
                new((float)bounds.Right, (float)bounds.Top),
                new((float)bounds.Right, (float)bounds.Bottom),
                new((float)bounds.Left, (float)bounds.Bottom)
            ]
        };
    }

    private static List<OcrContent> CloneContents(IReadOnlyList<OcrContent> contents) =>
        contents.Select(CloneContent).ToList();

    private static OcrContent CloneContent(OcrContent content) =>
        new()
        {
            Text = content.Text,
            BoxPoints = content.BoxPoints.Select(point => new BoxPoint(point.X, point.Y)).ToList()
        };

    private static string JoinLineText(IReadOnlyList<LayoutItem> items)
    {
        var text = items[0].Text;
        for (var i = 1; i < items.Count; i++)
        {
            if (NeedsSpace(text, items[i].Text))
                text += " ";

            text += items[i].Text;
        }

        return text;
    }

    private static string JoinParagraphText(IReadOnlyList<LineSegment> lines)
    {
        var text = lines[0].Text;
        for (var i = 1; i < lines.Count; i++)
        {
            if (NeedsSpace(text, lines[i].Text))
                text += " ";

            text += lines[i].Text;
        }

        return text;
    }

    private static bool NeedsSpace(string previous, string current)
    {
        if (string.IsNullOrWhiteSpace(previous) || string.IsNullOrWhiteSpace(current))
            return false;

        var left = previous[^1];
        var right = current[0];
        if (char.IsWhiteSpace(left) || char.IsWhiteSpace(right))
            return false;

        if (IsCjk(left) || IsCjk(right))
            return false;

        if (char.IsPunctuation(left) || char.IsPunctuation(right))
            return false;

        return true;
    }

    private static bool LooksLikeStandaloneControl(LineSegment line)
    {
        if (IsListStart(line.Text))
            return false;

        var compactText = line.Text.Replace(" ", string.Empty);
        if (compactText.Length > 14)
            return false;

        if (line.Text.Any(char.IsWhiteSpace))
            return false;

        var widthRatio = line.Bounds.Width / Math.Max(line.Bounds.Height, 1);
        return widthRatio <= 6.2 && !HasSentenceEnding(line.Text);
    }

    private static bool IsListStart(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.Length == 0)
            return false;

        if (trimmed[0] is '-' or '*' or '+' or '•' or '·' or '●' or '▪')
            return trimmed.Length == 1 || char.IsWhiteSpace(trimmed[1]);

        var i = 0;
        while (i < trimmed.Length && char.IsDigit(trimmed[i]))
            i++;

        return i > 0 &&
               i < trimmed.Length - 1 &&
               trimmed[i] is '.' or ')' or '、' &&
               char.IsWhiteSpace(trimmed[i + 1]);
    }

    private static bool HasSentenceEnding(string text) =>
        text.IndexOfAny(['.', '!', '?', ';', ':', '。', '！', '？', '；', '：']) >= 0;

    private static bool IsCjk(char ch) =>
        (ch >= '\u3400' && ch <= '\u9fff') ||
        (ch >= '\uf900' && ch <= '\ufaff') ||
        (ch >= '\u3040' && ch <= '\u30ff') ||
        (ch >= '\uac00' && ch <= '\ud7af');

    private static double VerticalGap(Bounds top, Bounds bottom) =>
        Math.Max(0, bottom.Top - top.Bottom);

    private static double HorizontalOverlapRatio(Bounds first, Bounds second)
    {
        var overlap = Math.Max(0, Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left));
        return overlap / Math.Max(1, Math.Min(first.Width, second.Width));
    }

    private static double VerticalOverlapRatio(Bounds first, Bounds second)
    {
        var overlap = Math.Max(0, Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Top, second.Top));
        return overlap / Math.Max(1, Math.Min(first.Height, second.Height));
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.Where(x => x > 0).Order().ToList();
        if (ordered.Count == 0)
            return 0;

        var middle = ordered.Count / 2;
        return ordered.Count % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2
            : ordered[middle];
    }

    private sealed class VisualLine
    {
        internal VisualLine(LayoutItem item)
        {
            Items.Add(item);
            Bounds = item.Bounds;
        }

        internal List<LayoutItem> Items { get; } = [];

        internal Bounds Bounds { get; private set; }

        internal void Add(LayoutItem item)
        {
            Items.Add(item);
            Bounds = Bounds.Union(Bounds, item.Bounds);
        }
    }

    private sealed class ParagraphGroup
    {
        internal ParagraphGroup(LineSegment line) => Lines.Add(line);

        internal List<LineSegment> Lines { get; } = [];

        internal LineSegment LastLine => Lines[^1];

        internal Bounds Bounds => Bounds.Union(Lines.Select(x => x.Bounds));

        internal void Add(LineSegment line) => Lines.Add(line);

        internal OcrContent ToOcrContent()
        {
            var items = Lines.SelectMany(x => x.Items).OrderBy(x => x.Index).ToList();
            return CreateMergedContent(JoinParagraphText(Lines), items);
        }
    }

    private sealed class LineSegment
    {
        private LineSegment(List<LayoutItem> items)
        {
            Items = items;
            Bounds = Bounds.Union(items.Select(x => x.Bounds));
            Text = JoinLineText(items);
        }

        internal List<LayoutItem> Items { get; }

        internal Bounds Bounds { get; }

        internal string Text { get; }

        internal static LineSegment From(List<LayoutItem> items) => new([.. items.OrderBy(x => x.Bounds.Left)]);
    }

    private sealed class LayoutItem
    {
        private LayoutItem(OcrContent content, Bounds bounds, int index)
        {
            Content = content;
            Bounds = bounds;
            Index = index;
            Text = content.Text.Trim();
        }

        internal OcrContent Content { get; }

        internal Bounds Bounds { get; }

        internal int Index { get; }

        internal string Text { get; }

        internal static LayoutItem? TryCreate(OcrContent content, int index)
        {
            if (string.IsNullOrWhiteSpace(content.Text) || content.BoxPoints.Count == 0)
                return null;

            var bounds = Bounds.From(content.BoxPoints);
            return bounds.Width <= 0 || bounds.Height <= 0
                ? null
                : new LayoutItem(content, bounds, index);
        }
    }

    private readonly record struct LayoutMetrics(double LineHeight)
    {
        internal static LayoutMetrics From(IReadOnlyList<LineSegment> lines) =>
            new(Math.Max(1, Median(lines.Select(x => x.Bounds.Height))));
    }

    private readonly record struct Bounds(double Left, double Top, double Right, double Bottom)
    {
        internal double Width => Right - Left;

        internal double Height => Bottom - Top;

        internal double CenterY => (Top + Bottom) / 2;

        internal static Bounds From(IReadOnlyList<BoxPoint> points) =>
            new(
                points.Min(p => p.X),
                points.Min(p => p.Y),
                points.Max(p => p.X),
                points.Max(p => p.Y));

        internal static Bounds Union(IEnumerable<Bounds> bounds)
        {
            var list = bounds.ToList();
            return new Bounds(
                list.Min(x => x.Left),
                list.Min(x => x.Top),
                list.Max(x => x.Right),
                list.Max(x => x.Bottom));
        }

        internal static Bounds Union(Bounds first, Bounds second) =>
            new(
                Math.Min(first.Left, second.Left),
                Math.Min(first.Top, second.Top),
                Math.Max(first.Right, second.Right),
                Math.Max(first.Bottom, second.Bottom));
    }
}
