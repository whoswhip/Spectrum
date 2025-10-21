using OpenCvSharp;
using System.Collections.Concurrent;
using System.Text;
using LogLevel = Spectrum.LogManager.LogLevel;

namespace Spectrum.Detection
{
    public static class AutoLabeling
    {
        private static int imageCount = 0;
        private static int detectionAttemps = 0;
        private static readonly object lockObject = new object();
        private static CancellationTokenSource? cancellationTokenSource;
        private static Task? backgroundTask;
        private static readonly ConcurrentQueue<LabelingData> labelingQueue = new ConcurrentQueue<LabelingData>();
        private static readonly ConcurrentQueue<BackgroundImageData> backgroundQueue = new ConcurrentQueue<BackgroundImageData>();
        private static ConfigManager<ConfigData> mainConfig = Program.mainConfig;
        public static bool Started = false;

        private static List<YoloBoundingBox> GetBoundingBoxes(OpenCvSharp.Point[][] contours, int imageWidth, int imageHeight)
        {
            var boundingBoxes = new List<YoloBoundingBox>();
            foreach (var contour in contours)
            {
                if (contour.Length < 3) continue; // probably not an actual enemy
                int minX = contour.Min(p => p.X);
                int minY = contour.Min(p => p.Y);
                int maxX = contour.Max(p => p.X);
                int maxY = contour.Max(p => p.Y);
                double centerX = (minX + maxX) / 2.0;
                double centerY = (minY + maxY) / 2.0;
                double width = maxX - minX;
                double height = maxY - minY;
                boundingBoxes.Add(new YoloBoundingBox
                {
                    ClassId = 0,
                    CenterX = centerX / imageWidth,
                    CenterY = centerY / imageHeight,
                    Width = width / imageWidth,
                    Height = height / imageHeight,
                    PixelMinX = minX,
                    PixelMinY = minY,
                    PixelMaxX = maxX,
                    PixelMaxY = maxY
                });
            }
            return boundingBoxes;
        }
        private static void SaveLabels(string path, List<YoloBoundingBox> boundingBoxes)
        {
            try
            {
                if (!Directory.Exists(Path.GetDirectoryName(path)))
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                var labelContent = new StringBuilder();
                foreach (var box in boundingBoxes)
                {
                    labelContent.AppendLine($"{box.ClassId} {box.CenterX} {box.CenterY} {box.Width} {box.Height}");
                }
                File.WriteAllText(path, labelContent.ToString());
            }
            catch (Exception ex)
            {
                LogManager.Log($"[ERROR] Failed to save labels. Error: {ex.Message}", LogLevel.Error);
            }
        }
        private static void SaveMatAsImage(Mat mat, string path)
        {
            try
            {
                if (!Directory.Exists(Path.GetDirectoryName(path)))
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                Cv2.ImWrite(path, mat);
            }
            catch (Exception ex)
            {
                LogManager.Log($"[ERROR] Failed to save image: {ex.Message}", LogLevel.Error);
            }
        }

        private static async Task LabelingLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (labelingQueue.TryDequeue(out LabelingData? data))
                {
                    try
                    {
                        await ProcessImageAsync(data);
                    }
                    catch
                    {
                        LogManager.Log("[ERROR] Failed to process labeling data.", LogLevel.Error);
                    }
                }

                if (backgroundQueue.TryDequeue(out BackgroundImageData? backgroundData))
                {
                    try
                    {
                        await ProcessBackgroundImageAsync(backgroundData);
                    }
                    catch
                    {
                        LogManager.Log("[ERROR] Failed to process background image data.", LogLevel.Error);
                    }
                }

                await Task.Delay(10, cancellationToken);
            }
        }


        public static void AddToQueue(Mat mat, Rectangle bounds, OpenCvSharp.Point[][] filteredContours)
        {
            if (!mainConfig.Data.AutoLabel || filteredContours.Length == 0)
                return;

            if (labelingQueue.Count > 250)
                return;

            Mat clone = mat.Clone();
            labelingQueue.Enqueue(new LabelingData
            {
                Mat = clone,
                Bounds = bounds,
                FilteredContours = filteredContours
            });
        }

        public static void AddBackgroundImage(Mat mat, bool detected)
        {
            detectionAttemps++;

            if (detectionAttemps < mainConfig.Data.BackgroundImageInterval || detected)
                return;

            if (backgroundQueue.Count > 250)
                return;

            Mat clone = mat.Clone();
            backgroundQueue.Enqueue(new BackgroundImageData
            {
                Mat = clone
            });
        }

        private static async Task ProcessImageAsync(LabelingData data)
        {
            await Task.Run(() =>
            {
                try
                {
                    var boundingBoxes = GetBoundingBoxes(data.FilteredContours, data.Mat.Width, data.Mat.Height);

                    if (boundingBoxes.Count == 0)
                        return;

                    lock (lockObject)
                    {
                        string imageFileName = $"image_{imageCount:D6}.jpg";
                        string labelFileName = $"image_{imageCount:D6}.txt";

                        string imagePath = Path.Combine("bin/dataset/images", imageFileName);
                        string labelPath = Path.Combine("bin/dataset/labels", labelFileName);

                        SaveMatAsImage(data.Mat, imagePath);

                        SaveLabels(labelPath, boundingBoxes);

                        imageCount++;
                    }
                }
                finally
                {
                    data.Mat?.Dispose();
                }
            });
        }
        private static async Task ProcessBackgroundImageAsync(BackgroundImageData data)
        {
            await Task.Run(() =>
            {
                try
                {
                    lock (lockObject)
                    {
                        string imageFileName = $"image_{imageCount:D6}.jpg";
                        string imagePath = Path.Combine("bin/dataset/images", imageFileName);

                        SaveMatAsImage(data.Mat, imagePath);

                        imageCount++;
                    }
                }
                finally
                {
                    data.Mat?.Dispose();
                }
            });
        }

        public static void StartLabeling()
        {
            if (cancellationTokenSource != null)
                return;
            LogManager.Log("Starting auto labeling...", LogLevel.Info);
            int existingImages = Directory.Exists("bin/dataset/images") ?
                Directory.GetFiles("bin/dataset/images", "image_*.jpg").Length : 0;
            imageCount = existingImages;
            Started = true;
            cancellationTokenSource = new CancellationTokenSource();
            backgroundTask = Task.Run(() => LabelingLoop(cancellationTokenSource.Token));
        }
        public static void StopLabeling()
        {
            if (cancellationTokenSource == null)
                return;

            LogManager.Log("Stopping auto labeling...", LogLevel.Info);
            Started = false;
            cancellationTokenSource.Cancel();
            backgroundTask?.Wait();
            cancellationTokenSource.Dispose();
            cancellationTokenSource = null;
            backgroundTask = null;
        }

        public class YoloBoundingBox
        {
            public int ClassId { get; set; }
            public double CenterX { get; set; }
            public double CenterY { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }


            public int PixelMinX { get; set; }
            public int PixelMinY { get; set; }
            public int PixelMaxX { get; set; }
            public int PixelMaxY { get; set; }
        }
        public class LabelingData
        {
            public Mat Mat { get; set; } = null!;
            public Rectangle Bounds { get; set; }
            public OpenCvSharp.Point[][] FilteredContours { get; set; } = null!;
        }
        public class BackgroundImageData
        {
            public Mat Mat { get; set; } = null!;
        }
    }
}