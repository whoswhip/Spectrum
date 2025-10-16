using Newtonsoft.Json;
using OpenCvSharp;
using System.Numerics;
using LogLevel = Spectrum.LogManager.LogLevel;

namespace Spectrum
{
    public class ConfigManager<T> where T : new()
    {
        private readonly string _defaultFilename;
        private readonly string _configDirectory;
        private FileSystemWatcher? _fileWatcher;
        private FileSystemWatcher? _directoryWatcher;
        private T _data;
        private string _currentConfigName;
        private List<string> _availableConfigs;

        public T Data => _data;
        public string CurrentConfigName => _currentConfigName;
        public IReadOnlyList<string> AvailableConfigs => _availableConfigs.AsReadOnly();

        public ConfigManager(string fileName, string configDirectory = "configs")
        {
            _defaultFilename = fileName;
            _configDirectory = configDirectory;
            _currentConfigName = Path.GetFileNameWithoutExtension(fileName);
            _data = new T();
            _availableConfigs = [];
            
            if (!Directory.Exists(_configDirectory))
            {
                Directory.CreateDirectory(_configDirectory);
            }
            
            UpdateAvailableConfigs();
            StartDirectoryWatcher();
            LoadConfig();
        }

        public void LoadConfig(string filename = "")
        {
            if (string.IsNullOrEmpty(filename))
                filename = _defaultFilename;
                
            try
            {
                if (File.Exists(filename))
                {
                    try
                    {
                        var json = File.ReadAllText(filename);
                        _data = JsonConvert.DeserializeObject<T>(json) ?? new T();
                        _currentConfigName = Path.GetFileNameWithoutExtension(filename);
                    }
                    catch
                    {
                        LogManager.Log($"Failed to parse {filename}, using default values.", LogLevel.Error);
                        _data = new T();
                        SaveConfig();
                    }
                }
                else
                {
                    LogManager.Log($"{filename} not found, creating a new one with default values.", LogLevel.Error);
                    _data = new T();
                    SaveConfig();
                }
            }
            catch (Exception ex)
            {
                LogManager.Log($"Error loading config {filename}: {ex.Message}", LogLevel.Error);
                _data = new T();
            }
        }

        public void SaveConfig(string filename = "")
        {
            if (string.IsNullOrEmpty(filename))
                filename = _defaultFilename;
                
            try
            {
                File.WriteAllText(filename, JsonConvert.SerializeObject(_data, Formatting.Indented));
                _currentConfigName = Path.GetFileNameWithoutExtension(filename);
            }
            catch (Exception ex)
            {
                LogManager.Log($"Error saving config {filename}: {ex.Message}", LogLevel.Error);
            }
        }

        public void SaveConfigToDirectory(string configName)
        {
            if (string.IsNullOrEmpty(configName))
            {
                LogManager.Log("Config name cannot be empty.", LogLevel.Error);
                return;
            }

            var filePath = Path.Combine(_configDirectory, $"{configName}.json");
            SaveConfig(filePath);
        }

        public bool LoadConfigFromDirectory(string configName)
        {
            if (string.IsNullOrEmpty(configName))
            {
                LogManager.Log("Config name cannot be empty.", LogLevel.Error);
                return false;
            }

            var filePath = Path.Combine(_configDirectory, $"{configName}.json");
            
            if (!File.Exists(filePath))
            {
                LogManager.Log($"Config {configName} not found in directory.", LogLevel.Error);
                return false;
            }

            LoadConfig(filePath);
            return true;
        }

        private void UpdateAvailableConfigs()
        {
            try
            {
                if (!Directory.Exists(_configDirectory))
                {
                    _availableConfigs.Clear();
                    return;
                }

                var configs = Directory.GetFiles(_configDirectory, "*.json")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Cast<string>()
                    .ToList();

                _availableConfigs.Clear();
                _availableConfigs.AddRange(configs);
            }
            catch (Exception ex)
            {
                LogManager.Log($"Error updating available configs: {ex.Message}", LogLevel.Error);
            }
        }

