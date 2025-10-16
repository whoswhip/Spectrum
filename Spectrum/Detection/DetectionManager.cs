using OpenCvSharp;
using Spectrum.Input;
using System.Diagnostics;

namespace Spectrum.Detection
{
    class DetectionManager
    {
        private readonly Stopwatch stopwatch = new Stopwatch();
        private Rectangle bounds = new Rectangle(0, 0, 0, 0);
        private readonly (int Width, int Height) screenSize = SystemHelper.GetPrimaryScreenSize();
        private readonly int minArea;
        private readonly ConfigManager<ConfigData> mainConfig = Program.mainConfig;
        private readonly Renderer? renderer;
        private readonly Thread? _detectionThread;
        private DateTime lastMenuToggle = DateTime.MinValue;
        private readonly CaptureManager _captureManager;
        private readonly Queue<double> processTimes = new Queue<double>(100);
        private DateTime lastFpsUpdate = DateTime.Now;
        private int frameCount = 0;

        public DetectionManager(Renderer renderer)
        {
            this.renderer = renderer;
            _captureManager = Program.SharedCaptureManager;
            _detectionThread = new Thread(DetectionLoop)
            {
                IsBackground = true,
                Name = "Spectrum Detection Thread",
                Priority = ThreadPriority.AboveNormal
            };
            minArea = (100 / 1440) * screenSize.Height;
            _detectionThread.Start();
        }

