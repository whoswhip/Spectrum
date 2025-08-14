
namespace Spectrum.Input
{
    public class MovementPaths
    {
        private static double Clamp01(double t) => t < 0 ? 0 : (t > 1 ? 1 : t);
        public static Point CubicBezier(Point start, Point end, Point control1, Point control2, double t)
        {
            t = Clamp01(t);
            double u = 1 - t;
            double tt = t * t;
            double uu = u * u;

            double x = uu * u * start.X + 3 * uu * t * control1.X + 3 * u * tt * control2.X + tt * t * end.X;
            double y = uu * u * start.Y + 3 * uu * t * control1.Y + 3 * u * tt * control2.Y + tt * t * end.Y;

            return new Point((int)Math.Round(x), (int)Math.Round(y));
        }
        public static Point CubicBezierMovement(Point start, Point end, double t)
        {
            t = Clamp01(t);
            Point control1 = new Point(start.X + (end.X - start.X) / 3, start.Y + (end.Y - start.Y) / 3);
            Point control2 = new Point(start.X + 2 * (end.X - start.X) / 3, start.Y + 2 * (end.Y - start.Y) / 3);
            return CubicBezier(start, end, control1, control2, t);
        }

        public static Point CubicBezierCurvedMovement(Point start, Point end, double t, double curvature = 0.25)
        {
            t = Clamp01(t);
            curvature = Math.Max(0, Math.Min(1, curvature));
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist == 0) return start;
            double nx = -dy / dist;
            double ny = dx / dist;
            double offset = dist * curvature;
            var control1 = new Point((int)Math.Round(start.X + dx / 3 + nx * offset), (int)Math.Round(start.Y + dy / 3 + ny * offset));
            var control2 = new Point((int)Math.Round(start.X + 2 * dx / 3 + nx * offset), (int)Math.Round(start.Y + 2 * dy / 3 + ny * offset));
            return CubicBezier(start, end, control1, control2, t);
        }

        public static Point LinearInterpolation(Point start, Point end, double t)
        {
            t = Clamp01(t);
            int x = (int)Math.Round(start.X + t * (end.X - start.X));
            int y = (int)Math.Round(start.Y + t * (end.Y - start.Y));
            return new Point(x, y);
        }

        public static Point CurvedMovement(Point start, Point end, double t)
        {
            t = Clamp01(t);
            Point control = new Point((start.X + end.X) / 2, (start.Y + end.Y) / 2 - 200);
            return QuadraticBezier(start, end, control, t);
        }
        public static Point QuadraticBezier(Point start, Point end, Point control, double t)
        {
            t = Clamp01(t);
            double u = 1 - t;
            double tt = t * t;
            double uu = u * u;
            double x = uu * start.X + 2 * u * t * control.X + tt * end.X;
            double y = uu * start.Y + 2 * u * t * control.Y + tt * end.Y;
            return new Point((int)Math.Round(x), (int)Math.Round(y));
        }

        public static Point AdaptiveMovement(Point start, Point end, double sensitivity)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance == 0) return start;
            double stepSize = Math.Max(0, sensitivity) * distance;
            stepSize = Math.Min(stepSize, distance);
            double directionX = dx / distance;
            double directionY = dy / distance;
            int newX = (int)Math.Round(start.X + directionX * stepSize);
            int newY = (int)Math.Round(start.Y + directionY * stepSize);
            return new Point(newX, newY);
        }
        public static double EmaSmoothing(double previousValue, double currentValue, double smoothingFactor)
        {
            smoothingFactor = Math.Max(0, Math.Min(1, smoothingFactor));
            return currentValue * smoothingFactor + previousValue * (1 - smoothingFactor);
        }

        public static Point EmaSmoothing(Point previous, Point current, double smoothingFactor)
        {
            smoothingFactor = Math.Max(0, Math.Min(1, smoothingFactor));
            int x = (int)Math.Round(EmaSmoothing(previous.X, current.X, smoothingFactor));
            int y = (int)Math.Round(EmaSmoothing(previous.Y, current.Y, smoothingFactor));
            return new Point(x, y);
        }
    }
}
