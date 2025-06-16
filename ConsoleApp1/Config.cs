using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace Spectrum
{
    public static class Config
    {
        #region Variables
        public static int ImageWidth { get; set; } = 640;
        public static int ImageHeight { get; set; } = 640;

        public static double YOffsetPercent { get; set; } = 0.8;
        public static double XOffsetPercent { get; set; } = 0.5;
        public static bool EnableAim { get; set; } = true;
        public static bool ClosestToMouse { get; set; } = true; // if false it will use the center of the screen
        public static int Keybind { get; set; } = 0x06; // first side button on mouse
        public static double Sensitivity { get; set; } = 0.5;

        public static bool ShowDetectionWindow { get; set; } = true;

        public static bool CollectData { get; set; } = false;
        public static bool AutoLabel { get; set; } = false; // collect data needs to be enabled 
        public static int BackgroundImageInterval { get; set; } = 10; // after 10 loops with no detection, save a background image

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
                    ImageWidth = config["ImageWidth"]?.Value<int>() ?? ImageWidth;
                    ImageHeight = config["ImageHeight"]?.Value<int>() ?? ImageHeight;

                    YOffsetPercent = config["YOffsetPercent"]?.Value<double>() ?? YOffsetPercent;
                    XOffsetPercent = config["XOffsetPercent"]?.Value<double>() ?? XOffsetPercent;
                    EnableAim = config["EnableAim"]?.Value<bool>() ?? EnableAim;
                    ClosestToMouse = config["ClosestToMouse"]?.Value<bool>() ?? ClosestToMouse;
                    Keybind = config["Keybind"]?.Value<int>() ?? Keybind;
                    Sensitivity = config["Sensitivity"]?.Value<double>() ?? Sensitivity;

                    ShowDetectionWindow = config["ShowDetectionWindow"]?.Value<bool>() ?? ShowDetectionWindow;

                    CollectData = config["CollectData"]?.Value<bool>() ?? CollectData;
                    AutoLabel = config["AutoLabel"]?.Value<bool>() ?? AutoLabel;
                    BackgroundImageInterval = config["BackgroundImageInterval"]?.Value<int>() ?? BackgroundImageInterval;

                    UpperHSV = config["UpperHSV"]?.ToObject<Scalar>() ?? UpperHSV;
                    LowerHSV = config["LowerHSV"]?.ToObject<Scalar>() ?? LowerHSV;

                    if (ImageWidth <= 0 || ImageHeight <= 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("[ERROR] Invalid image dimensions in config.json, using default values.");
                        Console.ResetColor();
                        ImageWidth = 640;
                        ImageHeight = 640;
                    }
                    if (YOffsetPercent < 0 || YOffsetPercent > 1 || XOffsetPercent < 0 || XOffsetPercent > 1)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("[ERROR] Invalid offset percentages in config.json, using default values.");
                        Console.ResetColor();
                        YOffsetPercent = 0.8;
                        XOffsetPercent = 0.5;
                    }
                    if (Keybind < 0 || Keybind > 255)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("[ERROR] Invalid keybind in config.json, using default value.");
                        Console.ResetColor();
                        Keybind = 0x06;
                    }
                    if (Sensitivity < 0 || Sensitivity > 1)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("[ERROR] Invalid sensitivity in config.json, using default value.");
                        Console.ResetColor();
                        Sensitivity = 0.5;
                    }
                    if (AutoLabel && !CollectData )
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("[WARNING] AutoLabel is enabled but CollectData is not. Disabling AutoLabel.");
                        Console.ResetColor();
                        AutoLabel = false;
                    }
                    if (BackgroundImageInterval <= 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("[ERROR] Invalid BackgroundImageInterval in config.json, using default value.");
                        Console.ResetColor();
                    }

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
                ["ImageWidth"] = ImageWidth,
                ["ImageHeight"] = ImageHeight,
                ["YOffsetPercent"] = YOffsetPercent,
                ["XOffsetPercent"] = XOffsetPercent,
                ["EnableAim"] = EnableAim,
                ["ClosestToMouse"] = ClosestToMouse,
                ["Keybind"] = Keybind,
                ["Sensitivity"] = Sensitivity,
                ["ShowDetectionWindow"] = ShowDetectionWindow,
                ["CollectData"] = CollectData,
                ["AutoLabel"] = AutoLabel,
                ["BackgroundImageInterval"] = BackgroundImageInterval,
                ["UpperHSV"] = JToken.FromObject(UpperHSV),
                ["LowerHSV"] = JToken.FromObject(LowerHSV)
            };
            File.WriteAllText("config.json", config.ToString(Formatting.Indented));
        }

    }
}
