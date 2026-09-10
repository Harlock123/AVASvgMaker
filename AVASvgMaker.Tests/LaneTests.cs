using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using Xunit;

namespace AVASvgMaker.Tests;

public class LaneTests
{
    private static (DiagramDocument Document, ContainerShape Pool, ContainerShape[] Lanes) Pool(int lanes = 3)
    {
        var document = Harness.Page(700, 500);

        var pool = (ContainerShape)ShapeFactory.Create(ShapeKind.Pool, new Rect(40, 40, 620, 360));
        document.Add(pool);

        foreach (var _ in Enumerable.Range(0, lanes))
        {
            var lane = (ContainerShape)ShapeFactory.Create(ShapeKind.Lane, pool.Body);
            document.Add(lane);
            document.Adopt(lane, pool);
        }

        document.NormaliseOrder();
        document.LayoutContainers();

        return (document, pool, document.LanesOf(pool).ToArray());
    }

    [AvaloniaFact]
    public void EqualSharesDivideThePoolEqually()
    {
        var (_, pool, lanes) = Pool();
        var third = pool.Body.Height / 3;

        Assert.All(lanes, lane => Assert.Equal(1, lane.LaneShare, 6));
        Assert.All(lanes, lane => Assert.Equal(third, lane.Bounds.Height, 2));
        Assert.Equal(pool.Body.Bottom, lanes[^1].Bounds.Bottom, 6);
    }

    [AvaloniaFact]
    public void MovingABoundaryTradesBetweenTwoLanesOnly()
    {
        var (document, pool, lanes) = Pool();
        var body = pool.Body;
        var third = body.Height / 3;

        Assert.True(document.ResizeLane(lanes[0], body.Top + body.Height * 0.5));

        Assert.Equal(body.Height * 0.5, lanes[0].Bounds.Height, 2);
        Assert.Equal(2 * third - body.Height * 0.5, lanes[1].Bounds.Height, 2);
        Assert.Equal(third, lanes[2].Bounds.Height, 2);

        Assert.Equal(body.Height, lanes.Sum(lane => lane.Bounds.Height), 2);
        Assert.Equal(body.Bottom, lanes[^1].Bounds.Bottom, 6);
    }

    [AvaloniaFact]
    public void SharesAreProportionsSoAPoolResizeKeepsThem()
    {
        var (document, pool, lanes) = Pool();
        document.ResizeLane(lanes[0], pool.Body.Top + pool.Body.Height * 0.5);

        var wasTall = lanes[0].Bounds.Height / pool.Body.Height;

        pool.Bounds = new Rect(40, 40, 620, 200);
        document.LayoutContainers();

        Assert.Equal(wasTall, lanes[0].Bounds.Height / pool.Body.Height, 6);
    }

    [AvaloniaFact]
    public void ALaneCannotBeSqueezedAway()
    {
        var (document, pool, lanes) = Pool();
        var body = pool.Body;

        document.ResizeLane(lanes[0], body.Top - 500);
        Assert.True(lanes[0].Bounds.Height >= DiagramDocument.MinimumLaneHeight - 0.01);

        document.ResizeLane(lanes[0], body.Bottom + 500);
        Assert.True(lanes[1].Bounds.Height >= DiagramDocument.MinimumLaneHeight - 0.01);

        // The bottom lane's lower edge is the pool's, so there is nothing below it to trade with.
        Assert.False(document.ResizeLane(lanes[^1], body.Bottom - 40));
    }

    [AvaloniaFact]
    public void ContentsTravelWithTheBand()
    {
        var (document, pool, lanes) = Pool();
        document.ResizeLane(lanes[0], pool.Body.Top + pool.Body.Height / 3);

        var shape = Harness.Box(document, new Rect(200, lanes[1].Bounds.Center.Y - 20, 100, 40));
        document.Adopt(shape, lanes[1]);

        var offset = shape.Bounds.Top - lanes[1].Bounds.Top;

        document.ResizeLane(lanes[0], pool.Body.Top + pool.Body.Height * 0.5);

        Assert.Equal(offset, shape.Bounds.Top - lanes[1].Bounds.Top, 2);
    }

    [AvaloniaFact]
    public void LanesCanBeEvenedAgain()
    {
        var (document, pool, lanes) = Pool();
        document.ResizeLane(lanes[0], pool.Body.Top + pool.Body.Height * 0.6);

        Assert.True(document.EvenLaneHeights(pool));
        Assert.All(lanes, lane => Assert.Equal(pool.Body.Height / 3, lane.Bounds.Height, 2));
        Assert.False(document.EvenLaneHeights(pool));
    }
}