        private void DetectionLoop()
        {
            while (true)
            {
                try
                {
                    if (InputManager.IsKeyOrMouseDown(mainConfig.Data.Keybind) || InputManager.IsKeyOrMouseDown(mainConfig.Data.TriggerKeybind))
                    {
                        var config = mainConfig.Data;
                        if (config.DebugMode)
                            stopwatch.Restart();

                        renderer?.ClearDetectionDrawCommands();

                        bounds = new Rectangle(
                            (screenSize.Width - config.ImageWidth) / 2,
                            (screenSize.Height - config.ImageHeight) / 2,
                            config.ImageWidth,
                            config.ImageHeight
                        );
                        var screenshot = null as Bitmap;
                        if (config.CaptureMethod == CaptureMethod.DirectX && _captureManager.IsDirectXAvailable)
                            screenshot = _captureManager.CaptureScreenshotDirectX(bounds);
                        else
                        {
                            if (_captureManager.IsInitialized)
                                _captureManager.Dispose();
                            screenshot = _captureManager.CaptureScreenshotGdi(bounds);
                        }

                        if (screenshot == null)
                        {
                            LogManager.Log("Capture returned null screenshot. Skipping iteration.", LogManager.LogLevel.Debug);
                            Thread.Sleep(2);
                            continue;
                        }

                        Mat mat = OpenCvSharp.Extensions.BitmapConverter.ToMat(screenshot);
                        screenshot.Dispose();

                        Mat? drawing = (config.AutoLabel || config.CollectData) ? mat.Clone() : null;

                        Cv2.CvtColor(mat, mat, ColorConversionCodes.BGR2HSV);
                        Cv2.InRange(mat, config.LowerHSV, config.UpperHSV, mat);
                        Cv2.Dilate(mat, mat, null, iterations: 2);
                        Cv2.Threshold(mat, mat, 127, 255, ThresholdTypes.Binary);

                        Cv2.FindContours(mat, out var contours, out var hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                        var filteredContours = contours.Where(c => Cv2.ContourArea(c) >= minArea).ToArray();

                        var processedContours = MergeOverlappingContours(filteredContours);

                        if (processedContours.Length > 0)
                        {
                            int refX = bounds.X + bounds.Width / 2;
                            int refY = bounds.Y + (int)(bounds.Height * config.YOffsetPercent);

                            double minDistSquared = double.MaxValue;
                            OpenCvSharp.Point[]? closestContour = null;
                            Rect bestRect = default;

                            bool shouldDrawDetections = config.DrawDetections && renderer != null;

                            foreach (var contour in processedContours)
                            {
                                var rect = Cv2.BoundingRect(contour);

                                int centerX = rect.X + rect.Width / 2;
                                int centerY = rect.Y + rect.Height / 2;

                                int absCenterX = centerX + bounds.X;
                                int absCenterY = centerY + bounds.Y;

                                double distSquared = (absCenterX - refX) * (absCenterX - refX) +
                                                    (absCenterY - refY) * (absCenterY - refY);

                                if (distSquared < minDistSquared)
                                {
                                    minDistSquared = distSquared;
                                    closestContour = contour;
                                    bestRect = rect;
                                }

                                if (shouldDrawDetections)
                                {
                                    var _rect = new Rectangle(
                                        rect.X + bounds.X,
                                        rect.Y + bounds.Y,
                                        rect.Width,
                                        rect.Height
                                    );
                                    renderer!.AddRect(_rect, config.DetectionColor, 1.0f);
                                }
                            }

                            if (closestContour != null)
                            {

                                if (shouldDrawDetections && config.HighlightTarget)
                                {
                                    var _rect = new Rectangle(
                                        bestRect.X + bounds.X,
                                        bestRect.Y + bounds.Y,
                                        bestRect.Width,
                                        bestRect.Height
                                    );
                                    renderer!.AddRect(_rect, config.TargetColor, 1.0f);
                                }

                                int groupX;
                                int groupY;
                                if (config.XPixelOffset)
                                    groupX = bestRect.X + config.XOffsetPixels;
                                else
                                    groupX = (int)(bestRect.X + bestRect.Width * config.XOffsetPercent);
                                if (config.YPixelOffset)
                                    groupY = bestRect.Y + config.YOffsetPixels;
                                else
                                    groupY = (int)(bestRect.Y + bestRect.Height * (1 - config.YOffsetPercent));

                                int targetX = groupX + bounds.X;
                                int targetY = groupY + bounds.Y;

                                if (config.DrawAimPoint)
                                {
                                    renderer!.AddLine(new System.Numerics.Vector2(targetX - 10, targetY), new System.Numerics.Vector2(targetX + 10, targetY), config.AimPointColor, 2);
                                    renderer!.AddLine(new System.Numerics.Vector2(targetX, targetY - 10), new System.Numerics.Vector2(targetX, targetY + 10), config.AimPointColor, 2);
                                    renderer!.AddCircle(new System.Numerics.Vector2(targetX, targetY), 5, config.AimPointColor, 20);
                                }

                                if (config.AutoLabel && drawing != null)
                                {
                                    AutoLabeling.AddToQueue(drawing, bounds, processedContours);
                                    AutoLabeling.AddBackgroundImage(drawing, true);
                                }
                                if (!config.AutoLabel && config.CollectData && drawing != null)
                                {
                                    AutoLabeling.AddBackgroundImage(drawing, false);
                                }

                                InputManager.SetLastDetection(new System.Drawing.Point(targetX, targetY));

                                if (config.EnableAim)
                                {
                                    InputManager.MoveMouse();
                                }
                                if (config.TriggerBot)
                                {
                                    var _ = InputManager.ClickMouse();
                                }
                                if (renderer != null && config.DrawTriggerFov && config.TriggerBot)
                                {
                                    renderer.AddCircle(new System.Numerics.Vector2(targetX, targetY), config.TriggerFov, config.TriggerRadiusColor, 100);
                                }
                            }
                        }
                        else if (config.CollectData && drawing != null)
                        {
                            AutoLabeling.AddBackgroundImage(drawing, false);
                        }

                        renderer?.CommitDetectionDrawCommands();

                        mat.Dispose();
                        drawing?.Dispose();

                        if (!AutoLabeling.Started && (config.CollectData || config.AutoLabel))
                        {
                            AutoLabeling.StartLabeling();
                        }
                        else if (AutoLabeling.Started && !config.CollectData && !config.AutoLabel)
                        {
                            AutoLabeling.StopLabeling();
                        }

                        if (config.DebugMode)
                        {
                            stopwatch.Stop();
                            double processTime = stopwatch.Elapsed.TotalMilliseconds;
                            processTimes.Enqueue(processTime);
                            if (processTimes.Count > 100)
                                processTimes.Dequeue();

                            frameCount++;
                            var now = DateTime.Now;
                            var timeSinceLastUpdate = (now - lastFpsUpdate).TotalSeconds;
                            if (timeSinceLastUpdate >= 1.0)
                            {
                                int fps = (int)(frameCount / timeSinceLastUpdate);
                                double avgProcessTime = processTimes.Count > 0 ? processTimes.Average() : 0;
                                Program.statistics = (fps, avgProcessTime);
                                frameCount = 0;
                                lastFpsUpdate = now;
                            }
                        }
                    }
                    else
                    {
                        renderer?.ClearDetectionDrawCommands();
                        renderer?.CommitDetectionDrawCommands();
                        Thread.Sleep(5);
                    }
                    if (InputManager.IsKeyOrMouseDown(mainConfig.Data.MenuKey))
                    {
                        var now = DateTime.Now;
                        if ((now - lastMenuToggle).TotalMilliseconds > 175)
                        {
                            mainConfig.Data.ShowMenu = !mainConfig.Data.ShowMenu;
                            lastMenuToggle = now;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogManager.Log($"DetectionLoop error: {ex.GetType().Name}: {ex.Message}", LogManager.LogLevel.Error);
                    Thread.Sleep(5);
                }
                finally
                {
                    Thread.Sleep(1);
                }
            }
        }
        static OpenCvSharp.Point[][] MergeOverlappingContours(OpenCvSharp.Point[][] contours)
        {
            if (contours.Length <= 1)
                return contours;

            var contourRects = new List<(Rect rect, List<int> indices)>(contours.Length);
            for (int i = 0; i < contours.Length; i++)
            {
                var rect = Cv2.BoundingRect(contours[i]);
                contourRects.Add((rect, new List<int> { i }));
            }

            bool merged;
            do
            {
                merged = false;
                for (int i = 0; i < contourRects.Count - 1; i++)
                {
                    var (rectA, indicesA) = contourRects[i];
                    for (int j = i + 1; j < contourRects.Count; j++)
                    {
                        var (rectB, indicesB) = contourRects[j];
                        if (rectA.IntersectsWith(rectB))
                        {
                            var newRect = new Rect(
                                Math.Min(rectA.X, rectB.X),
                                Math.Min(rectA.Y, rectB.Y),
                                Math.Max(rectA.X + rectA.Width, rectB.X + rectB.Width) - Math.Min(rectA.X, rectB.X),
                                Math.Max(rectA.Y + rectA.Height, rectB.Y + rectB.Height) - Math.Min(rectA.Y, rectB.Y)
                            );
                            indicesA.AddRange(indicesB);
                            contourRects[i] = (newRect, indicesA);
                            contourRects.RemoveAt(j);
                            merged = true;
                            break;
                        }
                    }
                    if (merged) break;
                }
            } while (merged);

            var mergedContours = new List<OpenCvSharp.Point[]>(contourRects.Count);
            foreach (var (rect, indices) in contourRects)
            {
                var pointsList = new List<OpenCvSharp.Point>(indices.Count * 10);
                foreach (var idx in indices)
                {
                    pointsList.AddRange(contours[idx]);
                }
                var hull = Cv2.ConvexHull(pointsList);
                mergedContours.Add(hull);
            }
            return mergedContours.ToArray();
        }
    }
}