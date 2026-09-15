using System;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using Xunit;

namespace AVASvgMaker.Tests;

/// <summary>
/// Which faces a connector leaves and arrives by, once the shapes have been moved somewhere
/// the faces it was drawn with no longer suit. The route itself is the router's business;
/// these are about the two points it is asked to join.
/// </summary>
public class PortChoiceTests
{
    private static double Length(ConnectorShape connector)
    {
        var path = connector.Path;
        var total = 0.0;

        for (var i = 0; i + 1 < path.Count; i++)
            total += Math.Abs(path[i + 1].X - path[i].X) + Math.Abs(path[i + 1].Y - path[i].Y);

        return total;
    }

    private static int Bends(ConnectorShape connector) => Math.Max(0, connector.Path.Count - 2);

    /// <summary>
    /// Drawn side by side and left-to-right, then stacked one above the other. Keeping the
    /// sides it was drawn with sends it out of one shape's left and into the other's right,
    /// around the outside of both, for half as much line again as going straight down.
    /// </summary>
    [AvaloniaFact]
    public void StackedShapesJoinByTheFacingSides()
    {
        var document = Harness.Page(900, 700);
        var a = Harness.Box(document, new Rect(60, 100, 110, 40), "A");
        var b = Harness.Box(document, new Rect(600, 100, 110, 40), "B");

        var connector = Harness.Join(document, a, 1, b, 3);
        document.RouteConnectors();

        Assert.Equal(0, Bends(connector));

        b.Bounds = new Rect(60, 400, 110, 40);
        document.RouteConnectors();

        // A's bottom to B's top: straight down the gap, with nothing to bend around.
        Assert.Equal(new Point(115, 140), connector.Path[0]);
        Assert.Equal(new Point(115, 400), connector.Path[^1]);
        Assert.Equal(0, Bends(connector));
        Assert.Equal(260, Length(connector), 3);
    }

    /// <summary>
    /// The same move with something in the way. The faces still have to be the facing ones,
    /// and the route still has to keep out of the wall.
    /// </summary>
    [AvaloniaFact]
    public void AMovedShapeStillRoutesRoundWhatIsInTheWay()
    {
        var document = Harness.Page(900, 700);
        var a = Harness.Box(document, new Rect(60, 300, 110, 40), "A");
        var b = Harness.Box(document, new Rect(600, 300, 110, 40), "B");
        var wall = Harness.Box(document, new Rect(350, 100, 70, 400), "wall");

        var connector = Harness.Join(document, a, 1, b, 3);
        document.RouteConnectors();

        b.Bounds = new Rect(60, 80, 110, 40);
        document.RouteConnectors();

        Assert.Equal(new Point(115, 300), connector.Path[0]);
        Assert.Equal(new Point(115, 120), connector.Path[^1]);
        Assert.Equal(180, Length(connector), 3);

        foreach (var (p, q) in Harness.Segments(connector))
        {
            Assert.False(
                Math.Min(p.X, q.X) < wall.Bounds.Right - 0.01 && Math.Max(p.X, q.X) > wall.Bounds.Left + 0.01 &&
                Math.Min(p.Y, q.Y) < wall.Bounds.Bottom - 0.01 && Math.Max(p.Y, q.Y) > wall.Bounds.Top + 0.01,
                "the route cuts through the wall");
        }
    }

    /// <summary>
    /// The case the flip was written for: pinned bottom to top, then the shapes swap places.
    /// The answer is the same as it always was - the opposite face at each end - and this is
    /// here to say so, because it now arrives by weighing four faces rather than by flipping.
    /// </summary>
    [AvaloniaFact]
    public void SwappedShapesTakeTheOppositeFaces()
    {
        var document = Harness.Page(900, 700);
        var a = Harness.Box(document, new Rect(60, 100, 110, 40), "A");
        var b = Harness.Box(document, new Rect(60, 400, 110, 40), "B");

        var connector = Harness.Join(document, a, 2, b, 0);
        document.RouteConnectors();

        Assert.Equal(new Point(115, 140), connector.Path[0]);
        Assert.Equal(new Point(115, 400), connector.Path[^1]);

        // Swap them over.
        a.Bounds = new Rect(60, 400, 110, 40);
        b.Bounds = new Rect(60, 100, 110, 40);
        document.RouteConnectors();

        Assert.Equal(new Point(115, 400), connector.Path[0]);
        Assert.Equal(new Point(115, 140), connector.Path[^1]);
        Assert.Equal(0, Bends(connector));
    }

    /// <summary>
    /// A pair that still faces each other is left alone, however roundabout it looks. The
    /// chosen points are the user's choice, and only a pair that cannot serve is overridden.
    /// </summary>
    [AvaloniaFact]
    public void APairThatStillFacesIsLeftAlone()
    {
        var document = Harness.Page(900, 700);
        var a = Harness.Box(document, new Rect(60, 300, 110, 40), "A");
        var b = Harness.Box(document, new Rect(600, 300, 110, 40), "B");

        // Out of the top of one and into the top of the other: an arch, and a deliberate one.
        var connector = Harness.Join(document, a, 0, b, 0);
        document.RouteConnectors();

        Assert.Equal(new Point(115, 300), connector.Path[0]);
        Assert.Equal(new Point(655, 300), connector.Path[^1]);
    }

    /// <summary>Putting the shapes back puts the route back: nothing is written back.</summary>
    [AvaloniaFact]
    public void MovingAShapeBackRestoresTheChosenPoints()
    {
        var document = Harness.Page(900, 700);
        var a = Harness.Box(document, new Rect(60, 100, 110, 40), "A");
        var b = Harness.Box(document, new Rect(600, 100, 110, 40), "B");

        var connector = Harness.Join(document, a, 1, b, 3);
        document.RouteConnectors();
        var original = Harness.Path(connector);

        b.Bounds = new Rect(60, 400, 110, 40);
        document.RouteConnectors();
        Assert.NotEqual(original, Harness.Path(connector));

        b.Bounds = new Rect(600, 100, 110, 40);
        document.RouteConnectors();

        Assert.Equal(original, Harness.Path(connector));
        Assert.Equal(1, connector.StartPort);
        Assert.Equal(3, connector.EndPort);
    }

    /// <summary>
    /// A hand-placed route is never given a new face. The bends were placed against whichever
    /// points were in use when they were placed, so moving an end to a face round the side of
    /// the shape would strand them - the flip to the opposite point is the most it may do.
    /// </summary>
    [AvaloniaFact]
    public void AHandPlacedRouteIsNotGivenANewFace()
    {
        var document = Harness.Page(900, 700);
        var a = Harness.Box(document, new Rect(60, 100, 110, 40), "A");
        var b = Harness.Box(document, new Rect(60, 400, 110, 40), "B");

        var connector = Harness.Join(document, a, 1, b, 3);

        // Out of A's left, down the outside, under B and back up to its right: laborious, and
        // clear of both, so there is no reason to touch it.
        connector.Waypoints =
        [
            new Point(20, 120), new Point(20, 470), new Point(220, 470), new Point(220, 420)
        ];

        document.RouteConnectors();

        Assert.True(connector.HasManualRoute, "the bends clear both shapes and should have stayed");

        // A's west point, the opposite of the east one it was drawn with - not the south point
        // a connector routing itself would have taken.
        Assert.Equal(new Point(60, 120), connector.Path[0]);
        Assert.Equal(new Point(170, 420), connector.Path[^1]);
        Assert.Contains(new Point(20, 470), connector.Path);
    }
}
