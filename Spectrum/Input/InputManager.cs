using Spectrum.Input.InputLibraries.Arduino;
using Spectrum.Input.InputLibraries.Makcu;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MouseEvent = Spectrum.Input.InputLibraries.MouseEvent.MouseMain;

namespace Spectrum.Input
{
    public static class InputManager
    {
        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out Point lpPoint);
        private static ConfigManager<ConfigData> mainConfig = Program.mainConfig;
        private static Point lastDetection = new Point();
        private static Rectangle lastDetectionBox = new Rectangle();
        private static long _lastMoveTicks = 0;
        private static readonly double _dtRef = 1.0 / 120.0;

        private static double GetDeltaSeconds()
        {
            long now = Stopwatch.GetTimestamp();
            if (_lastMoveTicks == 0)
            {
                _lastMoveTicks = now;
                return _dtRef;
            }
            long deltaTicks = now - _lastMoveTicks;
            _lastMoveTicks = now;
            double dt = (double)deltaTicks / Stopwatch.Frequency;

            if (dt < 0.0001) dt = 0.0001; // 10k fps cap
            if (dt > 0.05) dt = 0.05; // 20 fps floor
            return dt;
        }

        private static double TimeScaleFactor(double baseFactor, double deltaSeconds)
        {
            if (baseFactor <= 0) return 0;
            if (baseFactor >= 1)
            {
                double k = deltaSeconds / _dtRef;
                return baseFactor * k;
            }
            double kLow = deltaSeconds / _dtRef;
            return 1 - Math.Pow(1 - baseFactor, kLow);
        }

        public static void MoveMouse()
        {
            var config = mainConfig.Data;
            Point reference = new Point();
            if (config.ClosestToMouse)
            {
                GetCursorPos(out reference);
            }
            else
            {
                reference.X = SystemHelper.GetPrimaryScreenSize().Width / 2;
                reference.Y = SystemHelper.GetPrimaryScreenSize().Height / 2;
            }

            Point start = new Point(reference.X, reference.Y);
            Point end = new Point(lastDetection.X, lastDetection.Y);

            double dt = GetDeltaSeconds();
            double effSensitivity = TimeScaleFactor(config.Sensitivity, dt);

            bool insideBoundingBox = lastDetectionBox.Contains(start);

            Point newPosition = config.AimMovementType switch
            {
                MovementType.CubicBezier => MovementPaths.CubicBezierCurvedMovement(start, end, effSensitivity),
                MovementType.Linear => MovementPaths.LinearInterpolation(start, end, effSensitivity),
                MovementType.Adaptive => MovementPaths.AdaptiveMovement(start, end, effSensitivity),
                MovementType.QuadraticBezier => MovementPaths.CurvedMovement(start, end, effSensitivity),
                MovementType.PerlinNoise => MovementPaths.PerlinNoiseMovement(start, end, effSensitivity),
                MovementType.WindMouse => MovementPaths.WindMouse(start, end, 
                    config.WindMouseGravity, 
                    config.WindMouseWind, 
                    config.WindMouseMaxStep, 
                    config.WindMouseTargetArea, 
                    effSensitivity,
                    config.WindMouseOvershoot,
                    insideBoundingBox),
                _ => MovementPaths.LinearInterpolation(start, end, effSensitivity),
            };

            if (config.EmaSmoothening)
            {
                double emaAlpha = TimeScaleFactor(config.EmaSmootheningFactor, dt);
                newPosition.X = (int)Math.Round(MovementPaths.EmaSmoothing(reference.X, newPosition.X, emaAlpha));
                newPosition.Y = (int)Math.Round(MovementPaths.EmaSmoothing(reference.Y, newPosition.Y, emaAlpha));
            }

            int deltaX = newPosition.X - reference.X;
            int deltaY = newPosition.Y - reference.Y;

            if (Math.Abs(deltaX) < 1 && Math.Abs(deltaY) < 1) return;
            if (deltaX > config.ImageWidth || deltaX < -config.ImageWidth || deltaY > config.ImageHeight || deltaY < -config.ImageHeight)
                return;

            switch (config.MovementMethod)
            {
                case MovementMethod.MouseEvent:
                    MouseEvent.Move(deltaX, deltaY);
                    break;
                case MovementMethod.Makcu:
                    if (EnsureMakcuReady() && MakcuMain.MakcuInstance != null)
                        MakcuMain.MakcuInstance.Move(deltaX, deltaY);
                    else
                        MouseEvent.Move(deltaX, deltaY);
                    break;
                case MovementMethod.Arduino:
                    ArduinoMain.Move(deltaX, deltaY);
                    break;
                default:
                    MouseEvent.Move(deltaX, deltaY);
                    break;
            }
        }

