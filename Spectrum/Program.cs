using OpenCvSharp;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Spectrum
{
    class Program
    {
        static Rectangle bounds = new Rectangle(0, 0, 0, 0);
        static Renderer? renderer = null;
        private static DateTime lastMenuToggle = DateTime.MinValue;
        public static ConfigManager<ConfigData> mainConfig = new ConfigManager<ConfigData>("bin\\configs\\config.json");
        public static ConfigManager<ColorData> colorConfig = new ConfigManager<ColorData>("bin\\configs\\colors.json");
        static void Main()
        {
            Thread renderThread = new Thread(() =>
            {
                try
                {
                    renderer = new Renderer();
                    renderer.Run();
                    LogManager.Log("Renderer started successfully.", LogManager.LogLevel.Info);
                }
                catch (Exception ex)
                {
                    LogManager.Log($"Failed to start renderer: {ex.Message}", LogManager.LogLevel.Error);
                }
            });
            renderThread.Start();

            mainConfig.LoadConfig();
            mainConfig.StartFileWatcher();

            var screenSize = SystemHelper.GetPrimaryScreenSize();

            if (mainConfig.Data.ShowDetectionWindow)
            {
                Cv2.NamedWindow("Spectrum Detection", WindowFlags.AutoSize);
                Cv2.WaitKey(1);
            }
            if (mainConfig.Data.AutoLabel)
            {
                AutoLabeling.StartLabeling();
            }

            Directory.CreateDirectory("bin");
            Directory.CreateDirectory("bin/dataset");
            Directory.CreateDirectory("bin/dataset/images");
            Directory.CreateDirectory("bin/dataset/labels");
            Directory.CreateDirectory("bin/logs");
            Directory.CreateDirectory("bin/configs");

            if (colorConfig.Data.Colors.Count == 0)
            {
                colorConfig.Data.Colors.Add(new ColorInfo("Arsenal [Magenta]", new Scalar(150, 255, 229), new Scalar(150, 255, 229)));
                colorConfig.Data.Colors.Add(new ColorInfo("Combat Surf [Red]", new Scalar(0, 255, 255), new Scalar(0, 255, 255)));
                colorConfig.SaveConfig();
                colorConfig.LoadConfig(true);
                mainConfig.LoadConfig(true);
            }

            while (true)
            {
                if (SystemHelper.GetAsyncKeyState((int)mainConfig.Data.Keybind) < 0)
                {
                    bounds = new Rectangle(
                        (screenSize.Width - mainConfig.Data.ImageWidth) / 2,
                        (screenSize.Height - mainConfig.Data.ImageHeight) / 2,
                        mainConfig.Data.ImageWidth,
                        mainConfig.Data.ImageHeight
                    );
                    var screenshot = CaptureScreenshot(bounds);
                    Mat mat = OpenCvSharp.Extensions.BitmapConverter.ToMat(screenshot);
                    Mat drawing = mat.Clone();
                    Cv2.CvtColor(mat, mat, ColorConversionCodes.BGR2HSV);
                    Cv2.InRange(mat, mainConfig.Data.LowerHSV, mainConfig.Data.UpperHSV, mat);
                    Cv2.Dilate(mat, mat, null, iterations: 2);
                    Cv2.Threshold(mat, mat, 127, 255, ThresholdTypes.Binary);

                    Cv2.FindContours(mat, out var contours, out var hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                    var filteredContours = contours.Where(c => Cv2.ContourArea(c) >= 100).ToArray();

                    var mergedContours = MergeOverlappingContours(filteredContours);

                    var processedContours = mergedContours.Select(ShrinkWideContour).ToArray();

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

                            if (mainConfig.Data.EnableAim)
                            {
                                InputManager.MoveMouse(new System.Drawing.Point(targetX, targetY));
                            }

                            if (mainConfig.Data.ShowDetectionWindow)
                            {
                                Cv2.Rectangle(drawing, new OpenCvSharp.Point(bestMinX, bestMinY), new OpenCvSharp.Point(bestMaxX, bestMaxY), Scalar.Blue, 2);
                                Cv2.ImShow("Spectrum Detection", drawing);
                                Cv2.WaitKey(1);
                            }
                        }
                    }
                    else if (mainConfig.Data.ShowDetectionWindow || mainConfig.Data.CollectData)
                    {
                        Cv2.ImShow("Spectrum Detection", drawing);
                        Cv2.WaitKey(1);
                        if (mainConfig.Data.CollectData)
                            AutoLabeling.AddBackgroundImage(drawing, false);
                    }
                    screenshot?.Dispose();
                    mat.Dispose();
                    drawing.Dispose();
                }
                else if (SystemHelper.GetAsyncKeyState(0x2D) < 0)
                {
                    var now = DateTime.Now;
                    if ((now - lastMenuToggle).TotalMilliseconds > 200)
                    {
                        mainConfig.Data.ShowMenu = !mainConfig.Data.ShowMenu;
                        lastMenuToggle = now;
                    }
                }
                else
                {
                    if (mainConfig.Data.ShowDetectionWindow)
                    {
                        Cv2.WaitKey(1);
                    }
                    else if (!mainConfig.Data.ShowDetectionWindow)
                    {
                        if (Cv2.GetWindowProperty("Spectrum Detection", WindowPropertyFlags.Visible) >= 0)
                        {
                            Cv2.DestroyAllWindows();
                        }
                    }

                }
            }
        }

        static OpenCvSharp.Point[][] MergeOverlappingContours(OpenCvSharp.Point[][] contours)
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

        static OpenCvSharp.Point[] ShrinkWideContour(OpenCvSharp.Point[] contour)
        {
            var rect = Cv2.BoundingRect(contour);
            double aspect = rect.Width / (double)rect.Height;
            if (aspect <= 1.2)
                return contour;

            int[] xs = contour.Select(p => p.X).ToArray();
            int minX = xs.Min();
            int maxX = xs.Max();

            int binCount = Math.Max(10, (maxX - minX + 1) / 5);
            int[] bins = new int[binCount];
            int binWidth = (int)Math.Ceiling((maxX - minX + 1) / (double)binCount);

            foreach (var pt in contour)
            {
                int binIdx = (pt.X - minX) / binWidth;
                if (binIdx < 0) binIdx = 0;
                if (binIdx >= binCount) binIdx = binCount - 1;
                bins[binIdx]++;
            }

            int maxMass = bins.Max();
            int left = 0, right = binCount - 1;
            for (int i = 0; i < binCount; i++)
            {
                if (bins[i] == maxMass)
                {
                    left = i;
                    break;
                }
            }
            for (int i = binCount - 1; i >= 0; i--)
            {
                if (bins[i] == maxMass)
                {
                    right = i;
                    break;
                }
            }
            int cropMinX = minX + left * binWidth;
            int cropMaxX = minX + (right + 1) * binWidth - 1;

            var shrunken = contour.Where(p => p.X >= cropMinX && p.X <= cropMaxX).ToArray();
            if (shrunken.Length < 3)
                return contour;

            return Cv2.ConvexHull(shrunken);
        }

        public static Bitmap CaptureScreenshot(Rectangle bounds)
        {
            Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
            }
            return bitmap;
        }
    }
    public static class SystemHelper
    {
        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;

        public static (int Width, int Height) GetPrimaryScreenSize()
        {
            return (GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));
        }

        public static (int X, int Y, int Width, int Height) GetVirtualScreenBounds()
        {
            return (
                GetSystemMetrics(SM_XVIRTUALSCREEN),
                GetSystemMetrics(SM_YVIRTUALSCREEN),
                GetSystemMetrics(SM_CXVIRTUALSCREEN),
                GetSystemMetrics(SM_CYVIRTUALSCREEN)
            );
        }
    }
}