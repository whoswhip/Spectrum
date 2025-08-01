﻿using Newtonsoft.Json;
using OpenCvSharp;
using System.Numerics;
using System.Security.Cryptography.X509Certificates;
using LogLevel = Spectrum.LogManager.LogLevel;

namespace Spectrum
{
    public class ConfigManager<T> where T : new()
    {
        private readonly string _fileName;
        private FileSystemWatcher? _watcher;
        private T _data;

        public T Data => _data;

        public ConfigManager(string fileName)
        {
            _fileName = fileName;
            _data = new T();
            LoadConfig();
        }

        public void LoadConfig(bool silent = false)
        {
            if (File.Exists(_fileName))
            {
                try
                {
                    var json = File.ReadAllText(_fileName);
                    _data = JsonConvert.DeserializeObject<T>(json) ?? new T();
                    if (!silent) LogManager.Log($"Loaded config {_fileName}.", LogLevel.Info);
                }
                catch
                {
                    LogManager.Log($"Failed to parse {_fileName}, using default values.", LogLevel.Error);
                    _data = new T();
                    SaveConfig();
                }
            }
            else
            {
                LogManager.Log($"{_fileName} not found, creating a new one with default values.", LogLevel.Error);
                _data = new T();
                SaveConfig();
            }
        }

        public void SaveConfig()
        {
            File.WriteAllText(_fileName, JsonConvert.SerializeObject(_data, Formatting.Indented));
        }

        public void StartFileWatcher()
        {
            if (_watcher != null) return;

            _watcher = new FileSystemWatcher(Directory.GetCurrentDirectory(), _fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
            };
            _watcher.Changed += OnConfigFileChanged;
            _watcher.EnableRaisingEvents = true;
            LogManager.Log($"File watcher started for {_fileName}.", LogLevel.Info);
        }

        public void StopFileWatcher()
        {
            if (_watcher == null) return;
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnConfigFileChanged;
            _watcher.Dispose();
            _watcher = null;
            LogManager.Log($"File watcher stopped for {_fileName}.", LogLevel.Info);
        }

        private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            Thread.Sleep(100);
            try
            {
                LoadConfig(true);
                LogManager.Log($"Configuration reloaded successfully for {_fileName}.", LogLevel.Info);
            }
            catch (Exception ex)
            {
                LogManager.Log($"Failed to reload configuration for {_fileName}: {ex.Message}", LogLevel.Error);
            }
        }
    }

    public enum MovementType
    {
        Linear,
        CubicBezier,
        QuadraticBezier,
        Adaptive
    }
    public class ConfigData
    {

        // Image settings
        public int ImageWidth { get; set; } = 640;
        public int ImageHeight { get; set; } = 640;

        // Offset settings
        public double YOffsetPercent { get; set; } = 0.8;
        public double XOffsetPercent { get; set; } = 0.5;

        // Aim settings
        public bool EnableAim { get; set; } = true;
        public bool ClosestToMouse { get; set; } = true;
        public Keys Keybind { get; set; } = Keys.XButton2;
        public double Sensitivity { get; set; } = 0.5;
        public MovementType AimMovementType { get; set; } = MovementType.Adaptive;
        public bool EmaSmoothening { get; set; } = true;
        public double EmaSmootheningFactor { get; set; } = 0.1;
        public bool TriggerBot { get; set; } = false;

        // Display settings
        public bool ShowDetectionWindow { get; set; } = true;
        public bool DrawDetections { get; set; } = true;
        public Vector4 DetectionColor { get; set; } = new Vector4(0.0f, 1.0f, 0.0f, 1.0f);
        public bool ShowMenu { get; set; } = true;
        public Keys MenuKey { get; set; } = Keys.Insert;
        public bool DrawFOV { get; set; } = false;
        public Vector4 FOVColor { get; set; } = new Vector4(0.0f, 1.0f, 0.0f, 1.0f);

        // Data collection settings
        public bool CollectData { get; set; } = false;
        public bool AutoLabel { get; set; } = false;
        public int BackgroundImageInterval { get; set; } = 10;

        // Color settings
        public Scalar UpperHSV { get; set; } = new Scalar(150, 255, 229);
        public Scalar LowerHSV { get; set; } = new Scalar(150, 255, 229);
        public string SelectedColor { get; set; } = "Arsenal [Magenta]";

        // Misc
        public bool DebugMode { get; set; } = false;
    }
    public class ColorData
    {
        public List<ColorInfo> Colors { get; set; } = new List<ColorInfo>();
    }

    public class ColorInfo
    {
        public string Name { get; set; }
        public Scalar Upper { get; set; }
        public Scalar Lower { get; set; }
        public ColorInfo(string name, Scalar upper, Scalar lower)
        {
            Name = name;
            Upper = upper;
            Lower = lower;
        }
    }
}