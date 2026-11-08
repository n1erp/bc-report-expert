namespace ReportExpert.Reporting.Design.Tests;

public sealed class SnapperTests
{
    private const double Threshold = 0.06;

    [Fact]
    public void A_left_edge_near_another_left_edge_snaps_with_a_vertical_guide()
    {
        var other = new SnapRect(1, 0.5, 2, 0.25);
        var result = ReportSnapper.Snap(new SnapRect(1.04, 1.5, 0.8, 0.25), [other], Threshold);

        Assert.True(result.SnappedX);
        Assert.Equal(1, result.Left, 6);
        var guide = Assert.Single(result.Guides, guide => guide.Vertical);
        Assert.Equal(1, guide.Position, 6);
        Assert.Equal(0.5, guide.From, 6);
        Assert.Equal(1.75, guide.To, 6);
        Assert.Equal(SnapKind.Align, guide.Kind);
    }

    [Fact]
    public void Centres_and_bottoms_align_too()
    {
        var other = new SnapRect(0, 0, 2, 1);
        var result = ReportSnapper.Snap(new SnapRect(0.53, 0.03, 1, 1), [other], Threshold);

        Assert.Equal(0.5, result.Left, 6);   // centre on centre (1.0)
        Assert.Equal(0, result.Top, 6);      // top on top
    }

    [Fact]
    public void Nothing_near_means_no_snap()
    {
        var result = ReportSnapper.Snap(new SnapRect(3, 3, 1, 1), [new SnapRect(0.2, 0.2, 1, 1)], Threshold, bandWidth: 7.5);

        Assert.False(result.SnappedX);
        Assert.False(result.SnappedY);
        Assert.Equal(3, result.Left);
        Assert.Empty(result.Guides);
    }

    [Fact]
    public void The_band_edges_are_snap_targets()
    {
        var result = ReportSnapper.Snap(new SnapRect(6.47, 2, 1, 0.5), [], Threshold, bandWidth: 7.5);

        Assert.Equal(6.5, result.Left, 6);
        Assert.Equal(SnapKind.Band, Assert.Single(result.Guides).Kind);
    }

    [Fact]
    public void Gaps_snap_to_the_spacing_already_used_between_items()
    {
        // Two boxes 0.5in apart; a third placed after the second snaps to the same 0.5in gap.
        var first = new SnapRect(0, 0, 1, 0.5);
        var second = new SnapRect(1.5, 0, 1, 0.5);
        var result = ReportSnapper.Snap(new SnapRect(3.04, 0.2, 1, 0.5), [first, second], Threshold);

        Assert.True(result.SnappedX);
        Assert.Equal(3, result.Left, 6);
        var guide = Assert.Single(result.Guides, guide => guide.Kind == SnapKind.Spacing);
        Assert.False(guide.Vertical);
        Assert.Equal(2.5, guide.From, 6);
        Assert.Equal(3, guide.To, 6);
    }
}
