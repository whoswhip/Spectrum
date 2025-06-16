using OpenCvSharp;
using OpenCvSharp.Internal.Vectors;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Spectrum
{
    class Program
    {
        static Rectangle bounds = new Rectangle(0, 0, 0, 0);
        static void Main()
        {
            Config.LoadConfig();
            var screenSize = SystemHelper.GetPrimaryScreenSize();
            bounds = new Rectangle(
                (screenSize.Width - Config.ImageWidth) / 2,
                (screenSize.Height - Config.ImageHeight) / 2,
                Config.ImageWidth,
                Config.ImageHeight
                );

            if (Config.ShowDetectionWindow)
            {
                Cv2.NamedWindow("Spectrum Detection", WindowFlags.AutoSize);
                Cv2.WaitKey(1);
            }
            if (Config.AutoLabel)
            {
                AutoLabeling.StartLabeling();
            }

            Directory.CreateDirectory("dataset");
            Directory.CreateDirectory("dataset/images");
            Directory.CreateDirectory("dataset/labels");

            while (true)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;
                    if (key == ConsoleKey.Escape)
                    {
                        if (Config.ShowDetectionWindow)
                        {
                            Cv2.DestroyAllWindows();
                        }
                    }
                    else if (key == ConsoleKey.F5 || key == ConsoleKey.R)
                    {
                        Config.LoadConfig(true);
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("[INFO] Configuration reloaded.");
                        Console.ResetColor();
                    }
                }

                if (SystemHelper.GetAsyncKeyState(Config.Keybind) < 0)
                {
                    var screenshot = CaptureScreenshot(bounds);
                    Mat mat = OpenCvSharp.Extensions.BitmapConverter.ToMat(screenshot);
                    Mat drawing = mat.Clone(); // clone for drawing
                    Cv2.CvtColor(mat, mat, ColorConversionCodes.BGR2HSV); // convert to hsv
                    Cv2.InRange(mat, Config.LowerHSV, Config.UpperHSV, mat); // apply mask
                    Cv2.Dilate(mat, mat, null, iterations: 2); // dilate to fill gaps
                    Cv2.Threshold(mat, mat, 127, 255, ThresholdTypes.Binary); // threshold to binary

                    Cv2.FindContours(mat, out var contours, out var hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                    var filteredContours = contours.Where(c => Cv2.ContourArea(c) >= 100).ToArray();
                    if (filteredContours.Length > 0)
                    {
                        int refX = bounds.X + bounds.Width / 2;
                        int refY = bounds.Y + (int)(bounds.Height * Config.YOffsetPercent);

                        double minDist = double.MaxValue;
                        OpenCvSharp.Point[]? closestContour = null;
                        int bestMinX = 0, bestMinY = 0, bestMaxX = 0, bestMaxY = 0;

                        foreach (var contour in filteredContours)
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
                        }

                        if (closestContour != null)
                        {
                            int groupX = (int)(bestMinX + (bestMaxX - bestMinX) * (1.0 - Config.XOffsetPercent));
                            int groupY = (int)(bestMinY + (bestMaxY - bestMinY) * (1.0 - Config.YOffsetPercent));
                            int targetX = groupX + bounds.X;
                            int targetY = groupY + bounds.Y;

                            if (Config.AutoLabel)
                            {
                                AutoLabeling.AddToQueue(drawing, bounds, filteredContours);
                                AutoLabeling.AddBackgroundImage(drawing, true);
                            }

                            if (Config.EnableAim)
                            {
                                InputManager.MoveMouse(new System.Drawing.Point(targetX, targetY));
                            }

                            if (Config.ShowDetectionWindow)
                            {
                                Cv2.DrawContours(drawing, filteredContours, -1, Scalar.Red, 2);
                                Cv2.Rectangle(drawing, new OpenCvSharp.Point(bestMinX, bestMinY), new OpenCvSharp.Point(bestMaxX, bestMaxY), Scalar.Blue, 2);
                                Cv2.ImShow("Spectrum Detection", drawing);
                                Cv2.WaitKey(1);
                            }
                        }
                    }
                    else if (Config.ShowDetectionWindow)
                    {
                        Cv2.ImShow("Spectrum Detection", drawing);
                        Cv2.WaitKey(1);
                    }
                }
                else
                {
                    if (Config.ShowDetectionWindow)
                    {
                        Cv2.WaitKey(1);
                    }
                    else
                    {
                        Thread.Sleep(1);
                    }

                }
            }
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