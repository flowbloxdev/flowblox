using System.Windows;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.UICore.ViewModels.ProjectPanel;
using FlowBlox.UICore.Utilities;

namespace FlowBloxTest.Util;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class FlowBlockArrowGeometryTests
{
    [TestMethod]
    [DataRow(800d, 350d, "recursive", 18d)]
    [DataRow(-800d, -350d, "recursive", 18d)]
    [DataRow(150d, 600d, "iteration", 10d)]
    [DataRow(800d, 0d, "recursive", 18d)]
    [DataRow(0d, 600d, "iteration", 10d)]
    [DataRow(800d, 350d, "invoke", 0d)]
    public void ConnectionEndpoints_StayOnBothCardEdges(double x, double y, string kind, double offset)
    {
        using var from = new FlowBlockNodeViewModel(new StartFlowBlock { Name = "From" });
        using var to = new FlowBlockNodeViewModel(new StartFlowBlock
        {
            Name = "To",
            Location = new System.Drawing.Point((int)x, (int)y)
        });
        var arrow = new FlowBlockArrowViewModel(from, to, kind, offset);
        AssertAnchored(arrow, from, to);

        to.InternalFlowBlock.Location = new System.Drawing.Point((int)y, (int)x);
        arrow.NotifyGeometryChanged();
        AssertAnchored(arrow, from, to);
    }

    [TestMethod]
    [DataRow(18d)]
    [DataRow(-18d)]
    public void OffsetLargerThanCardHalfHeight_IsLimitedToKeepBothEndpointsAttached(double offset)
    {
        var from = new Rect(0, 0, 328, 20);
        var to = new Rect(800, -20, 328, 60);
        var endpoints = ArrowGeometryHelper.GetConnectionEndpoints(from, to, offset);

        AssertOnEdge(from, endpoints.Start);
        AssertOnEdge(to, endpoints.End);
        Assert.AreEqual(endpoints.Start.Y, endpoints.End.Y, 0.000001d);
        Assert.AreEqual(Math.Sign(offset), Math.Sign(endpoints.Start.Y - 10d));
    }

    private static void AssertAnchored(FlowBlockArrowViewModel arrow, FlowBlockNodeViewModel from, FlowBlockNodeViewModel to)
    {
        AssertOnEdge(new Rect(from.X, from.Y, from.Width, from.Height), new Point(arrow.X1, arrow.Y1));
        AssertOnEdge(new Rect(to.X, to.Y, to.Width, to.Height), new Point(arrow.X2, arrow.Y2));
        var direction = new Vector(to.X + to.Width / 2 - from.X - from.Width / 2, to.Y + to.Height / 2 - from.Y - from.Height / 2);
        var line = new Vector(arrow.X2 - arrow.X1, arrow.Y2 - arrow.Y1);
        Assert.AreEqual(0d, Vector.CrossProduct(direction, line), 0.00001d, "The offset connection must remain parallel to the center line.");
    }

    private static void AssertOnEdge(Rect bounds, Point point)
    {
        const double tolerance = 0.000001d;
        Assert.IsTrue(point.X >= bounds.Left - tolerance && point.X <= bounds.Right + tolerance);
        Assert.IsTrue(point.Y >= bounds.Top - tolerance && point.Y <= bounds.Bottom + tolerance);
        var distance = new[]
        {
            Math.Abs(point.X - bounds.Left),
            Math.Abs(point.X - bounds.Right),
            Math.Abs(point.Y - bounds.Top),
            Math.Abs(point.Y - bounds.Bottom)
        }.Min();
        Assert.IsTrue(distance < tolerance, $"Endpoint {point} is detached from card {bounds}.");
    }
}
