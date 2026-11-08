namespace ReportExpert.Reporting.Design;

/// <summary>A rectangle on a band, in inches from the band's top-left corner.</summary>
public readonly record struct SnapRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;

    public double CenterX => Left + Width / 2;

    public double CenterY => Top + Height / 2;

    public SnapRect Offset(double dx, double dy) => this with { Left = Left + dx, Top = Top + dy };
}

/// <summary>How a snap line was found.</summary>
public enum SnapKind
{
    /// <summary>An edge or centre lines up with another item's edge or centre.</summary>
    Align,

    /// <summary>The gap to a neighbour equals a gap already used between two other items.</summary>
    Spacing,

    /// <summary>The item lines up with the band edge.</summary>
    Band,
}

/// <summary>
/// A guide line to draw while moving: vertical lines are at an x position and run from <see cref="From"/>
/// to <see cref="To"/> on the y axis; horizontal lines the other way round.
/// </summary>
public sealed record SnapGuide(bool Vertical, double Position, double From, double To, SnapKind Kind);

public sealed record SnapResult(double Left, double Top, bool SnappedX, bool SnappedY, IReadOnlyList<SnapGuide> Guides);

/// <summary>
/// Snap lines for moving report items: edges and centres align with the other items of the band and with
/// the band edges, and gaps snap to the spacing already used between items (equal spacing). REX's own
/// implementation of the snapping a report designer offers; a plain grid snap applies when nothing is near.
/// </summary>
public static class ReportSnapper
{
    public static SnapResult Snap(SnapRect moving, IReadOnlyList<SnapRect> others, double threshold, double? bandWidth = null, double? bandHeight = null)
    {
        ArgumentNullException.ThrowIfNull(others);
        var guides = new List<SnapGuide>();
        var dx = BestAlign(moving, others, threshold, vertical: true, bandWidth, out var alignX);
        var dy = BestAlign(moving, others, threshold, vertical: false, bandHeight, out var alignY);

        if (dx is null && BestSpacing(moving, others, threshold, horizontal: true, out var spacingX) is { } sx)
        {
            dx = sx;
            alignX = spacingX;
        }

        if (dy is null && BestSpacing(moving, others, threshold, horizontal: false, out var spacingY) is { } sy)
        {
            dy = sy;
            alignY = spacingY;
        }

        var snapped = moving.Offset(dx ?? 0, dy ?? 0);
        if (dx is not null)
        {
            guides.AddRange(alignX(snapped));
        }

        if (dy is not null)
        {
            guides.AddRange(alignY(snapped));
        }

        return new SnapResult(snapped.Left, snapped.Top, dx is not null, dy is not null, guides);
    }

    private static double? BestAlign(SnapRect moving, IReadOnlyList<SnapRect> others, double threshold, bool vertical, double? bandSize, out Func<SnapRect, IEnumerable<SnapGuide>> guides)
    {
        double[] Lines(SnapRect rect) => vertical ? [rect.Left, rect.CenterX, rect.Right] : [rect.Top, rect.CenterY, rect.Bottom];

        double? best = null;
        var targets = new List<(double Line, SnapRect? Source)>();
        foreach (var other in others)
        {
            foreach (var line in Lines(other))
            {
                targets.Add((line, other));
            }
        }

        targets.Add((0, null));
        if (bandSize is { } size and > 0)
        {
            targets.Add((size, null));
        }

        foreach (var own in Lines(moving))
        {
            foreach (var (line, _) in targets)
            {
                var delta = line - own;
                if (Math.Abs(delta) <= threshold && (best is null || Math.Abs(delta) < Math.Abs(best.Value) - 1e-9))
                {
                    best = delta;
                }
            }
        }

        if (best is null)
        {
            guides = _ => [];
            return null;
        }

        guides = snapped =>
        {
            var result = new List<SnapGuide>();
            foreach (var own in Lines(snapped))
            {
                var matches = targets.Where(target => Math.Abs(target.Line - own) < 1e-6).ToList();
                if (matches.Count == 0)
                {
                    continue;
                }

                var rects = matches.Where(match => match.Source is not null).Select(match => match.Source!.Value).Append(snapped).ToList();
                var from = vertical ? rects.Min(rect => rect.Top) : rects.Min(rect => rect.Left);
                var to = vertical ? rects.Max(rect => rect.Bottom) : rects.Max(rect => rect.Right);
                var kind = matches.All(match => match.Source is null) ? SnapKind.Band : SnapKind.Align;
                if (!result.Any(guide => Math.Abs(guide.Position - own) < 1e-6))
                {
                    result.Add(new SnapGuide(vertical, own, from, to, kind));
                }
            }

            return result;
        };
        return best;
    }

    /// <summary>Equal spacing: the gap to the nearest neighbour snaps to a gap already used between two other items.</summary>
    private static double? BestSpacing(SnapRect moving, IReadOnlyList<SnapRect> others, double threshold, bool horizontal, out Func<SnapRect, IEnumerable<SnapGuide>> guides)
    {
        bool Overlap(SnapRect a, SnapRect b) => horizontal
            ? a.Top < b.Bottom && b.Top < a.Bottom
            : a.Left < b.Right && b.Left < a.Right;

        double Start(SnapRect rect) => horizontal ? rect.Left : rect.Top;

        double End(SnapRect rect) => horizontal ? rect.Right : rect.Bottom;

        var gaps = new List<double>();
        for (var i = 0; i < others.Count; i++)
        {
            for (var j = 0; j < others.Count; j++)
            {
                if (i != j && Overlap(others[i], others[j]) && Start(others[j]) >= End(others[i]))
                {
                    gaps.Add(Start(others[j]) - End(others[i]));
                }
            }
        }

        guides = _ => [];
        if (gaps.Count == 0)
        {
            return null;
        }

        double? best = null;
        SnapRect? neighbour = null;
        var before = true;
        foreach (var other in others.Where(other => Overlap(other, moving)))
        {
            foreach (var gap in gaps)
            {
                // Neighbour before (left or above) the moving item.
                var delta = End(other) + gap - Start(moving);
                if (Start(moving) >= End(other) - threshold && Math.Abs(delta) <= threshold && (best is null || Math.Abs(delta) < Math.Abs(best.Value)))
                {
                    best = delta;
                    neighbour = other;
                    before = true;
                }

                // Neighbour after (right or below).
                delta = Start(other) - gap - End(moving);
                if (End(moving) <= Start(other) + threshold && Math.Abs(delta) <= threshold && (best is null || Math.Abs(delta) < Math.Abs(best.Value)))
                {
                    best = delta;
                    neighbour = other;
                    before = false;
                }
            }
        }

        if (best is null || neighbour is not { } near)
        {
            return null;
        }

        guides = snapped =>
        {
            var a = before ? End(near) : End(snapped);
            var b = before ? Start(snapped) : Start(near);
            var cross = horizontal ? (Math.Max(near.Top, snapped.Top) + Math.Min(near.Bottom, snapped.Bottom)) / 2 : (Math.Max(near.Left, snapped.Left) + Math.Min(near.Right, snapped.Right)) / 2;
            return [new SnapGuide(!horizontal, cross, a, b, SnapKind.Spacing)];
        };
        return best;
    }
}
