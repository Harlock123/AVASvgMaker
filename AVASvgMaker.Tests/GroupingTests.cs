using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using Xunit;

namespace AVASvgMaker.Tests;

public class GroupingTests
{
    [AvaloniaFact]
    public void ShapesGroupAndUngroup()
    {
        var document = Harness.Page();
        var a = Harness.Box(document, new Rect(100, 100, 100, 60), "A");
        var b = Harness.Box(document, new Rect(300, 200, 100, 60), "B");
        var loose = Harness.Box(document, new Rect(100, 320, 80, 40), "C");

        Assert.False(document.Group([a]));
        Assert.Equal(0, a.GroupId);

        Assert.True(document.Group([a, b]));
        Assert.NotEqual(0, a.GroupId);
        Assert.Equal(a.GroupId, b.GroupId);
        Assert.Equal(0, loose.GroupId);

        Assert.Equal(2, document.GroupOf(a).Count);
        Assert.Single(document.GroupOf(loose));
        Assert.Equal(2, document.WithGroups([b]).Count);

        Assert.False(document.Group([a, b]));

        // A group and a loose shape make one group, not a group of groups.
        var before = a.GroupId;
        Assert.True(document.Group([a, loose]));
        Assert.Equal(a.GroupId, loose.GroupId);
        Assert.Equal(b.GroupId, loose.GroupId);
        Assert.NotEqual(before, a.GroupId);

        Assert.True(document.Ungroup([b]));
        Assert.Equal(0, a.GroupId);
        Assert.Equal(0, b.GroupId);
        Assert.Equal(0, loose.GroupId);
        Assert.False(document.Ungroup([a]));
    }

    [AvaloniaFact]
    public void APastedGroupIsAGroupOfItsOwn()
    {
        var document = Harness.Page();
        var a = Harness.Box(document, new Rect(100, 100, 60, 40));
        var b = Harness.Box(document, new Rect(200, 100, 60, 40));
        document.Group([a, b]);

        var copied = ShapeClipboard.Copy(document, document.Shapes.ToList())!;
        var pasted = ShapeClipboard.Paste(document, copied, new Vector(20, 20));

        Assert.Equal(2, pasted.Count);
        Assert.NotEqual(0, pasted[0].GroupId);
        Assert.Equal(pasted[0].GroupId, pasted[1].GroupId);
        Assert.NotEqual(a.GroupId, pasted[0].GroupId);
    }

    [AvaloniaFact]
    public void StretchingASelectionKeepsEveryShapeInProportion()
    {
        var document = Harness.Page();
        var one = Harness.Box(document, new Rect(100, 100, 100, 50));
        var two = Harness.Box(document, new Rect(300, 200, 100, 50));

        var from = new Rect(100, 100, 300, 150);
        var snapshots = new[] { ShapeArranger.Snapshot(one), ShapeArranger.Snapshot(two) };

        ShapeArranger.Scale(snapshots, from, new Rect(100, 100, 600, 300));

        // The anchored corner stays where it was; everything else doubles away from it.
        Assert.Equal(100, one.Bounds.X, 2);
        Assert.Equal(100, one.Bounds.Y, 2);
        Assert.Equal(200, one.Bounds.Width, 2);
        Assert.Equal(100, one.Bounds.Height, 2);
        Assert.Equal(500, two.Bounds.X, 2);
    }

    [AvaloniaFact]
    public void AConnectorsEndsScaleButAGluedEndIsLeftToItsShape()
    {
        var document = Harness.Page();
        var p = Harness.Box(document, new Rect(100, 100, 80, 40));
        var q = Harness.Box(document, new Rect(300, 100, 80, 40));

        var free = new ConnectorShape(new Point(200, 120), new Point(280, 120))
        {
            Routing = ConnectorRouting.Straight
        };

        document.Add(free);

        var glued = Harness.Join(document, p, 1, q, 3);
        var wasGlued = glued.ResolvedStart;

        var snapshots = new[] { ShapeArranger.Snapshot(free), ShapeArranger.Snapshot(glued) };
        ShapeArranger.Scale(snapshots, new Rect(100, 100, 280, 40), new Rect(100, 100, 560, 40));

        Assert.Equal(300, free.Start.X, 2);
        Assert.Equal(460, free.End.X, 2);
        Assert.Equal(120, free.Start.Y, 2);

        // The glued one has no free end to move, so nothing of it was touched directly.
        Assert.Equal(wasGlued, glued.ResolvedStart);
    }
}
