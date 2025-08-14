using OpenCvSharp;
using Spectrum.Input;
using System.Diagnostics;

namespace Spectrum.Detection
{
    class DetectionManager
    {
        private long iterationCount = 0;
        private long totalTime = 0;
        private Stopwatch stopwatch = new Stopwatch();
        private Rectangle bounds = new Rectangle(0, 0, 0, 0);
        private (int Width, int Height) screenSize = SystemHelper.GetPrimaryScreenSize();
        private ConfigManager<ConfigData> mainConfig = Program.mainConfig;
        private Renderer? renderer;
        private Thread? _detectionThread;
        private DateTime lastMenuToggle = DateTime.MinValue;
        private readonly CaptureManager _captureManager;

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
            _detectionThread.Start();
        }

        private void DetectionLoop()
        {
            while (true)
            {
                try
                {
                    if (InputManager.IsKeyOrMouseDown(mainConfig.Data.Keybind) || InputManager.IsKeyOrMouseDown(mainConfig.Data.TriggerKey))
                    {
                        stopwatch.Restart();
                        bounds = new Rectangle(
                            (screenSize.Width - mainConfig.Data.ImageWidth) / 2,
                            (screenSize.Height - mainConfig.Data.ImageHeight) / 2,
                            mainConfig.Data.ImageWidth,
                            mainConfig.Data.ImageHeight
                        );
                        var screenshot = null as Bitmap;
                        if (mainConfig.Data.CaptureMethod == CaptureMethod.DirectX && _captureManager.IsDirectXAvailable)
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
                        Mat drawing = mat.Clone();
                        Cv2.CvtColor(mat, mat, ColorConversionCodes.BGR2HSV);
                        Cv2.InRange(mat, mainConfig.Data.LowerHSV, mainConfig.Data.UpperHSV, mat);
                        Cv2.Dilate(mat, mat, null, iterations: 2);
                        Cv2.Threshold(mat, mat, 127, 255, ThresholdTypes.Binary);

                        Cv2.FindContours(mat, out var contours, out var hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                        var filteredContours = contours.Where(c => Cv2.ContourArea(c) >= 50).ToArray();

                        var processedContours = MergeOverlappingContours(filteredContours);

                        if (processedContours.Length > 0)
                        {
                            int refX = bounds.X + bounds.Width / 2;
                            int refY = bounds.Y + (int)(bounds.Height * mainConfig.Data.YOffsetPercent);

                            double minDist = double.MaxValue;
                            OpenCvSharp.Point[]? closestContour = null;
                            int bestMinX = 0, bestMinY = 0, bestMaxX = 0, bestMaxY = 0;

                            foreach (var contour in processedContours)
                            {
                                int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                                foreach (var point in contour)
                                {
                                    if (point.X < minX) minX = point.X;
                                    if (point.Y < minY) minY = point.Y;
                                    if (point.X > maxX) maxX = point.X;
                                    if (point.Y > maxY) maxY = point.Y;
                                }
                                int centerX = (minX + maxX) / 2;
                                int centerY = (minY + maxY) / 2;

                                int absCenterX = centerX + bounds.X;
                                int absCenterY = centerY + bounds.Y;
                                double dist = Math.Sqrt(Math.Pow(absCenterX - refX, 2) + Math.Pow(absCenterY - refY, 2));

                                if (dist < minDist)
                                {
                                    minDist = dist;
                                    closestContour = contour;
                                    bestMinX = minX;
                                    bestMinY = minY;
                                    bestMaxX = maxX;
                                    bestMaxY = maxY;
                                }

                                if (renderer != null && mainConfig.Data.DrawDetections)
                                {
                                    var _rect = new Rectangle(
                                        minX + bounds.X,
                                        minY + bounds.Y,
                                        (maxX - minX + 1),
                                        (maxY - minY + 1)
                                    );
                                    renderer.AddRect(_rect, mainConfig.Data.DetectionColor, 1.0f);
                                }
                            }

                            if (closestContour != null)
                            {
                                int groupX = (int)(bestMinX + (bestMaxX - bestMinX) * (1.0 - mainConfig.Data.XOffsetPercent));
                                int groupY = (int)(bestMinY + (bestMaxY - bestMinY) * (1.0 - mainConfig.Data.YOffsetPercent));
                                int targetX = groupX + bounds.X;
                                int targetY = groupY + bounds.Y;

                                if (mainConfig.Data.AutoLabel)
                                {
                                    AutoLabeling.AddToQueue(drawing, bounds, processedContours);
                                    AutoLabeling.AddBackgroundImage(drawing, true);
                                }
                                if (!mainConfig.Data.AutoLabel && mainConfig.Data.CollectData)
                                {
                                    AutoLabeling.AddBackgroundImage(drawing, false);
                                }

                                InputManager.SetLastDetection(new System.Drawing.Point(targetX, targetY));

                                if (mainConfig.Data.EnableAim)
                                {
                                    InputManager.MoveMouse();
                                }
                                if (mainConfig.Data.TriggerBot)
                                {
                                    var _ = InputManager.ClickMouse();
                                }
                            }
                            if (mainConfig.Data.DebugMode)
                            {
                                iterationCount++;
                                totalTime += stopwatch.ElapsedMilliseconds;
                            }
                        }
                        else if (mainConfig.Data.CollectData)
                        {
                            if (mainConfig.Data.CollectData)
                                AutoLabeling.AddBackgroundImage(drawing, false);
                        }
                        screenshot?.Dispose();
                        mat.Dispose();
                        drawing.Dispose();

                        if (!AutoLabeling.Started && (mainConfig.Data.CollectData || mainConfig.Data.AutoLabel))
                        {
                            AutoLabeling.StartLabeling();
                        }
                        else if (AutoLabeling.Started && !mainConfig.Data.CollectData && !mainConfig.Data.AutoLabel)
                        {
                            AutoLabeling.StopLabeling();
                        }

                        if (iterationCount >= 1000 && mainConfig.Data.DebugMode)
                        {
                            LogManager.Log($"Processed {iterationCount} iterations in {totalTime} ms.", LogManager.LogLevel.Info);
                            int fps = (int)(iterationCount * 1000 / totalTime);
                            LogManager.Log($"Average FPS: {fps}", LogManager.LogLevel.Info);
                            LogManager.Log($"Average time per iteration: {totalTime / iterationCount} ms", LogManager.LogLevel.Info);
                            iterationCount = 0;
                            totalTime = 0;
                        }
                    }
                    else if (InputManager.IsKeyOrMouseDown(mainConfig.Data.MenuKey))
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
        OpenCvSharp.Point[][] MergeOverlappingContours(OpenCvSharp.Point[][] contours)
        {
            var contourRects = new List<(Rect rect, List<int> indices)>();
            for (int i = 0; i < contours.Length; i++)
            {
                var rect = Cv2.BoundingRect(contours[i]);
                contourRects.Add((rect, new List<int> { i }));
            }
            bool merged;
            do
            {
                merged = false;
                for (int i = 0; i < contourRects.Count; i++)
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
                            var newIndices = new List<int>();
                            newIndices.AddRange(indicesA);
                            newIndices.AddRange(indicesB);
                            contourRects[i] = (newRect, newIndices);
                            contourRects.RemoveAt(j);
                            merged = true;
                            break;
                        }
                    }
                    if (merged) break;
                }
            } while (merged);

            var mergedContours = new List<OpenCvSharp.Point[]>();
            foreach (var (rect, indices) in contourRects)
            {
                var points = new List<OpenCvSharp.Point>();
                foreach (var idx in indices)
                {
                    points.AddRange(contours[idx]);
                }
                var hull = Cv2.ConvexHull(points);
                mergedContours.Add(hull);
            }
            return mergedContours.ToArray();
        }
    }
}