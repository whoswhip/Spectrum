namespace Spectrum.Input
{
    public class MovementPaths
    {
        private static double Clamp01(double t) => t < 0 ? 0 : (t > 1 ? 1 : t);
        private static readonly int[] _permutation = GeneratePermutation();
        private static readonly int[] _p;
        private static double _perlinTime = 0.0;

        private static Point _lastMove = new Point(0, 0);
        private static double _windX = 0.0;
        private static double _windY = 0.0;
        private static double _velocityX = 0.0;
        private static double _velocityY = 0.0;

        static MovementPaths()
        {
            _p = new int[512];
            for (int i = 0; i < 256; i++)
            {
                _p[256 + i] = _p[i] = _permutation[i];
            }
        }

        private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
        private static double Lerp(double a, double b, double t) => a + t * (b - a);
        private static double Grad(int hash, double x)
        {
            int h = hash & 15;
            double grad = 1 + (h & 7);
            if ((h & 8) != 0) grad = -grad;
            return grad * x;
        }
        private static double Perlin1D(double x)
        {
            int X = (int)Math.Floor(x) & 255;
            x -= Math.Floor(x);
            double u = Fade(x);
            int a = _p[X];
            int b = _p[X + 1];
            double res = Lerp(Grad(a, x), Grad(b, x - 1), u);
            return res * 0.188;
        }

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
        public static Point PerlinNoiseMovement(Point start, Point end, double t, double amplitudeFraction = 0.2, double frequency = 1.5)
        {
            t = Clamp01(t);
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance == 0 || t == 0) return start;

            double baseX = start.X + dx * t;
            double baseY = start.Y + dy * t;

            _perlinTime += t * 0.75;
            double noise = Perlin1D(_perlinTime * frequency);

            double nx = -dy / distance;
            double ny = dx / distance;

            double offsetMag = amplitudeFraction * distance * t;
            double ox = nx * noise * offsetMag;
            double oy = ny * noise * offsetMag;

            int finalX = (int)Math.Round(baseX + ox);
            int finalY = (int)Math.Round(baseY + oy);
            return new Point(finalX, finalY);
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

        public static Point WindMouse(Point start, Point end, double gravity = 9.0, double wind = 3.0,
            double maxStep = 10.0, double sensitivity = 1.0, bool enableOvershoot = false, bool insideBoundingBox = false)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d <= 0) return start;
            double inv = 1.0 / d;
            double dirX = dx * inv;
            double dirY = dy * inv;

            if (insideBoundingBox)
            {
                double s = Math.Min(d * 0.35, maxStep * sensitivity * 0.6);
                _perlinTime += 0.03;
                double n = Math.Sin(_perlinTime * 1.1) * 0.3 + Math.Cos(_perlinTime * 0.9) * 0.2;
                int x = (int)Math.Round(start.X + dirX * s + n);
                int y = (int)Math.Round(start.Y + dirY * s + n * 0.7);
                _windX *= 0.15;
                _windY *= 0.15;
                _velocityX *= 0.3;
                _velocityY *= 0.3;
                return _lastMove = new Point(x, y);
            }

            double distScale = Math.Min(d / 100.0, 3.0);
            double sens = Math.Max(0.1, sensitivity);
            double dyn = maxStep * sens * (1.0 + distScale);
            double g = gravity * (1.0 + distScale * 0.3) * sens * (enableOvershoot ? 1.15 : 1.0);
            double prox = insideBoundingBox ? 0.0 : 1.0;
            double w = wind * Math.Pow(prox, 4.0) * (enableOvershoot ? 1.2 : 1.0);

            if (insideBoundingBox)
            {
                double damp = 0.25;
                _velocityX *= damp;
                _velocityY *= damp;
                double wd = Math.Pow(damp, 4.0);
                _windX *= wd;
                _windY *= wd;
            }

            _windX = _windX / Math.Sqrt(3) + (Random.Shared.NextDouble() * 2 - 1) * w;
            _windY = _windY / Math.Sqrt(3) + (Random.Shared.NextDouble() * 2 - 1) * w;

            _velocityX += _windX + g * dirX;
            _velocityY += _windY + g * dirY;

            double vm = Math.Sqrt(_velocityX * _velocityX + _velocityY * _velocityY);
            if (vm > dyn)
            {
                double r = dyn / vm;
                _velocityX *= r;
                _velocityY *= r;
                vm = dyn;
            }

            if (!enableOvershoot && insideBoundingBox)
            {
                double limit = d * 0.4;
                if (vm > limit && vm > 0)
                {
                    double r = limit / vm;
                    _velocityX *= r;
                    _velocityY *= r;
                }
            }

            int nx = (int)Math.Round(start.X + _velocityX);
            int ny = (int)Math.Round(start.Y + _velocityY);
            return _lastMove = new Point(nx, ny);
        }

        public static void ResetWindMouse()
        {
            _windX = 0.0;
            _windY = 0.0;
            _velocityX = 0.0;
            _velocityY = 0.0;
            _lastMove = new Point(0, 0);
        }

        private static int[] GeneratePermutation()
        {
            int[] perm = new int[256];
            for (int i = 0; i < 256; i++) perm[i] = i;
            for (int i = 255; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                (perm[i], perm[j]) = (perm[j], perm[i]);
            }
            return perm;
        }
    }
}