        private void StartDirectoryWatcher()
        {
            try
            {
                if (_directoryWatcher != null) return;

                _directoryWatcher = new FileSystemWatcher(_configDirectory, "*.json")
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
                };

                _directoryWatcher.Created += OnDirectoryChanged;
                _directoryWatcher.Deleted += OnDirectoryChanged;
                _directoryWatcher.Renamed += OnDirectoryChanged;
                _directoryWatcher.Changed += OnDirectoryChanged;
                _directoryWatcher.EnableRaisingEvents = true;

            }
            catch (Exception ex)
            {
                LogManager.Log($"Error starting directory watcher: {ex.Message}", LogLevel.Error);
            }
        }

        private void StopDirectoryWatcher()
        {
            if (_directoryWatcher == null) return;

            _directoryWatcher.EnableRaisingEvents = false;
            _directoryWatcher.Created -= OnDirectoryChanged;
            _directoryWatcher.Deleted -= OnDirectoryChanged;
            _directoryWatcher.Renamed -= OnDirectoryChanged;
            _directoryWatcher.Changed -= OnDirectoryChanged;
            _directoryWatcher.Dispose();
            _directoryWatcher = null;

        }

        private void OnDirectoryChanged(object sender, FileSystemEventArgs e)
        {
            Thread.Sleep(50);
            UpdateAvailableConfigs();
        }

