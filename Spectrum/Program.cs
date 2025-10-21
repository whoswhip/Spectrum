using OpenCvSharp;
using Spectrum.Detection;
using System.Runtime.InteropServices;

namespace Spectrum
{
    class Program
    {
        static Renderer? renderer = null;
        public static ConfigManager<ConfigData> mainConfig = new ConfigManager<ConfigData>("bin\\configs\\config.json");
        public static ConfigManager<ColorData> colorConfig = new ConfigManager<ColorData>("bin\\configs\\colors.json");
        private static DetectionManager? detectionManager;
        public static (int fps, double avgProcessTime) statistics = (0, 0);
        public static CaptureManager SharedCaptureManager { get; } = new CaptureManager();


        static void Main()
        {
            Thread renderThread = new Thread(() =>
            {
                try
                {
                    renderer = new Renderer(SharedCaptureManager);
                    bool dxok = SharedCaptureManager.TryInitializeDirectX();
                    if (!dxok)
                    {
                        LogManager.Log("Failed to initialize DirectX. Falling back to gdi.", LogManager.LogLevel.Warning);
                        mainConfig.Data.CaptureMethod = CaptureMethod.GDI;
                    }
                    renderer.Run();
                }
                catch (Exception ex)
                {
                    LogManager.Log($"Failed to start renderer: {ex.Message}", LogManager.LogLevel.Error);
                }
            });
            renderThread.Start();

            string[] dirs = ["bin", "bin/dataset", "bin/dataset/images", "bin/dataset/labels", "bin/logs", "bin/configs"];
            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }

            mainConfig.LoadConfig();
            mainConfig.StartFileWatcher();

            if (mainConfig.Data.AutoLabel)
            {
                AutoLabeling.StartLabeling();
            }

            if (colorConfig.Data.Colors.Count == 0)
            {
                colorConfig.Data.Colors.Add(new ColorInfo("Arsenal [Magenta]", new Scalar(179, 255, 255), new Scalar(150, 255, 255)));
                colorConfig.Data.Colors.Add(new ColorInfo("Combat Surf [Red]", new Scalar(0, 255, 255), new Scalar(0, 255, 255)));
                colorConfig.SaveConfig();
                colorConfig.LoadConfig();
                mainConfig.LoadConfig();
            }

            Thread.Sleep(1000);

            if (renderer == null)
            {
                LogManager.Log("Renderer failed to initialize. Exiting application.", LogManager.LogLevel.Error);
                return;
            }

            detectionManager = new DetectionManager(renderer);
        }
    }
}