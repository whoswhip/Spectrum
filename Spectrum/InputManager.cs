using System.Drawing;
using System.Runtime.InteropServices;

namespace Spectrum
{
    public static class InputManager
    {
        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        private struct POINT
        {
            public int X;
            public int Y;
        }
        // AIMMY V2 CODE - https://github.com/Babyhamsta/Aimmy/blob/2e906552673e359ded90383c5e97d79c8f38a2f2/Aimmy2/InputLogic/MouseManager.cs#L34-L50
        private static Point CubicBezier(Point start, Point end, Point control1, Point control2, double t)
        {
            double u = 1 - t;
            double tt = t * t;
            double uu = u * u;

            double x = uu * u * start.X + 3 * uu * t * control1.X + 3 * u * tt * control2.X + tt * t * end.X;
            double y = uu * u * start.Y + 3 * uu * t * control1.Y + 3 * u * tt * control2.Y + tt * t * end.Y;

            return new Point((int)x, (int)y);
        }
        private static Point CubicBezierMovement(Point start, Point end, double t)
        {
            Point control1 = new Point(start.X + (end.X - start.X) / 3, start.Y + (end.Y - start.Y) / 3);
            Point control2 = new Point(start.X + 2 * (end.X - start.X) / 3, start.Y + 2 * (end.Y - start.Y) / 3);
            return CubicBezier(start, end, control1, control2, t);
        }

        private static Point LinearInterpolation(Point start, Point end, double t)
        {
            int x = (int)(start.X + t * (end.X - start.X));
            int y = (int)(start.Y + t * (end.Y - start.Y));
            return new Point(x, y);
        }

        private static Point AdaptiveMovement(Point start, Point end, double sensitivity)
        {
            // Calculate the distance between the start and end points
            double distance = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
            // Calculate the step size based on sensitivity
            double stepSize = distance * sensitivity;
            // Calculate the direction vector
            double directionX = (end.X - start.X) / distance;
            double directionY = (end.Y - start.Y) / distance;
            // Calculate the new position
            int newX = (int)(start.X + directionX * stepSize);
            int newY = (int)(start.Y + directionY * stepSize);
            return new Point(newX, newY);
        }

        public static void MoveMouse(Point target)
        {
            POINT reference = new POINT();
            if (Config.ClosestToMouse)
            {
                GetCursorPos(out reference);
            }
            else
            {
                // center of the screen
                reference.X = SystemHelper.GetPrimaryScreenSize().Width / 2;
                reference.Y = SystemHelper.GetPrimaryScreenSize().Height / 2;
            }



            Point start = new Point(reference.X, reference.Y);
            Point end = new Point(target.X, target.Y);
            Point newPosition = Config.AimMovementType switch
            {
                Config.MovementType.CubicBezier => CubicBezierMovement(start, end, Config.Sensitivity),
                Config.MovementType.Linear => LinearInterpolation(start, end, Config.Sensitivity),
                Config.MovementType.Adaptive => AdaptiveMovement(start, end, Config.Sensitivity),
                _ => end
            };

            int deltaX = newPosition.X - reference.X;
            int deltaY = newPosition.Y - reference.Y;


            mouse_event(0x0001, (uint)(deltaX), (uint)(deltaY), 0, 0);
        }
    }
}
