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
        // AIMMY V2 CODE - https://github.com/babyhamsta/Aimmy/blob/Aimmy-V2/Aimmy2/InputLogic/MouseManager.cs Line 34 - 50
        private static Point CubicBezier(Point start, Point end, Point control1, Point control2, double t)
        {
            double u = 1 - t;
            double tt = t * t;
            double uu = u * u;

            double x = uu * u * start.X + 3 * uu * t * control1.X + 3 * u * tt * control2.X + tt * t * end.X;
            double y = uu * u * start.Y + 3 * uu * t * control1.Y + 3 * u * tt * control2.Y + tt * t * end.Y;

            return new Point((int)x, (int)y);
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
            Point control1 = new(start.X + (end.X - start.X) / 3, start.Y + (end.Y - start.Y) / 3);
            Point control2 = new(start.X + 2 * (end.X - start.X) / 3, start.Y + 2 * (end.Y - start.Y) / 3);
            Point newPosition = CubicBezier(start, end, control1, control2, Config.Sensitivity);

            int deltaX = newPosition.X - reference.X;
            int deltaY = newPosition.Y - reference.Y;


            mouse_event(0x0001, (uint)(deltaX), (uint)(deltaY), 0, 0);
        }
    }
}
