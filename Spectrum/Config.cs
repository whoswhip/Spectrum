using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace Spectrum
{
    public static class Config
    {
        private static FileSystemWatcher? _configWatcher;
        #region Variables

        public enum MovementType
        {
            Linear,
            CubicBezier,
            QuadraticBezier,
            Adaptive
        }
        // Image settings
        public static int ImageWidth { get; set; } = 640;
        public static int ImageHeight { get; set; } = 640;

        // Offset settings
        public static double YOffsetPercent { get; set; } = 0.8;
        public static double XOffsetPercent { get; set; } = 0.5;

        // Aim settings
        public static bool EnableAim { get; set; } = true;
        public static bool ClosestToMouse { get; set; } = true; // if false it will use the center of the screen
        public static int Keybind { get; set; } = 0x06; // first side button on mouse
        public static double Sensitivity { get; set; } = 0.5;
        public static MovementType AimMovementType { get; set; } = MovementType.Adaptive;

        // Display settings
        public static bool ShowDetectionWindow { get; set; } = true;

        // Data collection settings
        public static bool CollectData { get; set; } = false;
        public static bool AutoLabel { get; set; } = false; // collect data needs to be enabled 
        public static int BackgroundImageInterval { get; set; } = 10; // after 10 loops with no detection, save a background image

        // Color settings
        public static Scalar UpperHSV { get; set; } = new Scalar(150, 255, 229);
        public static Scalar LowerHSV { get; set; } = new Scalar(150, 255, 229);
        #endregion

        public static void LoadConfig(bool silent = false)
        {
            if (File.Exists("config.json"))
            {
                string config_content = File.ReadAllText("config.json");
                try
                {
                    var config = JObject.Parse(config_content);
                    bool error = false;

                    var imageSettings = config["ImageSettings"];
                    ImageWidth = imageSettings?["ImageWidth"]?.Value<int>() ?? config["ImageWidth"]?.Value<int>() ?? ImageWidth;
                    ImageHeight = imageSettings?["ImageHeight"]?.Value<int>() ?? config["ImageHeight"]?.Value<int>() ?? ImageHeight;

                    var offsetSettings = config["OffsetSettings"];
                    YOffsetPercent = offsetSettings?["YOffsetPercent"]?.Value<double>() ?? config["YOffsetPercent"]?.Value<double>() ?? YOffsetPercent;
                    XOffsetPercent = offsetSettings?["XOffsetPercent"]?.Value<double>() ?? config["XOffsetPercent"]?.Value<double>() ?? XOffsetPercent;

                    var aimSettings = config["AimSettings"];
                    EnableAim = aimSettings?["EnableAim"]?.Value<bool>() ?? config["EnableAim"]?.Value<bool>() ?? EnableAim;
                    ClosestToMouse = aimSettings?["ClosestToMouse"]?.Value<bool>() ?? config["ClosestToMouse"]?.Value<bool>() ?? ClosestToMouse;
                    Keybind = aimSettings?["Keybind"]?.Value<int>() ?? config["Keybind"]?.Value<int>() ?? Keybind;
                    Sensitivity = aimSettings?["Sensitivity"]?.Value<double>() ?? config["Sensitivity"]?.Value<double>() ?? Sensitivity;
                    AimMovementType = aimSettings?["AimMovementType"]?.Value<string>() switch
                    {
                        "Linear" => MovementType.Linear,
                        "CubicBezier" => MovementType.CubicBezier,
                        "Adaptive" => MovementType.Adaptive,
                        _ => AimMovementType
                    };

                    var displaySettings = config["DisplaySettings"];
                    ShowDetectionWindow = displaySettings?["ShowDetectionWindow"]?.Value<bool>() ?? config["ShowDetectionWindow"]?.Value<bool>() ?? ShowDetectionWindow;

                    var dataSettings = config["DataCollectionSettings"];
                    CollectData = dataSettings?["CollectData"]?.Value<bool>() ?? config["CollectData"]?.Value<bool>() ?? CollectData;
                    AutoLabel = dataSettings?["AutoLabel"]?.Value<bool>() ?? config["AutoLabel"]?.Value<bool>() ?? AutoLabel;
                    BackgroundImageInterval = dataSettings?["BackgroundImageInterval"]?.Value<int>() ?? config["BackgroundImageInterval"]?.Value<int>() ?? BackgroundImageInterval;

                    var colorSettings = config["ColorSettings"];
                    UpperHSV = colorSettings?["UpperHSV"]?.ToObject<Scalar>() ?? config["UpperHSV"]?.ToObject<Scalar>() ?? UpperHSV;
                    LowerHSV = colorSettings?["LowerHSV"]?.ToObject<Scalar>() ?? config["LowerHSV"]?.ToObject<Scalar>() ?? LowerHSV;

                    if (ImageWidth <= 0 || ImageHeight <= 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("[ERROR] Invalid image dimensions in config.json, using default values.");
                        Console.ResetColor();
                        ImageWidth = 640;
                        ImageHeight = 640;
                        error = true;
                    }
                    if (YOffsetPercent < 0 || YOffsetPercent > 1 || XOffsetPercent < 0 || XOffsetPercent > 1)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("[ERROR] Invalid offset percentages in config.json, using default values.");
                        Console.ResetColor();
                        YOffsetPercent = 0.8;
                        XOffsetPercent = 0.5;
                        error = true;
                    }
                    if (Keybind < 0 || Keybind > 255)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("[ERROR] Invalid keybind in config.json, using default value.");
                        Console.ResetColor();
                        Keybind = 0x06;
                        error = true;
                    }
                    if (Sensitivity < 0 || Sensitivity > 2)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("[ERROR] Invalid sensitivity in config.json, using default value.");
                        Console.ResetColor();
                        Sensitivity = 0.5;
                        error = true;
                    }
                    if (AutoLabel && !CollectData)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("[WARNING] AutoLabel is enabled but CollectData is not. Disabling AutoLabel.");
                        Console.ResetColor();
                        AutoLabel = false;
                        error = true;
                    }
                    if (BackgroundImageInterval <= 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("[ERROR] Invalid BackgroundImageInterval in config.json, using default value.");
                        Console.ResetColor();
                        error = true;
                    }
                    if (error)
                        SaveConfig();

                    if (!silent)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("[INFO] Configuration loaded successfully.");
                        Console.ResetColor();
                    }
                }
                catch
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[ERROR] Error parsing config.json, using default values.");
                    Console.ResetColor();
                    SaveConfig();
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[WARNING] config.json not found, using default values.");
                Console.ResetColor();
                SaveConfig();
            }
        }
        public static void SaveConfig()
        {
            var config = new JObject
            {
                ["ImageSettings"] = new JObject
                {
                    ["ImageWidth"] = ImageWidth,
                    ["ImageHeight"] = ImageHeight
                },
                ["OffsetSettings"] = new JObject
                {
                    ["YOffsetPercent"] = YOffsetPercent,
                    ["XOffsetPercent"] = XOffsetPercent
                },
                ["AimSettings"] = new JObject
                {
                    ["EnableAim"] = EnableAim,
                    ["ClosestToMouse"] = ClosestToMouse,
                    ["Keybind"] = Keybind,
                    ["Sensitivity"] = Sensitivity,
                    ["AimMovementType"] = AimMovementType.ToString()
                },
                ["DisplaySettings"] = new JObject
                {
                    ["ShowDetectionWindow"] = ShowDetectionWindow
                },
                ["DataCollectionSettings"] = new JObject
                {
                    ["CollectData"] = CollectData,
                    ["AutoLabel"] = AutoLabel,
                    ["BackgroundImageInterval"] = BackgroundImageInterval
                },
                ["ColorSettings"] = new JObject
                {
                    ["UpperHSV"] = JToken.FromObject(UpperHSV),
                    ["LowerHSV"] = JToken.FromObject(LowerHSV)
                }
            }; File.WriteAllText("config.json", config.ToString(Formatting.Indented));
        }

        public static void StartFileWatcher()
        {
            if (_configWatcher != null)
                return;

            _configWatcher = new FileSystemWatcher(Directory.GetCurrentDirectory(), "config.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
            };

            _configWatcher.Changed += OnConfigFileChanged;
            _configWatcher.EnableRaisingEvents = true;

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("[INFO] File watcher started for config.json");
            Console.ResetColor();
        }

        public static void StopFileWatcher()
        {
            if (_configWatcher != null)
            {
                _configWatcher.EnableRaisingEvents = false;
                _configWatcher.Changed -= OnConfigFileChanged;
                _configWatcher.Dispose();
                _configWatcher = null;

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("[INFO] File watcher stopped for config.json");
                Console.ResetColor();
            }
        }

        private static void OnConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            Thread.Sleep(100);

            try
            {
                LoadConfig(true);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[INFO] Configuration automatically reloaded due to file change.");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] Failed to reload configuration: {ex.Message}");
                Console.ResetColor();
            }
        }

    }
}
