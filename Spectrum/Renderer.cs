using ClickableTransparentOverlay;
using ImGuiNET;
using OpenCvSharp;
using Spectrum.Detection;
using Spectrum.Input;
using Spectrum.Input.InputLibraries.Arduino;
using Spectrum.Input.InputLibraries.Makcu;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;

namespace Spectrum
{
    partial class Renderer : Overlay
    {
        static (int width, int height) screenSize = SystemHelper.GetPrimaryScreenSize();
        private List<Action<ImDrawListPtr>> detectionDrawCommands = new List<Action<ImDrawListPtr>>();
        private List<Action<ImDrawListPtr>> activeDrawCommands = new List<Action<ImDrawListPtr>>();
        private readonly object detectionDrawLock = new object();
        private readonly CaptureManager _captureManager;

        private bool _vsync = true;
        private int _fpsLimit = 240;
        private readonly Dictionary<string, bool> _waitingForKeybind = new();
        private readonly Dictionary<string, Keys> _pendingKeybind = new();
        private readonly Dictionary<string, Task?> _keybindTask = new();
        private readonly ConcurrentQueue<Keys> _keybindResults = new();
        private ConfigManager<ConfigData> mainConfig = Program.mainConfig;
        private ConfigManager<ColorData> colorConfig = Program.colorConfig;
        private string ColorName = "New Color";
        private static bool scrollToBottom = true;
        private ColorInfo? selectedColor = null;
        private string configName = "New Config";
        private string? selectedConfigName = null;

