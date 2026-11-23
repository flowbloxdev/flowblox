using System.Windows;
using System.Windows.Media;

namespace FlowBlox.UICore.Utilities
{
    public static class ArrowGeometryHelper
    {
        /// <summary>
        /// Intersects the offset connection with both card edges instead of shifting already clipped endpoints.
        /// </summary>
        public static (Point Start, Point End) GetConnectionEndpoints(Rect fromBounds, Rect toBounds, double offset)
        {
            var fromCenter = new Point(fromBounds.X + fromBounds.Width / 2d, fromBounds.Y + fromBounds.Height / 2d);
            var toCenter = new Point(toBounds.X + toBounds.Width / 2d, toBounds.Y + toBounds.Height / 2d);
            var displacement = GetPerpendicularOffset(fromCenter, toCenter, offset);

            // Keep the displaced line inside even short cards so it still intersects both boundaries.
            var scale = Math.Min(GetOffsetScale(fromBounds, displacement), GetOffsetScale(toBounds, displacement));
            displacement *= scale;
            var fromOrigin = fromCenter + displacement;
            var toOrigin = toCenter + displacement;

            return (
                GetEdgePoint(fromBounds, fromOrigin, toOrigin),
                GetEdgePoint(toBounds, toOrigin, fromOrigin));
        }

        private static double GetOffsetScale(Rect bounds, Vector displacement)
        {
            const double inset = 0.5d;
            var scale = 1d;
            if (Math.Abs(displacement.X) > 0.001d)
                scale = Math.Min(scale, Math.Max(0d, bounds.Width / 2d - inset) / Math.Abs(displacement.X));
            if (Math.Abs(displacement.Y) > 0.001d)
                scale = Math.Min(scale, Math.Max(0d, bounds.Height / 2d - inset) / Math.Abs(displacement.Y));

            return scale;
        }

        public static Point GetEdgePoint(Rect bounds, Point center, Point other)
        {
            var dx = other.X - center.X;
            var dy = other.Y - center.Y;
            if (Math.Abs(dx) < 0.001d && Math.Abs(dy) < 0.001d)
                return center;

            var tx = dx > 0d
                ? (bounds.Right - center.X) / dx
                : (bounds.Left - center.X) / dx;
            var ty = dy > 0d
                ? (bounds.Bottom - center.Y) / dy
                : (bounds.Top - center.Y) / dy;

            var t = Math.Min(Math.Abs(tx), Math.Abs(ty));
            return new Point(center.X + dx * t, center.Y + dy * t);
        }

        public static PointCollection CreateArrowHeadPoints(Point start, Point end, double length = 11d, double width = 5d)
        {
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var lineLength = Math.Sqrt(dx * dx + dy * dy);
            if (lineLength < 0.001d)
                return new PointCollection { end, end, end };

            var ux = dx / lineLength;
            var uy = dy / lineLength;
            var basePoint = new Point(end.X - ux * length, end.Y - uy * length);
            var perpendicular = new Vector(-uy, ux);

            return new PointCollection
            {
                end,
                new Point(basePoint.X + perpendicular.X * width, basePoint.Y + perpendicular.Y * width),
                new Point(basePoint.X - perpendicular.X * width, basePoint.Y - perpendicular.Y * width)
            };
        }

        public static Vector GetPerpendicularOffset(Point start, Point end, double offset)
        {
            if (Math.Abs(offset) < 0.001d)
                return new Vector();

            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 0.001d)
                return new Vector();

            return new Vector(-dy / length * offset, dx / length * offset);
        }
    }
}