        public static async Task ClickMouse()
        {
            var config = mainConfig.Data;
            if (!config.TriggerBot)
                return;
            if (!IsKeyOrMouseDown(config.TriggerKeybind) || config.TriggerKeybind.Key == Keys.None)
                return;

            var currentPosition = new Point();
            if (config.ClosestToMouse)
            {
                GetCursorPos(out currentPosition);
            }
            else
            {
                currentPosition.X = SystemHelper.GetPrimaryScreenSize().Width / 2;
                currentPosition.Y = SystemHelper.GetPrimaryScreenSize().Height / 2;
            }

            if (config.TriggerDelay > 0)
                await Task.Delay(config.TriggerDelay);

            int radius = config.TriggerFov;

            if (Math.Abs(currentPosition.X - lastDetection.X) < radius && Math.Abs(currentPosition.Y - lastDetection.Y) < radius)
            {
                switch (config.MovementMethod)
                {
                    case MovementMethod.MouseEvent:
                        MouseEvent.ClickDown();
                        break;
                    case MovementMethod.Makcu:
                        if (EnsureMakcuReady() && MakcuMain.MakcuInstance != null)
                            MakcuMain.MakcuInstance.Press(MakcuMouseButton.Left);
                        else
                            MouseEvent.ClickDown();
                        break;
                    case MovementMethod.Arduino:
                        ArduinoMain.ClickDown();
                        break;
                    default:
                        MouseEvent.ClickDown();
                        break;
                }
                await Task.Delay(config.TriggerDuration);
                switch (config.MovementMethod)
                {
                    case MovementMethod.MouseEvent:
                        MouseEvent.ClickUp();
                        break;
                    case MovementMethod.Makcu:
                        if (EnsureMakcuReady() && MakcuMain.MakcuInstance != null)
                            MakcuMain.MakcuInstance.Release(MakcuMouseButton.Left);
                        else
                            MouseEvent.ClickUp();
                        break;
                    case MovementMethod.Arduino:
                        ArduinoMain.ClickUp();
                        break;
                    default:
                        MouseEvent.ClickUp();
                        break;
                }
            }
            else
            {
                var dist = Math.Sqrt(Math.Pow(currentPosition.X - lastDetection.X, 2) + Math.Pow(currentPosition.Y - lastDetection.Y, 2));
            }
        }

        public static void SetLastDetection(Point point)
        {
            lastDetection = point;
        }