        public Renderer(CaptureManager captureManager) : base("Spectrum", screenSize.width, screenSize.width)
        {
            _captureManager = captureManager;
            VSync = _vsync;
            FPSLimit = _fpsLimit;
            ImGui.CreateContext();
            var io = ImGui.GetIO();
            io.Fonts.Clear();
            io.Fonts.AddFontFromFileTTF("C:\\Windows\\Fonts\\Arial.ttf", 16.0f);
        }
        override protected void Render()
        {
            var config = mainConfig.Data;
            if (config.DrawDetections || config.DrawFOV || config.DebugMode || config.DrawAimPoint)
                RenderOverlay();
            if (!config.ShowMenu)
                return;

            AddStyling();

            ImGui.SetNextWindowSize(new Vector2(700, 500), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowPos(new Vector2(screenSize.width / 2 - 350, screenSize.height / 2 - 250), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSizeConstraints(new Vector2(700, 500), new Vector2(float.MaxValue, float.MaxValue));

            ImGui.Begin("Spectrum", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar);
            ImGui.Text("Spectrum");
            ImGui.SameLine();
            float currentWidth = ImGui.GetWindowWidth();
            ImGui.SetCursorPosX(currentWidth - 30);
            if (ImGui.Button("X", new Vector2(22, 22)))
            {
                Close();
                Environment.Exit(0);
            }

            ImGui.BeginTabBar("##tabs");
            if (ImGui.BeginTabItem("Aiming"))
            {
                ImGuiExtensions.BeginPaneGroup("Aiming Panes", 2, 12f, new Vector2(12, 10), ImGuiChildFlags.AlwaysUseWindowPadding, ImGuiWindowFlags.None, 0f);

                if (ImGuiExtensions.BeginPane("Aim Assist"))
                {
                    bool enableAiming = config.EnableAim;
                    if (ImGui.Checkbox("##Enable Aiming", ref enableAiming))
                        config.EnableAim = enableAiming;

                    ImGui.SameLine();
                    ImGui.TextUnformatted("Enable Aiming");
                    ImGuiExtensions.KeybindInput("Aim Keybind", ref config.Keybind, true);

                    bool closestToMouse = config.ClosestToMouse;
                    if (ImGui.Checkbox(" Closest to Mouse", ref closestToMouse))
                        config.ClosestToMouse = closestToMouse;

                    float sensitivity = (float)config.Sensitivity;
                    if (ImGuiExtensions.SliderFill("Sensitivity", ref sensitivity, 0.1f, 2.0f, "%.1f", false))
                        config.Sensitivity = Math.Clamp(sensitivity, 0.1, float.MaxValue);

                    float EmaSmootheningFactor = (float)config.EmaSmootheningFactor;
                    bool EmaSmoothening = config.EmaSmoothening;
                    if (ImGui.Checkbox("Ema Smoothening", ref EmaSmoothening))
                        config.EmaSmoothening = EmaSmoothening;

                    if (EmaSmoothening)
                    {
                        float smoothingPercentage = (float)((1.0 - config.EmaSmootheningFactor) * 100.0);
                        if (ImGuiExtensions.SliderFill("Ema Smoothening (%)", ref smoothingPercentage, 0f, 99f, "%.0f%%"))
                        {
                            config.EmaSmootheningFactor = 1.0 - (smoothingPercentage / 100.0);
                        }
                    }

                    ImGui.TextUnformatted("Movement Path");
                    ImGui.SetNextItemWidth(-1);
                    MovementType AimPath = config.AimMovementType;
                    if (ImGui.BeginCombo("##Movement Type", AimPath.ToString()))
                    {
                        foreach (MovementType type in Enum.GetValues(typeof(MovementType)))
                        {
                            bool isSelected = (type == AimPath);
                            string displayName = string.Concat(type.ToString().Select((x, i) => i > 0 && char.IsUpper(x) ? " " + x : x.ToString())); // just adds a space before capitals except for the first
                            if (ImGui.Selectable(displayName, isSelected))
                                config.AimMovementType = type;
                            if (isSelected)
                                ImGui.SetItemDefaultFocus();
                        }
                        ImGui.EndCombo();
                    }

                    if (AimPath == MovementType.WindMouse)
                    {
                        float windMouseGravity = (float)config.WindMouseGravity;
                        if (ImGuiExtensions.SliderFill("Gravity", ref windMouseGravity, 1.0f, 20.0f, "%.1f"))
                            config.WindMouseGravity = windMouseGravity;

                        float windMouseWind = (float)config.WindMouseWind;
                        if (ImGuiExtensions.SliderFill("Wind", ref windMouseWind, 0.5f, 10.0f, "%.1f"))
                            config.WindMouseWind = windMouseWind;

                        float windMouseMaxStep = (float)config.WindMouseMaxStep;
                        if (ImGuiExtensions.SliderFill("Max Step", ref windMouseMaxStep, 1.0f, 50.0f, "%.1f"))
                            config.WindMouseMaxStep = windMouseMaxStep;

                        float windMouseTargetArea = (float)config.WindMouseTargetArea;
                        if (ImGuiExtensions.SliderFill("Target Area", ref windMouseTargetArea, 1.0f, 20.0f, "%.1f"))
                            config.WindMouseTargetArea = windMouseTargetArea;

                        bool preventOvershoot = config.WindMousePreventOvershoot;
                        if (ImGui.Checkbox("Prevent Overshoot", ref preventOvershoot))
                            config.WindMousePreventOvershoot = preventOvershoot;
                    }

                    bool YPixelOffset = config.YPixelOffset;
                    if (ImGui.Checkbox("Pixel Based Offset (Y)", ref YPixelOffset))
                        config.YPixelOffset = YPixelOffset;
                    bool XPixelOffset = config.XPixelOffset;
                    if (ImGui.Checkbox("Pixel Based Offset (X)", ref XPixelOffset))
                        config.XPixelOffset = XPixelOffset;

                    if (!YPixelOffset)
                    {
                        float YOffset = (int)(config.YOffsetPercent * 100);
                        if (ImGuiExtensions.SliderFill("Y Offset (%)", ref YOffset, 0, 100, "%d%%"))
                        {
                            config.YOffsetPercent = ((double)YOffset / 100);
                        }
                    }
                    else
                    {
                        int YOffset = config.YOffsetPixels;
                        if (ImGuiExtensions.SliderFill("Y Offset (px)", ref YOffset, -200, 200))
                            config.YOffsetPixels = Math.Max(-200, YOffset);
                    }

                    if (!XPixelOffset)
                    {
                        float XOffset = (int)(config.XOffsetPercent * 100);
                        if (ImGuiExtensions.SliderFill("X Offset (%)", ref XOffset, 0, 100, "%d%%"))
                        {
                            config.XOffsetPercent = ((double)XOffset / 100);
                        }
                    }
                    else
                    {
                        int XOffset = config.XOffsetPixels;
                        if (ImGuiExtensions.SliderFill("X Offset (px)", ref XOffset, -200, 200))
                            config.XOffsetPixels = Math.Max(-200, XOffset);
                    }

                    ImGui.SeparatorText("Overlay Settings");

                    bool DrawFOV = config.DrawFOV;
                    if (ImGui.Checkbox("##Draw FOV", ref DrawFOV))
                        config.DrawFOV = DrawFOV;

                    ImGui.SameLine();
                    ImGui.Text("Draw FOV");

                    ImGui.SameLine(ImGui.GetContentRegionAvail().X - 12);
                    Vector4 FOVColor = config.FOVColor;
                    if (ImGui.ColorEdit4("FOV Color", ref FOVColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
                        config.FOVColor = FOVColor;

                    bool DrawDetections = config.DrawDetections;
                    if (ImGui.Checkbox("##Draw Detections", ref DrawDetections))
                        config.DrawDetections = DrawDetections;
                    ImGui.SameLine();
                    ImGui.Text("Draw Detections");

                    ImGui.SameLine(ImGui.GetContentRegionAvail().X - 12);
                    Vector4 DetectionColor = config.DetectionColor;
                    if (ImGui.ColorEdit4("Draw Color", ref DetectionColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
                        config.DetectionColor = DetectionColor;
                    if (DrawDetections)
                    {
                        bool HighlightTarget = config.HighlightTarget;
                        if (ImGui.Checkbox("Highlight Target", ref HighlightTarget))
                            config.HighlightTarget = HighlightTarget;
                        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 12);
                        Vector4 HighlightColor = config.TargetColor;
                        if (ImGui.ColorEdit4("Highlight Color", ref HighlightColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
                            config.TargetColor = HighlightColor;
                    }

                    bool DrawAimPoint = config.DrawAimPoint;
                    if (ImGui.Checkbox("##Draw Aim Point", ref DrawAimPoint))
                        config.DrawAimPoint = DrawAimPoint;
                    ImGui.SameLine();
                    ImGui.Text("Draw Aim Point");
                    ImGui.SameLine(ImGui.GetContentRegionAvail().X - 12);
                    Vector4 AimPointColor = config.AimPointColor;
                    if (ImGui.ColorEdit4("Aim Point Color", ref AimPointColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
                        config.AimPointColor = AimPointColor;

                    if (DrawFOV)
                    {
                        ImGui.TextUnformatted("FOV Type");
                        ImGui.SetNextItemWidth(-1);
                        FovType fovType = config.FOVType;
                        if (ImGui.BeginCombo("##FOV Type", fovType.ToString()))
                        {
                            foreach (FovType type in Enum.GetValues(typeof(FovType)))
                            {
                                bool isSelected = (type == fovType);
                                if (ImGui.Selectable(type.ToString(), isSelected))
                                    config.FOVType = type;
                                if (isSelected)
                                    ImGui.SetItemDefaultFocus();
                            }
                            ImGui.EndCombo();
                        }
                    }
                    ImGuiExtensions.EndPane();
                }

                if (ImGuiExtensions.BeginPane("Triggerbot"))
                {
                    bool triggerBot = config.TriggerBot;
                    if (ImGui.Checkbox(" Enabled", ref triggerBot))
                        config.TriggerBot = triggerBot;
                    ImGuiExtensions.KeybindInput("Trigger Keybind", ref config.TriggerKeybind, true);

                    if (triggerBot)
                    {
                        int triggerDelay = config.TriggerDelay;
                        if (ImGuiExtensions.SliderFill("Trigger Delay (ms)", ref triggerDelay, 1, 1000))
                            config.TriggerDelay = triggerDelay;

                        int triggerFov = config.TriggerFov;
                        if (ImGuiExtensions.SliderFill("Trigger FOV (px)", ref triggerFov, 1, 100))
                            config.TriggerFov = triggerFov;

                        int triggerDuration = config.TriggerDuration;
                        if (ImGuiExtensions.SliderFill("Trigger Duration (ms)", ref triggerDuration, 1, 1000))
                            config.TriggerDuration = triggerDuration;

                        bool DrawTriggerFov = config.DrawTriggerFov;
                        if (ImGui.Checkbox("##Draw Trigger FOV", ref DrawTriggerFov))
                            config.DrawTriggerFov = DrawTriggerFov;
                        ImGui.SameLine();
                        ImGui.TextUnformatted("Draw Trigger FOV");
                        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 12);
                        Vector4 RadiusColor = config.TriggerRadiusColor;
                        if (ImGui.ColorEdit4("Trigger Color", ref RadiusColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
                            config.TriggerRadiusColor = RadiusColor;

                    }
                    ImGuiExtensions.EndPane();
                }

                ImGuiExtensions.EndPaneGroup();

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Detection"))
            {
                ImGuiExtensions.BeginPaneGroup("Detection Panes", 2, 12f, new Vector2(12, 10), ImGuiChildFlags.AlwaysUseWindowPadding, ImGuiWindowFlags.None, 0f);

                if (ImGuiExtensions.BeginPane("Detection Settings"))
                {
                    ImGui.TextUnformatted("Image Width");
                    ImGui.SetNextItemWidth(-1);
                    int ImageWidth = config.ImageWidth;
                    if (ImGui.InputInt("##Image Width", ref ImageWidth, 1, screenSize.width))
                    {
                        if (ImageWidth < 1)
                            ImageWidth = 1;
                        else if (ImageWidth > screenSize.width)
                            ImageWidth = screenSize.width;
                        config.ImageWidth = ImageWidth;
                    }

                    ImGui.TextUnformatted("Image Height");
                    ImGui.SetNextItemWidth(-1);
                    int ImageHeight = config.ImageHeight;
                    if (ImGui.InputInt("##Image Height", ref ImageHeight, 1, screenSize.height))
                    {
                        if (ImageHeight < 1)
                            ImageHeight = 1;
                        else if (ImageHeight > screenSize.height)
                            ImageHeight = screenSize.height;
                        config.ImageHeight = ImageHeight;
                    }

                    ImGui.SetNextItemWidth(-1);
                    Scalar upperHSV = config.UpperHSV;
                    if (ImGuiExtensions.ColorEditHSV3("Upper HSV", ref upperHSV))
                        config.UpperHSV = upperHSV;
                    Scalar lowerHSV = config.LowerHSV;
                    if (ImGuiExtensions.ColorEditHSV3("Lower HSV", ref lowerHSV))
                        config.LowerHSV = lowerHSV;

                    int Threshold = config.Threshold;
                    if (ImGuiExtensions.SliderFill("Threshold", ref Threshold, 1, 255))
                        config.Threshold = Threshold;

                    if (ImGui.Button("Open HSV Range Selector"))
                        _showHSVRangeWindow = true;

                    ImGuiExtensions.EndPane();
                }

                if (ImGuiExtensions.BeginPane("Color Configs"))
                {
                    List<ColorInfo> colors = colorConfig.Data.Colors;
                    ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.110f, 0.110f, 0.125f, 1.000f));
                    ImGui.BeginChild("##DColorConfigsChild", new Vector2(0, ImGui.GetContentRegionAvail().Y / 2), ImGuiChildFlags.None);
                    ImGui.Dummy(new Vector2(0, 4));
                    foreach (var color in colors)
                    {
                        if (ImGui.Selectable("  " + color.Name, selectedColor == color))
                        {
                            selectedColor = color;
                            ColorName = color.Name;
                            config.SelectedColor = selectedColor.Name;
                        }
                    }
                    ImGui.EndChild();
                    ImGui.PopStyleColor();

                    ImGui.SetNextItemWidth(-1);
                    ImGui.InputTextWithHint("##ColorName", "Color Name", ref ColorName, 100);

                    Vector2 size = new Vector2((ImGui.GetContentRegionAvail().X / 3) - 5.7f, 22);
                    if (ImGui.Button("Load Color", size))
                    {
                        if (selectedColor != null)
                        {
                            config.SelectedColor = selectedColor.Name;
                            config.UpperHSV = selectedColor.Upper;
                            config.LowerHSV = selectedColor.Lower;
                            ColorName = selectedColor.Name;
                        }
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("Save Color", size))
                    {
                        if (colors.Find(c => c.Name.Equals(ColorName, StringComparison.OrdinalIgnoreCase)) is ColorInfo existingColor)
                        {
                            existingColor.Upper = config.UpperHSV;
                            existingColor.Lower = config.LowerHSV;
                        }
                        else
                        {
                            colors.Add(new ColorInfo(ColorName, config.UpperHSV, config.LowerHSV));
                            colorConfig.SaveConfig();
                            selectedColor = colors.Find(c => c.Name.Equals(ColorName, StringComparison.OrdinalIgnoreCase));
                        }
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Delete Color", size))
                    {
                        if (selectedColor != null)
                        {
                            int currentIndex = colors.IndexOf(selectedColor);
                            colors.RemoveAll(c => c.Name.Equals(selectedColor.Name, StringComparison.OrdinalIgnoreCase));
                            colorConfig.SaveConfig();
                            if (colors.Count > 0)
                            {
                                if (selectedColor.Name == config.SelectedColor)
                                {
                                    int nextIndex = Math.Max(0, currentIndex - 1);
                                    selectedColor = colors[currentIndex - 1];
                                    config.SelectedColor = selectedColor.Name;
                                    config.UpperHSV = selectedColor.Upper;
                                    config.LowerHSV = selectedColor.Lower;
                                    ColorName = selectedColor.Name;
                                }
                            }
                            else
                            {
                                selectedColor = null;
                            }
                        }
                    }
                    ImGuiExtensions.EndPane();
                }


                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Configs"))
            {
                ImGuiExtensions.BeginPaneGroup("Config Panes", 2, 12f, new Vector2(12, 10), ImGuiChildFlags.AlwaysUseWindowPadding, ImGuiWindowFlags.None, 0f);

                if (ImGuiExtensions.BeginPane("Config Management"))
                {
                    ImGui.TextUnformatted("Available Configs");
                    ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.110f, 0.110f, 0.125f, 1.000f));
                    ImGui.BeginChild("##ConfigListChild", new Vector2(0, ImGui.GetContentRegionAvail().Y / 2), ImGuiChildFlags.None);
                    ImGui.Dummy(new Vector2(0, 4));

                    var availableConfigs = mainConfig.AvailableConfigs;
                    foreach (var _config in availableConfigs)
                    {
                        if (ImGui.Selectable("  " + _config, selectedConfigName == _config))
                        {
                            selectedConfigName = _config;
                            configName = _config;
                        }
                    }

                    ImGui.EndChild();
                    ImGui.PopStyleColor();

                    ImGui.SetNextItemWidth(-1);
                    ImGui.InputTextWithHint("##ConfigName", "Config Name", ref configName, 100);

                    Vector2 size = new Vector2((ImGui.GetContentRegionAvail().X / 3) - 5.7f, 22);

                    if (ImGui.Button("Load Config", size))
                    {
                        if (!string.IsNullOrEmpty(selectedConfigName))
                        {
                            bool success = mainConfig.LoadConfigFromDirectory(selectedConfigName);
                            if (success)
                                configName = selectedConfigName;
                        }
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("Save Config", size))
                    {
                        if (!string.IsNullOrEmpty(configName))
                        {
                            mainConfig.SaveConfigToDirectory(configName);
                            selectedConfigName = configName;
                        }
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("Delete Config", size))
                    {
                        if (!string.IsNullOrEmpty(selectedConfigName))
                        {
                            bool success = mainConfig.DeleteConfigFromDirectory(selectedConfigName);
                            if (success)
                            {
                                selectedConfigName = null;
                                configName = "New Config";
                            }
                        }
                    }

                    ImGuiExtensions.EndPane();
                }

                if (ImGuiExtensions.BeginPane("Current Config"))
                {
                    ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.110f, 0.110f, 0.125f, 1.000f));
                    ImGui.SetNextItemWidth(-1);
                    string currentConfig = mainConfig.CurrentConfigName;
                    ImGui.InputText("##CurrentConfig", ref currentConfig, 256, ImGuiInputTextFlags.ReadOnly);
                    ImGui.PopStyleColor();

                    Vector2 fullSize = new Vector2(ImGui.GetContentRegionAvail().X, 22);

                    if (ImGui.Button("Save Current to Default", fullSize))
                        mainConfig.SaveConfig();

                    if (ImGui.Button("Save Current Config", fullSize))
                        if (!string.IsNullOrEmpty(configName))
                            mainConfig.SaveConfigToDirectory(configName);

                    if (ImGui.Button("Open Configs Folder", fullSize))
                    {
                        string configDir = Path.Combine(Directory.GetCurrentDirectory(), "configs");
                        if (Directory.Exists(configDir))
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "explorer.exe",
                                Arguments = configDir,
                                UseShellExecute = true
                            });
                        }
                        else
                        {
                            LogManager.Log($"Directory doesn't exist: {configDir}", LogManager.LogLevel.Error);
                        }
                    }

                    ImGuiExtensions.EndPane();
                }

                ImGui.EndTabItem();
            }


            if (ImGui.BeginTabItem("Settings"))
            {
                ImGuiExtensions.BeginPaneGroup("Settings Panes", 2, 12f, new Vector2(12, 10), ImGuiChildFlags.AlwaysUseWindowPadding, ImGuiWindowFlags.None, 0f);

                if (ImGuiExtensions.BeginPane("Settings"))
                {
                    ImGui.TextUnformatted("Movement Method");
                    ImGui.SetNextItemWidth(-1);
                    MovementMethod movementMethod = config.MovementMethod;
                    if (ImGui.BeginCombo("##Movement Method", movementMethod.ToString()))
                    {
                        foreach (MovementMethod method in Enum.GetValues(typeof(MovementMethod)))
                        {
                            bool isSelected = (method == movementMethod);
                            if (ImGui.Selectable(method.ToString(), isSelected))
                            {
                                config.MovementMethod = method;
                                switch (method)
                                {
                                    case MovementMethod.Makcu:
                                        {
                                            ArduinoMain.Close();
                                            var ok = MakcuMain.Load().GetAwaiter().GetResult();
                                            if (!ok)
                                            {
                                                LogManager.Log("Makcu failed to initialize. Falling back to MouseEvent.", LogManager.LogLevel.Warning);
                                                config.MovementMethod = MovementMethod.MouseEvent;
                                                MakcuMain.Unload();
                                            }
                                            break;
                                        }
                                    case MovementMethod.Arduino:
                                        {
                                            MakcuMain.Unload();
                                            var ok = ArduinoMain.Load();
                                            if (!ok)
                                            {
                                                LogManager.Log("Arduino failed to initialize. Falling back to MouseEvent.", LogManager.LogLevel.Warning);
                                                config.MovementMethod = MovementMethod.MouseEvent;
                                                ArduinoMain.Close();
                                            }
                                            break;
                                        }
                                    default:
                                        ArduinoMain.Close();
                                        MakcuMain.Unload();
                                        break;
                                }
                            }
                            if (isSelected)
                                ImGui.SetItemDefaultFocus();
                        }
                        ImGui.EndCombo();
                    }

                    ImGui.TextUnformatted("Capture Method");
                    ImGui.SetNextItemWidth(-1);
                    CaptureMethod captureMethod = config.CaptureMethod;
                    if (ImGui.BeginCombo("##Capture Method", captureMethod.ToString()))
                    {
                        foreach (CaptureMethod method in Enum.GetValues(typeof(CaptureMethod)))
                        {
                            bool isSelected = (method == captureMethod);
                            if (ImGui.Selectable(method.ToString(), isSelected))
                            {
                                CaptureMethod _method = method;
                                if (method == CaptureMethod.DirectX)
                                {
                                    bool dxOk = _captureManager.TryInitializeDirectX();
                                    if (!dxOk)
                                    {
                                        LogManager.Log("DirectX capture failed to initialize. Falling back to GDI.", LogManager.LogLevel.Warning);
                                        _method = CaptureMethod.GDI;
                                    }
                                }
                                config.CaptureMethod = _method;
                            }
                            if (isSelected)
                                ImGui.SetItemDefaultFocus();
                        }
                        ImGui.EndCombo();
                    }

                    bool CollectData = config.CollectData;
                    if (ImGui.Checkbox("Collect Data", ref CollectData))
                        config.CollectData = CollectData;

                    bool AutoLabel = config.AutoLabel;
                    if (ImGui.Checkbox("Auto Label", ref AutoLabel))
                    {
                        if (!CollectData && AutoLabel)
                            config.CollectData = true;

                        config.AutoLabel = AutoLabel;
                    }

                    if (ImGui.Checkbox("VSync", ref _vsync))
                        VSync = _vsync;

                    bool DebugMode = config.DebugMode;
                    if (ImGui.Checkbox("Debug Mode", ref DebugMode))
                        config.DebugMode = DebugMode;

                    if (!VSync)
                        if (ImGuiExtensions.SliderFill("FPS Limit", ref _fpsLimit, 30, 480))
                            FPSLimit = _fpsLimit;

                    ImGui.TextUnformatted("Background Image Interval");
                    ImGui.SetNextItemWidth(-1);
                    int BackgroundImageInterval = config.BackgroundImageInterval;
                    if (ImGui.InputInt("##Background Image Interval", ref BackgroundImageInterval, 1, 1000))
                    {
                        if (BackgroundImageInterval < 1)
                            BackgroundImageInterval = 1;
                        else if (BackgroundImageInterval > 1000)
                            BackgroundImageInterval = 1000;

                        config.BackgroundImageInterval = BackgroundImageInterval;
                    }

                    if (!_waitingForKeybind.GetValueOrDefault("Menu Key", false))
                    {
                        if (ImGui.Button(config.MenuKey.ToString()))
                        {
                            _waitingForKeybind["Menu Key"] = true;
                            _pendingKeybind["Menu Key"] = Keys.None;
                            _keybindTask["Menu Key"] = Task.Run(async () =>
                            {
                                var key = await InputManager.ListenForNextKeyOrMouseAsync();
                                _pendingKeybind["Menu Key"] = key;
                            });
                        }
                    }
                    else
                    {
                        ImGui.Button("Listening...");
                        if (_pendingKeybind.GetValueOrDefault("Menu Key", Keys.None) != Keys.None)
                        {
                            config.MenuKey = _pendingKeybind["Menu Key"];
                            _pendingKeybind["Menu Key"] = Keys.None;
                            _waitingForKeybind["Menu Key"] = false;
                        }
                    }
                    ImGui.SameLine();
                    ImGui.Text("Menu Keybind");

                    ImGuiExtensions.EndPane();
                }

                if (ImGuiExtensions.BeginPane("Logs"))
                {
                    ImGui.BeginChild("Logs", new Vector2(0, -34), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);

                    float scrollY = ImGui.GetScrollY();
                    float scrollMaxY = ImGui.GetScrollMaxY();
                    bool isAtBottom = (scrollY >= scrollMaxY - 1.0f);

                    var _LogEntries = LogManager.LogEntries;
                    _LogEntries = [.. _LogEntries];
                    foreach (var log in _LogEntries)
                    {
                        ImGui.Text(log.ToString());
                    }

                    if (scrollToBottom || isAtBottom)
                    {
                        ImGui.SetScrollHereY(1.0f);
                        scrollToBottom = false;
                    }

                    ImGui.EndChild();
                    ImGui.Dummy(new Vector2(0, 1));

                    if (ImGui.Button("Save Logs"))
                        LogManager.SaveLog($"bin\\logs\\{DateTime.Now.ToString("MM-dd_HH-mm-ss")}_spectrum_log.txt");

                    ImGui.SameLine();

                    if (ImGui.Button("Clear Logs"))
                        LogManager.ClearLog();

                    ImGui.SameLine();

                    if (ImGui.Button("Open Log Folder"))
                        LogManager.OpenLogFolder();
                    ImGuiExtensions.EndPane();
                }



                ImGui.EndTabItem();
            }


            ImGui.EndTabBar();

            ImGui.End();

            RenderHSVRangeWindow();
        }


        protected void RenderOverlay()
        {
            ImGui.SetNextWindowSize(new Vector2(screenSize.width, screenSize.height), ImGuiCond.Always);
            ImGui.SetNextWindowPos(new Vector2(0, 0), ImGuiCond.Always);
            ImGui.Begin("Overlay", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNavFocus);
            var drawList = ImGui.GetWindowDrawList();
            var config = mainConfig.Data;

            if (config.DrawFOV)
            {
                if (config.FOVType == FovType.Rectangle)
                {
                    drawList.AddRect(new Vector2((screenSize.width - config.ImageWidth) / 2, (screenSize.height - config.ImageHeight) / 2),
                        new Vector2((screenSize.width + config.ImageWidth) / 2, (screenSize.height + config.ImageHeight) / 2),
                        ImGui.GetColorU32(config.FOVColor), 0.0f, ImDrawFlags.None, 1.0f);
                }
                else if (config.FOVType == FovType.Circle)
                {
                    drawList.AddCircle(new Vector2(screenSize.width / 2, screenSize.height / 2), config.ImageWidth / 2,
                        ImGui.GetColorU32(config.FOVColor), 100, 1.0f);
                } // added straight to drawlist to prevent caching/flickering
            }

            if (config.DebugMode)
            {
                var stats = Program.statistics;
                double avgTime = Math.Round(stats.avgProcessTime, 2);
                Vector2 textSize = ImGui.CalcTextSize($"FPS: {stats.fps} | AVG Process Time: {avgTime}ms");
                drawList.AddRectFilled(new Vector2(10, 10), new Vector2(10 + textSize.X + 10, 10 + textSize.Y + 10), ImGui.GetColorU32(new Vector4(0, 0, 0, 0.8f)), 5.0f);
                ImGui.SetCursorPos(new Vector2(15, 15));
                ImGui.TextUnformatted($"FPS: {stats.fps} | AVG Process Time: {avgTime}ms");
            }

            List<Action<ImDrawListPtr>> _drawCommands;
            lock (detectionDrawLock)
            {
                _drawCommands = [.. activeDrawCommands];
            }

            foreach (var cmd in _drawCommands)
            {
                cmd?.Invoke(drawList);
            }

            ImGui.End();
        }
        private void AddStyling()
        {
            var style = ImGui.GetStyle();
            var colors = style.Colors;

            style.WindowRounding = 4.0f;
            style.FrameRounding = 3.0f;
            style.ScrollbarRounding = 3.0f;
            style.GrabRounding = 3.0f;
            style.FrameBorderSize = 1.0f;
            style.WindowBorderSize = 1.0f;
            style.PopupRounding = 4.0f;
            style.TabRounding = 3.0f;
            style.ChildRounding = 3.0f;
            style.PopupBorderSize = 1.0f;
            style.GrabMinSize = 20.0f;
            style.WindowPadding = new Vector2(8, 8);
            style.FramePadding = new Vector2(6, 3);
            style.ItemSpacing = new Vector2(8, 4);
            style.ScrollbarRounding = 6.0f;
            style.ScrollbarSize = 12.0f;

            colors[(int)ImGuiCol.WindowBg] = new Vector4(0.10f, 0.10f, 0.12f, 1.0f);
            colors[(int)ImGuiCol.ChildBg] = new Vector4(0.12f, 0.12f, 0.14f, 1.0f);
            colors[(int)ImGuiCol.PopupBg] = new Vector4(0.11f, 0.11f, 0.12f, 1.0f);
            colors[(int)ImGuiCol.Border] = new Vector4(0.23f, 0.23f, 0.25f, 0.50f);
            colors[(int)ImGuiCol.BorderShadow] = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);

            colors[(int)ImGuiCol.FrameBg] = new Vector4(0.17f, 0.17f, 0.20f, 1.0f);
            colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.22f, 0.22f, 0.26f, 1.0f);
            colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.22f, 0.22f, 0.27f, 1.0f);

            colors[(int)ImGuiCol.TitleBg] = new Vector4(0.10f, 0.10f, 0.12f, 1.0f);
            colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.15f, 0.15f, 0.18f, 1.0f);
            colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.12f, 0.12f, 0.14f, 1.0f);

            colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.15f, 0.15f, 0.17f, 1.0f);

            colors[(int)ImGuiCol.CheckMark] = new Vector4(0.80f, 0.80f, 0.80f, 1.0f);
            colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.31f, 0.31f, 0.34f, 1.0f);
            colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.43f, 0.43f, 0.45f, 1.0f);
            colors[(int)ImGuiCol.Button] = new Vector4(0.17f, 0.17f, 0.20f, 1.0f);
            colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.24f, 0.24f, 0.28f, 1.0f);
            colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.29f, 0.29f, 0.33f, 1.0f);

            colors[(int)ImGuiCol.Header] = new Vector4(0.14f, 0.14f, 0.16f, 1.0f);
            colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.20f, 0.20f, 0.22f, 1.0f);
            colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.24f, 0.24f, 0.26f, 1.0f);

            colors[(int)ImGuiCol.Separator] = new Vector4(0.23f, 0.23f, 0.25f, 0.5f);
            colors[(int)ImGuiCol.SeparatorHovered] = new Vector4(0.33f, 0.33f, 0.35f, 0.78f);
            colors[(int)ImGuiCol.SeparatorActive] = new Vector4(0.32f, 0.32f, 0.35f, 1.0f);

            colors[(int)ImGuiCol.ResizeGrip] = new Vector4(0.23f, 0.23f, 0.25f, 0.2f);
            colors[(int)ImGuiCol.ResizeGripHovered] = new Vector4(0.33f, 0.33f, 0.36f, 0.7f);
            colors[(int)ImGuiCol.ResizeGripActive] = new Vector4(0.36f, 0.36f, 0.39f, 0.9f);

            colors[(int)ImGuiCol.Tab] = new Vector4(0.13f, 0.13f, 0.15f, 1.0f);
            colors[(int)ImGuiCol.TabHovered] = new Vector4(0.18f, 0.18f, 0.20f, 1.0f);
            colors[(int)ImGuiCol.TabSelected] = new Vector4(0.22f, 0.22f, 0.25f, 1.0f);
            colors[(int)ImGuiCol.TabDimmed] = new Vector4(0.13f, 0.13f, 0.15f, 1.0f);
            colors[(int)ImGuiCol.TabDimmedSelected] = new Vector4(0.18f, 0.18f, 0.20f, 1.0f);

            colors[(int)ImGuiCol.Text] = new Vector4(0.85f, 0.85f, 0.88f, 1.0f);
            colors[(int)ImGuiCol.TextDisabled] = new Vector4(0.48f, 0.48f, 0.52f, 1.0f);

            colors[(int)ImGuiCol.PlotLines] = new Vector4(0.61f, 0.61f, 0.62f, 1.0f);
            colors[(int)ImGuiCol.PlotLinesHovered] = new Vector4(0.80f, 0.80f, 0.81f, 1.0f);
            colors[(int)ImGuiCol.PlotHistogram] = new Vector4(0.61f, 0.61f, 0.62f, 1.0f);
            colors[(int)ImGuiCol.PlotHistogramHovered] = new Vector4(0.80f, 0.80f, 0.81f, 1.0f);

            colors[(int)ImGuiCol.DragDropTarget] = new Vector4(0.90f, 0.90f, 0.90f, 0.90f);

            colors[(int)ImGuiCol.NavWindowingHighlight] = new Vector4(0.70f, 0.70f, 0.70f, 0.70f);
            colors[(int)ImGuiCol.NavWindowingDimBg] = new Vector4(0.20f, 0.20f, 0.20f, 0.20f);
            colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0.10f, 0.10f, 0.10f, 0.35f);

            colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.15f, 0.15f, 0.18f, 1.0f);
            colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.31f, 0.31f, 0.34f, 1.0f);
            colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.43f, 0.43f, 0.45f, 1.0f);
            colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(0.50f, 0.50f, 0.52f, 1.0f);
        }
        public void AddRect(Rectangle rect, Vector4 color, float thickness = 1.0f)
        {
            lock (detectionDrawLock)
            {
                detectionDrawCommands.Add(drawList => drawList.AddRect(new Vector2(rect.Left, rect.Top), new Vector2(rect.Right, rect.Bottom), ColorFromVector4(color), 0, 0, thickness));
            }
        }

        public void AddLine(Vector2 p1, Vector2 p2, Vector4 color, float thickness = 1.0f)
        {
            lock (detectionDrawLock)
            {
                detectionDrawCommands.Add(drawList => drawList.AddLine(p1, p2, ColorFromVector4(color), thickness));
            }
        }

        public void AddCircle(Vector2 center, float radius, Vector4 color, int numSegments = 0, float thickness = 1.0f)
        {
            if (numSegments <= 0)
                numSegments = (int)(Math.Max(1, radius / 2.0f));

            lock (detectionDrawLock)
            {
                detectionDrawCommands.Add(drawList => drawList.AddCircle(center, radius, ColorFromVector4(color), numSegments, thickness));
            }
        }

        public void AddText(Vector2 pos, Vector4 color, string text, int fontSize = 16)
        {
            if (string.IsNullOrEmpty(text))
                return;
            lock (detectionDrawLock)
            {
                detectionDrawCommands.Add(drawList => drawList.AddText(ImGui.GetFont(), fontSize, pos, ColorFromVector4(color), text));
            }
        }

        public void CommitDetectionDrawCommands()
        {
            lock (detectionDrawLock)
            {
                activeDrawCommands = detectionDrawCommands;
                detectionDrawCommands = new List<Action<ImDrawListPtr>>();
            }
        }

        public void ClearDetectionDrawCommands()
        {
            lock (detectionDrawLock)
            {
                detectionDrawCommands.Clear();
            }
        }

        public Vector2 GetTextWidth(string text, int fontSize = 16)
        {
            if (string.IsNullOrEmpty(text))
                return new Vector2(0, 0);
            return ImGui.CalcTextSize(text, 0, text.Length, fontSize);
        }

        private static uint ColorFromVector4(Vector4 color)
        {
            int r = (int)(Math.Clamp(color.X, 0f, 1f) * 255.0f);
            int g = (int)(Math.Clamp(color.Y, 0f, 1f) * 255.0f);
            int b = (int)(Math.Clamp(color.Z, 0f, 1f) * 255.0f);
            int a = (int)(Math.Clamp(color.W, 0f, 1f) * 255.0f);
            return (uint)((a << 24) | (b << 16) | (g << 8) | r);
        }
    }
}