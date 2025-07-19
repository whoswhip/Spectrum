using System.Runtime.InteropServices;

namespace Spectrum
{
    public static class InputManager
    {
        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out Point lpPoint);
        private static ConfigManager<ConfigData> mainConfig = Program.mainConfig;
        private static Point lastDetection = new Point();

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

        private static Point CurvedMovement(Point start, Point end, double t)
        {
            Point control = new Point((start.X + end.X) / 2, (start.Y + end.Y) / 2 - 200);
            return QuadraticBezier(start, end, control, t);
        }
        private static Point QuadraticBezier(Point start, Point end, Point control, double t)
        {
            double u = 1 - t;
            double tt = t * t;
            double uu = u * u;
            double x = uu * start.X + 2 * u * t * control.X + tt * end.X;
            double y = uu * start.Y + 2 * u * t * control.Y + tt * end.Y;
            return new Point((int)x, (int)y);
        }

        private static Point AdaptiveMovement(Point start, Point end, double sensitivity)
        {
            double distance = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
            double stepSize = distance * sensitivity;

            double directionX = (end.X - start.X) / distance;
            double directionY = (end.Y - start.Y) / distance;

            int newX = (int)(start.X + directionX * stepSize);
            int newY = (int)(start.Y + directionY * stepSize);
            return new Point(newX, newY);
        }
        private static double EmaSmoothing(double previousValue, double currentValue, double smoothingFactor) => (currentValue * smoothingFactor) + (previousValue * (1 - smoothingFactor));

        public static void MoveMouse(Point target)
        {
            Point reference = new Point();
            if (mainConfig.Data.ClosestToMouse)
            {
                GetCursorPos(out reference);
            }
            else
            {
                // center of the screen
                reference.X = SystemHelper.GetPrimaryScreenSize().Width / 2;
                reference.Y = SystemHelper.GetPrimaryScreenSize().Height / 2;
            }

            lastDetection = target;

            Point start = new Point(reference.X, reference.Y);
            Point end = new Point(target.X, target.Y);
            Point newPosition = mainConfig.Data.AimMovementType switch
            {
                MovementType.CubicBezier => CubicBezierMovement(start, end, mainConfig.Data.Sensitivity),
                MovementType.Linear => LinearInterpolation(start, end, mainConfig.Data.Sensitivity),
                MovementType.Adaptive => AdaptiveMovement(start, end, mainConfig.Data.Sensitivity),
                MovementType.QuadraticBezier => CurvedMovement(start, end, mainConfig.Data.Sensitivity),
                _ => end
            };

            if (mainConfig.Data.EmaSmoothening)
            {
                newPosition.X = (int)EmaSmoothing(reference.X, newPosition.X, mainConfig.Data.EmaSmootheningFactor);
                newPosition.Y = (int)EmaSmoothing(reference.Y, newPosition.Y, mainConfig.Data.EmaSmootheningFactor);
            }

            int deltaX = newPosition.X - reference.X;
            int deltaY = newPosition.Y - reference.Y;

            if (Math.Abs(deltaX) < 1 && Math.Abs(deltaY) < 1) return;
            if (deltaX > mainConfig.Data.ImageWidth || deltaX < -mainConfig.Data.ImageWidth || deltaY > mainConfig.Data.ImageHeight || deltaY < -mainConfig.Data.ImageHeight)
                return;

            mouse_event(0x0001, (uint)(deltaX), (uint)(deltaY), 0, 0);
            ClickMouse();
        }

        public static void ClickMouse()
        {
            if (!mainConfig.Data.TriggerBot)
                return;

            var currentMousePosition = new Point();
            if (mainConfig.Data.ClosestToMouse)
            {
                GetCursorPos(out currentMousePosition);
            }
            else
            {
                // center of the screen
                currentMousePosition.X = SystemHelper.GetPrimaryScreenSize().Width / 2;
                currentMousePosition.Y = SystemHelper.GetPrimaryScreenSize().Height / 2;
            }

            if (Math.Abs(currentMousePosition.X - lastDetection.X) < 25 && Math.Abs(currentMousePosition.Y - lastDetection.Y) < 25)
            {
                mouse_event(0x0002, 0, 0, 0, 0);
                Thread.Sleep(5);
                mouse_event(0x0004, 0, 0, 0, 0);
            }
        }
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static readonly Keys[] MouseKeys =
        {
            Keys.LButton, Keys.RButton, Keys.MButton, Keys.XButton1, Keys.XButton2
        };

        public static Task<Keys> ListenForNextKeyOrMouseAsync(CancellationToken? cancellationToken = null)
        {
            var tcs = new TaskCompletionSource<Keys>();
            var thread = new Thread(() =>
            {
                while (true)
                {
                    for (int key = 0x08; key <= 0xFE; key++)
                    {
                        if ((GetAsyncKeyState(key) & 0x8000) != 0)
                        {
                            tcs.TrySetResult((Keys)key);
                            return;
                        }
                    }
                    foreach (var mouseKey in MouseKeys)
                    {
                        if ((GetAsyncKeyState((int)mouseKey) & 0x8000) != 0)
                        {
                            tcs.TrySetResult(mouseKey);
                            return;
                        }
                    }
                    if (cancellationToken?.IsCancellationRequested == true)
                    {
                        tcs.TrySetCanceled();
                        return;
                    }
                    Thread.Sleep(10);
                }
            })
            {
                IsBackground = true
            };
            thread.Start();
            return tcs.Task;
        }
    }
}