        public static void SetLastDetectionBox(Rectangle box)
        {
            lastDetectionBox = box;
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static readonly Keys[] MouseKeys =
        [
            Keys.LButton, Keys.RButton, Keys.MButton, Keys.XButton1, Keys.XButton2
        ];

        public static Task<Keys> ListenForNextKeyOrMouseAsync(CancellationToken? cancellationToken = null)
        {
            var tcs = new TaskCompletionSource<Keys>(TaskCreationOptions.RunContinuationsAsynchronously);

            Action<MakcuMouseButton, bool>? makcuHandler = null;
            bool subscribedToMakcu = false;
            if (mainConfig.Data.MovementMethod == MovementMethod.Makcu && EnsureMakcuReady() && MakcuMain.MakcuInstance != null)
            {
                makcuHandler = (btn, isPressed) =>
                {
                    if (!isPressed) return;
                    var key = MapMakcuButtonToKeys(btn);
                    if (key.HasValue)
                    {
                        tcs.TrySetResult(key.Value);
                    }
                };
                MakcuMain.MakcuInstance.ButtonStateChanged += makcuHandler;
                subscribedToMakcu = true;
            }

            var thread = new Thread(() =>
            {
                try
                {
                    while (true)
                    {
                        if (tcs.Task.IsCompleted)
                        {
                            return;
                        }
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
                }
                finally
                {
                    if (subscribedToMakcu && makcuHandler != null && MakcuMain.MakcuInstance != null)
                    {
                        try { MakcuMain.MakcuInstance.ButtonStateChanged -= makcuHandler; } catch { }
                    }
                }
            })
            { IsBackground = true };

            tcs.Task.ContinueWith(_ =>
            {
                if (subscribedToMakcu && makcuHandler != null && MakcuMain.MakcuInstance != null)
                {
                    try { MakcuMain.MakcuInstance.ButtonStateChanged -= makcuHandler; } catch { }
                }
            }, TaskScheduler.Default);

            thread.Start();
            return tcs.Task;
        }

        private static bool EnsureMakcuReady()
        {
            try
            {
                if (MakcuMain.MakcuInstance?.IsInitializedAndConnected == true) return true;
                var ok = MakcuMain.Load().GetAwaiter().GetResult();
                if (ok && MakcuMain.MakcuInstance?.IsInitializedAndConnected == true) return true;

                LogManager.Log("Makcu not ready. Falling back to MouseEvent.", LogManager.LogLevel.Warning);
                MakcuMain.Unload();
                mainConfig.Data.MovementMethod = MovementMethod.MouseEvent;
                return false;
            }
            catch
            {
                LogManager.Log("Makcu initialization threw an exception. Falling back to MouseEvent.", LogManager.LogLevel.Warning);
                MakcuMain.Unload();
                mainConfig.Data.MovementMethod = MovementMethod.MouseEvent;
                return false;
            }
        }

        private static Keys? MapMakcuButtonToKeys(MakcuMouseButton btn)
        {
            return btn switch
            {
                MakcuMouseButton.Left => Keys.LButton,
                MakcuMouseButton.Right => Keys.RButton,
                MakcuMouseButton.Middle => Keys.MButton,
                MakcuMouseButton.Mouse4 => Keys.XButton1,
                MakcuMouseButton.Mouse5 => Keys.XButton2,
                _ => null
            };
        }

        public static bool IsKeyOrMouseDown(Keys key)
        {
            if (mainConfig.Data.MovementMethod == MovementMethod.Makcu && MakcuMain.MakcuInstance?.IsInitializedAndConnected == true)
            {
                var makcuBtn = MapKeysToMakcuButton(key);
                if (makcuBtn.HasValue)
                {
                    try
                    {
                        var states = MakcuMain.MakcuInstance.GetCurrentButtonStates();

                        return states.TryGetValue(makcuBtn.Value, out var pressed) && pressed;
                    }
                    catch { }
                }
            }
            return (GetAsyncKeyState((int)key) & 0x8000) != 0; ;
        }

        private static readonly Dictionary<string, bool> _keybindToggleStates = new();

        public static bool IsKeyOrMouseDown(Keybind keybind)
        {
            if (keybind.Type == KeybindType.Always)
                return true;

            bool isKeyDown = IsKeyOrMouseDown(keybind.Key);

            if (keybind.Type == KeybindType.Hold)
                return isKeyDown;

            string toggleKey = keybind.Key.ToString();
            
            if (!_keybindToggleStates.ContainsKey(toggleKey))
                _keybindToggleStates[toggleKey] = false;

            if (isKeyDown && !_keybindToggleStates.ContainsKey($"{toggleKey}_pressed"))
            {
                _keybindToggleStates[$"{toggleKey}_pressed"] = true;
                _keybindToggleStates[toggleKey] = !_keybindToggleStates[toggleKey];
            }
            else if (!isKeyDown)
            {
                _keybindToggleStates.Remove($"{toggleKey}_pressed");
            }

            return _keybindToggleStates[toggleKey];
        }

        private static MakcuMouseButton? MapKeysToMakcuButton(Keys key)
        {
            return key switch
            {
                Keys.LButton => MakcuMouseButton.Left,
                Keys.RButton => MakcuMouseButton.Right,
                Keys.MButton => MakcuMouseButton.Middle,
                Keys.XButton1 => MakcuMouseButton.Mouse4,
                Keys.XButton2 => MakcuMouseButton.Mouse5,
                _ => null
            };
        }
    }
}