        public bool DeleteConfigFromDirectory(string configName)
        {
            if (string.IsNullOrEmpty(configName))
            {
                LogManager.Log("Config name cannot be empty.", LogLevel.Error);
                return false;
            }

            var filePath = Path.Combine(_configDirectory, $"{configName}.json");
            
            if (!File.Exists(filePath))
            {
                LogManager.Log($"Config {configName} not found.", LogLevel.Error);
                return false;
            }

            try
            {
                File.Delete(filePath);
                return true;
            }
            catch (Exception ex)
            {
                LogManager.Log($"Error deleting config {configName}: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        public bool RenameConfigInDirectory(string oldName, string newName)
        {
            if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName))
            {
                LogManager.Log("Config names cannot be empty.", LogLevel.Error);
                return false;
            }

            var oldPath = Path.Combine(_configDirectory, $"{oldName}.json");
            var newPath = Path.Combine(_configDirectory, $"{newName}.json");

            if (!File.Exists(oldPath))
            {
                LogManager.Log($"Config {oldName} not found.", LogLevel.Error);
                return false;
            }

            if (File.Exists(newPath))
            {
                LogManager.Log($"Config {newName} already exists.", LogLevel.Error);
                return false;
            }

            try
            {
                File.Move(oldPath, newPath);
                if (_currentConfigName == oldName)
                    _currentConfigName = newName;
                return true;
            }
            catch (Exception ex)
            {
                LogManager.Log($"Error renaming config: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        public void StartFileWatcher()
        {
            if (_fileWatcher != null) return;

            _fileWatcher = new FileSystemWatcher(Directory.GetCurrentDirectory(), _defaultFilename)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
            };
            _fileWatcher.Changed += OnConfigFileChanged;
            _fileWatcher.EnableRaisingEvents = true;
        }

        public void StopFileWatcher()
        {
            if (_fileWatcher == null) return;
            _fileWatcher.EnableRaisingEvents = false;
            _fileWatcher.Changed -= OnConfigFileChanged;
            _fileWatcher.Dispose();
            _fileWatcher = null;
        }

        private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            Thread.Sleep(100);
            try
            {
                LoadConfig();
            }
            catch (Exception ex)
            {
                LogManager.Log($"Failed to reload configuration for {_defaultFilename}: {ex.Message}", LogLevel.Error);
            }
        }

        public void Dispose()
        {
            StopFileWatcher();
            StopDirectoryWatcher();
        }
    }

    #region Enums
    public enum MovementType
    {
        Linear,
        CubicBezier,
        QuadraticBezier,
        Adaptive,
        PerlinNoise
    }
    public enum MovementMethod
    {
        MouseEvent,
        Makcu,
        Arduino
    }
    public enum FovType
    {
        Circle,
        Rectangle
    }
    public enum CaptureMethod
    {
        GDI,
        DirectX
    }
    public enum KeybindType
    {
        Hold,
        Toggle,
        Always
    }
    #endregion
    public class ConfigData
    {

        #region Image Settings
        public int ImageWidth { get; set; } = 640;
        public int ImageHeight { get; set; } = 640;
        public int Threshold { get; set; } = 200;
        public CaptureMethod CaptureMethod { get; set; } = CaptureMethod.DirectX;
        #endregion

        #region Offset Settings
        public double YOffsetPercent { get; set; } = 0.8;
        public double XOffsetPercent { get; set; } = 0.5;
        public int YOffsetPixels { get; set; } = 100;
        public int XOffsetPixels { get; set; } = 100;
        public bool XPixelOffset { get; set; } = false;
        public bool YPixelOffset { get; set; } = false;
        #endregion

        #region Aim Settings
        public bool EnableAim { get; set; } = true;
        public bool ClosestToMouse { get; set; } = true;
        public Keybind Keybind = new(Keys.XButton2, KeybindType.Hold);
        public double Sensitivity { get; set; } = 0.5;
        public MovementType AimMovementType { get; set; } = MovementType.Adaptive;
        public bool EmaSmoothening { get; set; } = true;
        public double EmaSmootheningFactor { get; set; } = 0.1;

        public MovementMethod MovementMethod { get; set; } = MovementMethod.MouseEvent;
        #endregion

        #region Trigger Settings
        public bool TriggerBot { get; set; } = false;
        public Keybind TriggerKeybind = new(Keys.XButton1, KeybindType.Hold);
        public int TriggerDelay { get; set; } = 50; // milliseconds
        public int TriggerFov { get; set; } = 15; // pixels
        public int TriggerDuration { get; set; } = 100; // milliseconds
        public bool DrawTriggerFov { get; set; } = false;
        public Vector4 TriggerRadiusColor { get; set; } = new Vector4(1.0f, 0.0f, 0.0f, 1.0f);
        #endregion

        #region Display Settings
        public bool DrawDetections { get; set; } = true;
        public Vector4 DetectionColor { get; set; } = new Vector4(0.0f, 1.0f, 0.0f, 1.0f);
        public bool HighlightTarget { get; set; } = false;
        public Vector4 TargetColor { get; set; } = new Vector4(1.0f, 0.0f, 0.0f, 1.0f);
        public bool ShowMenu { get; set; } = true;
        public Keys MenuKey { get; set; } = Keys.Insert;
        public bool DrawFOV { get; set; } = false;
        public FovType FOVType { get; set; } = FovType.Rectangle;
        public Vector4 FOVColor { get; set; } = new Vector4(0.0f, 1.0f, 0.0f, 1.0f);
        public bool DrawAimPoint { get; set; } = true;
        public Vector4 AimPointColor { get; set; } = new Vector4(1.0f, 0.0f, 0.0f, 1.0f);
        #endregion

        #region Data Collection Settings
        public bool CollectData { get; set; } = false;
        public bool AutoLabel { get; set; } = false;
        public int BackgroundImageInterval { get; set; } = 10;
        #endregion

        #region Color Settings
        public Scalar UpperHSV { get; set; } = new Scalar(179, 255, 255);
        public Scalar LowerHSV { get; set; } = new Scalar(150, 255, 255);
        public string SelectedColor { get; set; } = "Arsenal [Magenta]";
        #endregion

        #region Misc
        public bool DebugMode { get; set; } = false;
        #endregion
    }
    public class ColorData
    {
        public List<ColorInfo> Colors { get; set; } = [];
    }

    public class ColorInfo(string name, Scalar upper, Scalar lower)
    {
        public string Name { get; set; } = name;
        public Scalar Upper { get; set; } = upper;
        public Scalar Lower { get; set; } = lower;
    }
    public class Keybind
    {
        public Keys Key { get; set; }

        public KeybindType Type { get; set; }
        public Keybind(Keys key, KeybindType type)
        {
            Key = key;
            Type = type;
        }
    }
}