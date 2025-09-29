using ClickableTransparentOverlay;
using ImGuiNET;
using OpenCvSharp;
using Spectrum.Detection;
using Spectrum.Input;
using Spectrum.Input.InputLibraries.Arduino;
using Spectrum.Input.InputLibraries.Makcu;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;

namespace Spectrum
{
    partial class Renderer : Overlay
    {
        static (int width, int height) screenSize = SystemHelper.GetPrimaryScreenSize();
        private List<Action<ImDrawListPtr>> drawCommands = new List<Action<ImDrawListPtr>>();
        private readonly object drawCommandsLock = new object();
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
            if (mainConfig.Data.DrawDetections || mainConfig.Data.DrawFOV)
                RenderOverlay();
            if (!mainConfig.Data.ShowMenu)
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
                    bool enableAiming = mainConfig.Data.EnableAim;
                    if (ImGui.Checkbox("##Enable Aiming", ref enableAiming))
                    {
                        mainConfig.Data.EnableAim = enableAiming;
                    }

                    ImGui.SameLine();
                    ImGui.TextUnformatted("Enable Aiming");

                    ImGui.SameLine(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(_waitingForKeybind.GetValueOrDefault("Aiming") ? "Listening..." : mainConfig.Data.Keybind.ToString()).X);
                    if (!_waitingForKeybind.GetValueOrDefault("Aiming", false))
                    {
                        if (ImGui.Button($"{mainConfig.Data.Keybind.ToString()}###AimKeybind"))
                        {
                            _waitingForKeybind["Aiming"] = true;
                            _pendingKeybind["Aiming"] = Keys.None;
                            _keybindTask["Aiming"] = Task.Run(async () =>
                            {
                                var key = await InputManager.ListenForNextKeyOrMouseAsync();
                                _pendingKeybind["Aiming"] = key;
                            });
                        }
                    }
                    else
                    {
                        ImGui.Button("Listening... ###AimListening");
                        if (_pendingKeybind.GetValueOrDefault("Aiming", Keys.None) != Keys.None)
                        {
                            mainConfig.Data.Keybind = _pendingKeybind["Aiming"];
                            _pendingKeybind["Aiming"] = Keys.None;
                            _waitingForKeybind["Aiming"] = false;
                        }
                    }

                    bool closestToMouse = mainConfig.Data.ClosestToMouse;
                    if (ImGui.Checkbox(" Closest to Mouse", ref closestToMouse))
                    {
                        mainConfig.Data.ClosestToMouse = closestToMouse;
                    }

                    float sensitivity = (float)mainConfig.Data.Sensitivity;
                    if (ImGuiExtensions.SliderFill("Sensitivity", ref sensitivity, 0.1f, 2.0f, "%.1f"))
                    {
                        mainConfig.Data.Sensitivity = sensitivity;
                    }

                    float EmaSmootheningFactor = (float)mainConfig.Data.EmaSmootheningFactor;
                    bool EmaSmoothening = mainConfig.Data.EmaSmoothening;
                    if (ImGui.Checkbox("Ema Smoothening", ref EmaSmoothening))
                    {
                        mainConfig.Data.EmaSmoothening = EmaSmoothening;
                    }

                    if (EmaSmoothening)
                    {
                        if (ImGuiExtensions.SliderFill("Ema Smoothening Factor", ref EmaSmootheningFactor, 0.01f, 1.0f))
                        {
                            mainConfig.Data.EmaSmootheningFactor = EmaSmootheningFactor;
                        }
                    }

                    ImGui.TextUnformatted("Movement Type");
                    ImGui.SetNextItemWidth(-1);
                    MovementType AimPath = mainConfig.Data.AimMovementType;
                    if (ImGui.BeginCombo("##Movement Type", AimPath.ToString()))
                    {
                        foreach (MovementType type in Enum.GetValues(typeof(MovementType)))
                        {
                            bool isSelected = (type == AimPath);
                            string displayName = string.Concat(type.ToString().Select((x, i) => i > 0 && char.IsUpper(x) ? " " + x : x.ToString())); // just adds a space before capitals except for the first
                            if (ImGui.Selectable(displayName, isSelected))
                            {
                                mainConfig.Data.AimMovementType = type;
                            }
                            if (isSelected)
                            {
                                ImGui.SetItemDefaultFocus();
                            }
                        }
                        ImGui.EndCombo();
                    }

                    float YOffset = (int)(mainConfig.Data.YOffsetPercent * 100);
                    if (ImGuiExtensions.SliderFill("Y Offset (%)", ref YOffset, 0, 100, "%d%%"))
                    {
                        mainConfig.Data.YOffsetPercent = ((double)YOffset / 100);
                    }

                    float XOffset = (int)(mainConfig.Data.XOffsetPercent * 100);
                    if (ImGuiExtensions.SliderFill("X Offset (%)", ref XOffset, 0, 100, "%d%%"))
                    {
                        mainConfig.Data.XOffsetPercent = ((double)XOffset / 100);
                    }

                    ImGui.SeparatorText("Overlay Settings");

                    bool DrawFOV = mainConfig.Data.DrawFOV;
                    if (ImGui.Checkbox("##Draw FOV", ref DrawFOV))
                    {
                        mainConfig.Data.DrawFOV = DrawFOV;
                    }

                    ImGui.SameLine();
                    ImGui.Text("Draw FOV");

                    ImGui.SameLine(ImGui.GetContentRegionAvail().X - 12);
                    Vector4 FOVColor = mainConfig.Data.FOVColor;
                    if (ImGui.ColorEdit4("FOV Color", ref FOVColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
                    {
                        mainConfig.Data.FOVColor = FOVColor;
                    }

                    bool DrawDetections = mainConfig.Data.DrawDetections;
                    if (ImGui.Checkbox("##Draw Detections", ref DrawDetections))
                    {
                        mainConfig.Data.DrawDetections = DrawDetections;
                    }
                    ImGui.SameLine();
                    ImGui.Text("Draw Detections");

                    ImGui.SameLine(ImGui.GetContentRegionAvail().X - 12);
                    Vector4 DetectionColor = mainConfig.Data.DetectionColor;
                    if (ImGui.ColorEdit4("Draw Color", ref DetectionColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
                    {
                        mainConfig.Data.DetectionColor = DetectionColor;
                    }

                    if (DrawFOV)
                    {
                        ImGui.TextUnformatted("FOV Type");
                        ImGui.SetNextItemWidth(-1);
                        FovType fovType = mainConfig.Data.FOVType;
                        if (ImGui.BeginCombo("##FOV Type", fovType.ToString()))
                        {
                            foreach (FovType type in Enum.GetValues(typeof(FovType)))
                            {
                                bool isSelected = (type == fovType);
                                if (ImGui.Selectable(type.ToString(), isSelected))
                                {
                                    mainConfig.Data.FOVType = type;
                                }
                                if (isSelected)
                                {
                                    ImGui.SetItemDefaultFocus();
                                }
                            }
                            ImGui.EndCombo();
                        }
                    }
                    ImGuiExtensions.EndPane();
                }

                if (ImGuiExtensions.BeginPane("Triggerbot"))
                {
                    bool triggerBot = mainConfig.Data.TriggerBot;
                    if (ImGui.Checkbox(" Enabled", ref triggerBot))
                    {
                        mainConfig.Data.TriggerBot = triggerBot;
                    }
                    ImGui.SameLine(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(_waitingForKeybind.GetValueOrDefault("Trigger Key") ? "Listening..." : mainConfig.Data.TriggerKey.ToString()).X);
                    if (!_waitingForKeybind.GetValueOrDefault("Trigger Key", false))
                    {
                        if (ImGui.Button($"{mainConfig.Data.TriggerKey}###TriggerKeybind"))
                        {
                            _waitingForKeybind["Trigger Key"] = true;
                            _pendingKeybind["Trigger Key"] = Keys.None;
                            _keybindTask["Trigger Key"] = Task.Run(async () =>
                            {
                                var key = await InputManager.ListenForNextKeyOrMouseAsync();
                                _pendingKeybind["Trigger Key"] = key;
                            });
                        }
                    }
                    else
                    {
                        ImGui.Button("Listening... ###TriggerListening");
                        if (_pendingKeybind.GetValueOrDefault("Trigger Key", Keys.None) != Keys.None)
                        {
                            mainConfig.Data.TriggerKey = _pendingKeybind["Trigger Key"];
                            _pendingKeybind["Trigger Key"] = Keys.None;
                            _waitingForKeybind["Trigger Key"] = false;
                        }
                    }

                    if (triggerBot)
                    {
                        int triggerDelay = mainConfig.Data.TriggerDelay;
                        if (ImGuiExtensions.SliderFill("Trigger Delay (ms)", ref triggerDelay, 1, 1000))
                        {
                            mainConfig.Data.TriggerDelay = triggerDelay;
                        }

                        int triggerRadius = mainConfig.Data.TriggerRadius;
                        if (ImGuiExtensions.SliderFill("Trigger Radius (px)", ref triggerRadius, 1, 100))
                        {
                            mainConfig.Data.TriggerRadius = triggerRadius;
                        }

                        int triggerDuration = mainConfig.Data.TriggerDuration;
                        if (ImGuiExtensions.SliderFill("Trigger Duration (ms)", ref triggerDuration, 1, 1000))
                        {
                            mainConfig.Data.TriggerDuration = triggerDuration;
                        }

                        bool DrawTriggerRadius = mainConfig.Data.DrawTriggerRadius;
                        if (ImGui.Checkbox("##Draw Trigger Radius", ref DrawTriggerRadius))
                        {
                            mainConfig.Data.DrawTriggerRadius = DrawTriggerRadius;
                        }
                        ImGui.SameLine();
                        ImGui.TextUnformatted("Draw Trigger Radius");
                        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 12);
                        Vector4 RadiusColor = mainConfig.Data.TriggerRadiusColor;
                        if (ImGui.ColorEdit4("Trigger Color", ref RadiusColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
                        {
                            mainConfig.Data.DetectionColor = RadiusColor;
                        }

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
                    int ImageWidth = mainConfig.Data.ImageWidth;
                    if (ImGui.InputInt("##Image Width", ref ImageWidth, 1, screenSize.width))
                    {
                        if (ImageWidth < 1)
                            ImageWidth = 1;
                        else if (ImageWidth > screenSize.width)
                            ImageWidth = screenSize.width;
                        mainConfig.Data.ImageWidth = ImageWidth;
                    }

                    ImGui.TextUnformatted("Image Height");
                    ImGui.SetNextItemWidth(-1);
                    int ImageHeight = mainConfig.Data.ImageHeight;
                    if (ImGui.InputInt("##Image Height", ref ImageHeight, 1, screenSize.height))
                    {
                        if (ImageHeight < 1)
                            ImageHeight = 1;
                        else if (ImageHeight > screenSize.height)
                            ImageHeight = screenSize.height;
                        mainConfig.Data.ImageHeight = ImageHeight;
                    }

                    ImGui.SetNextItemWidth(-1);
                    Scalar upperHSV = mainConfig.Data.UpperHSV;
                    if (ImGuiExtensions.ColorEditHSV3("Upper HSV", ref upperHSV))
                    {
                        mainConfig.Data.UpperHSV = upperHSV;
                    }
                    Scalar lowerHSV = mainConfig.Data.LowerHSV;
                    if (ImGuiExtensions.ColorEditHSV3("Lower HSV", ref lowerHSV))
                    {
                        mainConfig.Data.LowerHSV = lowerHSV;
                    }

                    ImGuiExtensions.EndPane();
                }

                if (ImGuiExtensions.BeginPane("Detection Configs"))
                {
                    List<ColorInfo> colors = colorConfig.Data.Colors;
                    ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.110f, 0.110f, 0.125f, 1.000f));
                    ImGui.BeginChild("##DetectionConfigsChild", new Vector2(0, ImGui.GetContentRegionAvail().Y / 2), ImGuiChildFlags.None);
                    ImGui.Dummy(new Vector2(0, 4));
                    foreach (var color in colors)
                    {
                        if (ImGui.Selectable("  " + color.Name, selectedColor == color))
                        {
                            selectedColor = color;
                            ColorName = color.Name;
                            mainConfig.Data.SelectedColor = selectedColor.Name;
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
                            mainConfig.Data.SelectedColor = selectedColor.Name;
                            mainConfig.Data.UpperHSV = selectedColor.Upper;
                            mainConfig.Data.LowerHSV = selectedColor.Lower;
                            ColorName = selectedColor.Name;
                        }
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("Save Color", size))
                    {
                        if (colors.Find(c => c.Name.Equals(ColorName, StringComparison.OrdinalIgnoreCase)) is ColorInfo existingColor)
                        {
                            existingColor.Upper = mainConfig.Data.UpperHSV;
                            existingColor.Lower = mainConfig.Data.LowerHSV;
                            selectedColor = existingColor;
                        }
                        else
                        {
                            colors.Add(new ColorInfo(ColorName, mainConfig.Data.UpperHSV, mainConfig.Data.LowerHSV));
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
                                int nextIndex = Math.Max(0, currentIndex - 1);
                                selectedColor = colors[currentIndex - 1];
                                mainConfig.Data.SelectedColor = selectedColor.Name;
                                mainConfig.Data.UpperHSV = selectedColor.Upper;
                                mainConfig.Data.LowerHSV = selectedColor.Lower;
                                ColorName = selectedColor.Name;
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

            if (ImGui.BeginTabItem("Settings"))
            {
                ImGuiExtensions.BeginPaneGroup("Settings Panes", 2, 12f, new Vector2(12, 10), ImGuiChildFlags.AlwaysUseWindowPadding, ImGuiWindowFlags.None, 0f);

                if (ImGuiExtensions.BeginPane("Settings"))
                {
                    ImGui.TextUnformatted("Movement Method");
                    ImGui.SetNextItemWidth(-1);
                    MovementMethod movementMethod = mainConfig.Data.MovementMethod;
                    if (ImGui.BeginCombo("##Movement Method", movementMethod.ToString()))
                    {
                        foreach (MovementMethod method in Enum.GetValues(typeof(MovementMethod)))
                        {
                            bool isSelected = (method == movementMethod);
                            if (ImGui.Selectable(method.ToString(), isSelected))
                            {
                                mainConfig.Data.MovementMethod = method;
                                switch (method)
                                {
                                    case MovementMethod.Makcu:
                                        {
                                            ArduinoMain.Close();
                                            var ok = MakcuMain.Load().GetAwaiter().GetResult();
                                            if (!ok)
                                            {
                                                LogManager.Log("Makcu failed to initialize. Falling back to MouseEvent.", LogManager.LogLevel.Warning);
                                                mainConfig.Data.MovementMethod = MovementMethod.MouseEvent;
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
                                                mainConfig.Data.MovementMethod = MovementMethod.MouseEvent;
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
                            {
                                ImGui.SetItemDefaultFocus();
                            }
                        }
                        ImGui.EndCombo();
                    }

                    ImGui.TextUnformatted("Capture Method");
                    ImGui.SetNextItemWidth(-1);
                    CaptureMethod captureMethod = mainConfig.Data.CaptureMethod;
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
                                mainConfig.Data.CaptureMethod = _method;
                            }
                            if (isSelected)
                            {
                                ImGui.SetItemDefaultFocus();
                            }
                        }
                        ImGui.EndCombo();
                    }

                    bool CollectData = mainConfig.Data.CollectData;
                    if (ImGui.Checkbox("Collect Data", ref CollectData))
                    {
                        mainConfig.Data.CollectData = CollectData;
                    }

                    bool AutoLabel = mainConfig.Data.AutoLabel;
                    if (ImGui.Checkbox("Auto Label", ref AutoLabel))
                    {
                        if (!CollectData && AutoLabel)
                            mainConfig.Data.CollectData = true;

                        mainConfig.Data.AutoLabel = AutoLabel;
                    }

                    if (ImGui.Checkbox("VSync", ref _vsync))
                    {
                        VSync = _vsync;
                    }

                    bool DebugMode = mainConfig.Data.DebugMode;
                    if (ImGui.Checkbox("Debug Mode", ref DebugMode))
                    {
                        mainConfig.Data.DebugMode = DebugMode;
                    }

                    if (!VSync)
                    {
                        if (ImGuiExtensions.SliderFill("FPS Limit", ref _fpsLimit, 30, 480))
                        {
                            FPSLimit = _fpsLimit;
                        }
                    }

                    ImGui.TextUnformatted("Background Image Interval");
                    ImGui.SetNextItemWidth(-1);
                    int BackgroundImageInterval = mainConfig.Data.BackgroundImageInterval;
                    if (ImGui.InputInt("##Background Image Interval", ref BackgroundImageInterval, 1, 1000))
                    {
                        if (BackgroundImageInterval < 1)
                            BackgroundImageInterval = 1;
                        else if (BackgroundImageInterval > 1000)
                            BackgroundImageInterval = 1000;

                        mainConfig.Data.BackgroundImageInterval = BackgroundImageInterval;
                    }

                    if (!_waitingForKeybind.GetValueOrDefault("Menu Key", false))
                    {
                        if (ImGui.Button(mainConfig.Data.MenuKey.ToString()))
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
                            mainConfig.Data.MenuKey = _pendingKeybind["Menu Key"];
                            _pendingKeybind["Menu Key"] = Keys.None;
                            _waitingForKeybind["Menu Key"] = false;
                        }
                    }
                    ImGui.SameLine();
                    ImGui.Text("Menu Keybind");

                    if (ImGui.Button("Save Configs"))
                    {
                        mainConfig.SaveConfig();
                        colorConfig.SaveConfig();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Open Folder"))
                    {
                        string folderPath = Path.Combine(Directory.GetCurrentDirectory(), "bin\\configs");
                        if (Directory.Exists(folderPath))
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "explorer.exe",
                                Arguments = folderPath,
                                UseShellExecute = true
                            });
                        }
                        else
                        {
                            LogManager.Log($"Directory doesnt exist: {folderPath}", LogManager.LogLevel.Error);
                        }
                    }
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
                    {
                        LogManager.SaveLog($"bin\\logs\\{DateTime.Now.ToString("MM-dd_HH-mm-ss")}_spectrum_log.txt");
                    }

                    ImGui.SameLine();

                    if (ImGui.Button("Clear Logs"))
                    {
                        LogManager.ClearLog();
                    }

                    ImGui.SameLine();

                    if (ImGui.Button("Open Log Folder"))
                    {
                        LogManager.OpenLogFolder();
                    }
                    ImGuiExtensions.EndPane();
                }



                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();

            ImGui.End();
        }


        protected void RenderOverlay()
        {
            ImGui.SetNextWindowSize(new Vector2(screenSize.width, screenSize.height), ImGuiCond.Always);
            ImGui.SetNextWindowPos(new Vector2(0, 0), ImGuiCond.Always);
            ImGui.Begin("Overlay", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNavFocus);
            var drawList = ImGui.GetWindowDrawList();

            if (mainConfig.Data.DrawFOV)
            {
                if (mainConfig.Data.FOVType == FovType.Rectangle)
                {
                    AddRect(new Rectangle(
                        ((screenSize.width - mainConfig.Data.ImageWidth) / 2) - 1,
                        ((screenSize.height - mainConfig.Data.ImageHeight) / 2) - 1,
                        mainConfig.Data.ImageWidth + 2,
                        mainConfig.Data.ImageHeight + 2
                    ), mainConfig.Data.FOVColor);
                }
                else if (mainConfig.Data.FOVType == FovType.Circle)
                {
                    AddCircle(new Vector2(screenSize.width / 2, screenSize.height / 2), mainConfig.Data.ImageWidth / 2, mainConfig.Data.FOVColor, 100, 1.0f);
                }
            }

            List<Action<ImDrawListPtr>> _drawCommands;
            lock (drawCommandsLock)
            {
                _drawCommands = drawCommands.ToList();
                drawCommands.Clear();
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
            lock (drawCommandsLock)
            {
                drawCommands.Add(drawList => drawList.AddRect(new Vector2(rect.Left, rect.Top), new Vector2(rect.Right, rect.Bottom), ColorFromVector4(color), 0, 0, thickness));
            }
        }

        public void AddLine(Vector2 p1, Vector2 p2, Vector4 color, float thickness = 1.0f)
        {
            lock (drawCommandsLock)
            {
                drawCommands.Add(drawList => drawList.AddLine(p1, p2, ColorFromVector4(color), thickness));
            }
        }

        public void AddCircle(Vector2 center, float radius, Vector4 color, int numSegments = 0, float thickness = 1.0f)
        {
            if (numSegments <= 0)
                numSegments = (int)(Math.Max(1, radius / 2.0f));

            lock (drawCommandsLock)
            {
                drawCommands.Add(drawList => drawList.AddCircle(center, radius, ColorFromVector4(color), numSegments, thickness));
            }
        }

        public void AddText(Vector2 pos, Vector4 color, string text, int fontSize = 16)
        {
            if (string.IsNullOrEmpty(text))
                return;
            lock (drawCommandsLock)
            {
                drawCommands.Add(drawList => drawList.AddText(ImGui.GetFont(), fontSize, pos, ColorFromVector4(color), text));
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
    public static class ImGuiExtensions
    {
        private static readonly Dictionary<string, string> _sliderEditBuffers = new();
        public static bool ColorEditHSV3(string label, ref Scalar hsv)
        {
            ImGui.PushID(label);

            int h = (int)Math.Clamp(Math.Round(hsv.Val0), 0, 179);
            int s = (int)Math.Clamp(Math.Round(hsv.Val1), 0, 255);
            int v = (int)Math.Clamp(Math.Round(hsv.Val2), 0, 255);

            bool changed = false;
            bool inputChanged = false;

            ImGui.TextUnformatted(label);

            var style = ImGui.GetStyle();
            float avail = ImGui.GetContentRegionAvail().X;
            float previewWidth = 21.0f;
            float totalSpacing = style.ItemSpacing.X * 3;
            float partWidth = Math.Max(24.0f, (avail - totalSpacing - previewWidth) / 3.0f);

            ImGui.PushItemWidth(partWidth);
            if (ImGui.InputInt($"##{label}H", ref h, 0, 179))
                inputChanged = true;
            ImGui.PopItemWidth();

            ImGui.SameLine();
            ImGui.PushItemWidth(partWidth);
            if (ImGui.InputInt($"##{label}S", ref s, 0, 255))
                inputChanged = true;
            ImGui.PopItemWidth();

            ImGui.SameLine();
            ImGui.PushItemWidth(partWidth);
            if (ImGui.InputInt($"##{label}V", ref v, 0, 255))
                inputChanged = true;
            ImGui.PopItemWidth();

            if (inputChanged)
            {
                hsv = new Scalar(h, s, v);
                changed = true;
            }

            Vector3 rgbColor = OpenCvHsvToRgb(hsv);

            ImGui.SameLine();
            Vector2 btnSize = new Vector2(previewWidth, previewWidth);
            Vector4 previewCol4 = new Vector4(rgbColor.X, rgbColor.Y, rgbColor.Z, 1.0f);
            string previewBtnId = $"##ColorPreview_{label}";
            if (ImGui.ColorButton(previewBtnId, previewCol4, ImGuiColorEditFlags.NoInputs, btnSize))
            {
                ImGui.OpenPopup($"##ColorPicker_{label}");
            }
            if (ImGui.BeginPopup($"##ColorPicker_{label}"))
            {
                Vector3 picker = rgbColor;
                if (ImGui.ColorPicker3($"##ColorPickerPicker_{label}", ref picker))
                {
                    rgbColor = picker;
                    Scalar newHsv = RgbToOpenCvHsv(rgbColor);
                    hsv = newHsv;
                    changed = true;
                }
                ImGui.EndPopup();
            }

            ImGui.PopID();

            return changed;
        }

        private static Vector3 OpenCvHsvToRgb(Scalar hsv)
        {
            double h = Math.Clamp(hsv.Val0, 0.0, 179.0);
            double s = Math.Clamp(hsv.Val1, 0.0, 255.0);
            double v = Math.Clamp(hsv.Val2, 0.0, 255.0);

            double hueDeg = (h / 179.0) * 360.0;
            if (hueDeg >= 360.0)
                hueDeg -= 360.0;

            double saturation = s / 255.0;
            double value = v / 255.0;

            if (saturation <= double.Epsilon)
            {
                return new Vector3((float)value, (float)value, (float)value);
            }

            double c = value * saturation;
            double huePrime = hueDeg / 60.0;
            double x = c * (1 - Math.Abs(huePrime % 2 - 1));
            double m = value - c;

            double r1 = 0, g1 = 0, b1 = 0;

            int region = (int)Math.Floor(huePrime);
            if (region >= 6)
                region = 0;

            switch (region)
            {
                case 0:
                    r1 = c;
                    g1 = x;
                    b1 = 0;
                    break;
                case 1:
                    r1 = x;
                    g1 = c;
                    b1 = 0;
                    break;
                case 2:
                    r1 = 0;
                    g1 = c;
                    b1 = x;
                    break;
                case 3:
                    r1 = 0;
                    g1 = x;
                    b1 = c;
                    break;
                case 4:
                    r1 = x;
                    g1 = 0;
                    b1 = c;
                    break;
                case 5:
                default:
                    r1 = c;
                    g1 = 0;
                    b1 = x;
                    break;
            }

            double r = r1 + m;
            double g = g1 + m;
            double b = b1 + m;

            return new Vector3(
                (float)Math.Clamp(r, 0.0, 1.0),
                (float)Math.Clamp(g, 0.0, 1.0),
                (float)Math.Clamp(b, 0.0, 1.0)
            );
        }

        private static Scalar RgbToOpenCvHsv(Vector3 rgb)
        {
            double r = Math.Clamp(rgb.X, 0f, 1f);
            double g = Math.Clamp(rgb.Y, 0f, 1f);
            double b = Math.Clamp(rgb.Z, 0f, 1f);

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            double hueDeg;
            if (delta <= double.Epsilon)
            {
                hueDeg = 0;
            }
            else if (Math.Abs(max - r) < double.Epsilon)
            {
                hueDeg = 60.0 * (((g - b) / delta) % 6.0);
            }
            else if (Math.Abs(max - g) < double.Epsilon)
            {
                hueDeg = 60.0 * (((b - r) / delta) + 2.0);
            }
            else
            {
                hueDeg = 60.0 * (((r - g) / delta) + 4.0);
            }

            if (hueDeg < 0)
                hueDeg += 360.0;

            double saturation = max <= double.Epsilon ? 0.0 : (delta / max);
            double value = max;

            int h = (int)Math.Round((hueDeg / 360.0) * 179.0);
            int s = (int)Math.Round(saturation * 255.0);
            int v = (int)Math.Round(value * 255.0);

            h = Math.Clamp(h, 0, 179);
            s = Math.Clamp(s, 0, 255);
            v = Math.Clamp(v, 0, 255);

            return new Scalar(h, s, v);
        }

        public static bool SliderFill(string label, ref float value, float min, float max, string format = "%.2f")
        {
            bool changed = false;

            string FormatValue(float v)
            {
                if (string.IsNullOrEmpty(format))
                    return v.ToString(CultureInfo.InvariantCulture);

                bool percent = format.Contains("%%");
                string core = format.Replace("%%", "");

                string result;
                if (core.Contains("%d"))
                {
                    result = ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    int pos = core.IndexOf("%.");
                    if (pos >= 0)
                    {
                        int fPos = core.IndexOf('f', pos + 2);
                        if (fPos > pos)
                        {
                            string digits = core.Substring(pos + 2, fPos - (pos + 2));
                            if (int.TryParse(digits, out int prec))
                                result = v.ToString("F" + prec, CultureInfo.InvariantCulture);
                            else
                                result = v.ToString("F2", CultureInfo.InvariantCulture);
                        }
                        else
                            result = v.ToString("F2", CultureInfo.InvariantCulture);
                    }
                    else if (core.Contains("%f"))
                    {
                        result = v.ToString("F2", CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        result = v.ToString(core, CultureInfo.InvariantCulture);
                    }
                }
                if (percent) result += "%";
                return result;
            }

            ImGui.TextUnformatted(label);
            string valueStr = FormatValue(value);
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(valueStr).X + 12);
            ImGui.TextUnformatted(valueStr);

            float sliderWidth = ImGui.GetContentRegionAvail().X;
            float sliderHeight = ImGui.GetFrameHeight();

            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
            ImGui.InvisibleButton("##" + label, new Vector2(sliderWidth, sliderHeight));
            bool active = ImGui.IsItemActive();
            bool activated = ImGui.IsItemActivated();
            var io = ImGui.GetIO();

            if (activated && (io.KeyCtrl))
            {
                string popupId = $"##SliderFillEditPopup_{label}";
                string seed = value.ToString(CultureInfo.InvariantCulture);
                if (!_sliderEditBuffers.ContainsKey(label))
                    _sliderEditBuffers[label] = seed;
                ImGui.OpenPopup(popupId);
            }

            if (active && !io.KeyCtrl)
            {
                float mouseDelta = ImGui.GetIO().MousePos.X - ImGui.GetItemRectMin().X;
                float newValue = min + (mouseDelta / sliderWidth) * (max - min);
                newValue = Math.Clamp(newValue, min, max);
                if (format.Contains("%d"))
                {
                    newValue = (int)Math.Round(newValue);
                }
                else
                {
                    int pos = format.IndexOf("%.");
                    if (pos >= 0)
                    {
                        int fPos = format.IndexOf('f', pos + 2);
                        if (fPos > pos)
                        {
                            string digits = format.Substring(pos + 2, fPos - (pos + 2));
                            if (int.TryParse(digits, out int prec))
                                newValue = (float)Math.Round(newValue, prec);
                        }
                    }
                }
                if (Math.Abs(newValue - value) > float.Epsilon)
                {
                    value = newValue;
                    changed = true;
                }
            }

            string popupIdOuter = $"##SliderFillEditPopup_{label}";
            if (ImGui.BeginPopup(popupIdOuter))
            {
                ImGui.TextUnformatted(label);
                ImGui.Separator();
                ImGui.PushItemWidth(-1);
                string buffer = value.ToString(CultureInfo.InvariantCulture);
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 4));
                var inputFlags = ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.CharsDecimal | ImGuiInputTextFlags.CharsNoBlank;
                if (ImGui.InputText($"##SliderFillEdit_{label}", ref buffer, 32, inputFlags))
                {
                    string parseStr = buffer.Trim();
                    if (parseStr.EndsWith("%")) parseStr = parseStr.TrimEnd('%');
                    if (float.TryParse(parseStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float typed))
                    {
                        float clamped = Math.Clamp(typed, min, max);
                        if (Math.Abs(clamped - value) > float.Epsilon)
                        {
                            value = clamped;
                            changed = true;
                        }
                    }
                    ImGui.CloseCurrentPopup();
                }
                ImGui.PopStyleVar();

                _sliderEditBuffers[label] = buffer;
                if (ImGui.IsKeyPressed(ImGuiKey.Escape))
                {
                    ImGui.CloseCurrentPopup();
                }
                ImGui.PopItemWidth();
                ImGui.EndPopup();
            }

            var drawList = ImGui.GetWindowDrawList();
            Vector2 p0 = ImGui.GetItemRectMin();
            Vector2 p1 = ImGui.GetItemRectMax();

            drawList.AddRectFilled(p0, p1, ImGui.GetColorU32(ImGuiCol.FrameBg), 4f);

            float fillWidth = ((value - min) / (max - min)) * (p1.X - p0.X);
            drawList.AddRectFilled(p0, new Vector2(p0.X + fillWidth, p1.Y), ImGui.GetColorU32(ImGuiCol.SliderGrabActive), 4f);

            ImGui.PopStyleVar();

            return changed;
        }

        public static bool SliderFill(string label, ref int value, int min, int max)
        {
            float temp = value;
            bool changed = SliderFill(label, ref temp, min, max, "");
            if (changed)
                value = (int)Math.Round(temp);
            return changed;
        }
        private class PaneGroupState
        {
            public string Id = "";
            public int Count;
            public float Spacing;
            public int Index;
            public float Width;
            public Vector2? Padding;
            public ImGuiChildFlags ChildFlags;
            public ImGuiWindowFlags WindowFlags;
            public float Height;
        }

        private static readonly Stack<PaneGroupState> _paneGroups = new();
        private static readonly Stack<bool> _paneInnerStack = new();

        public static void BeginPaneGroup(string id, int count, float spacing = 12f,
            Vector2? padding = null,
            ImGuiChildFlags childFlags = ImGuiChildFlags.None,
            ImGuiWindowFlags windowFlags = ImGuiWindowFlags.None,
            float height = 0f)
        {
            if (count < 1) count = 1;
            var avail = ImGui.GetContentRegionAvail();
            float width = (avail.X - spacing * (count - 1)) / count;
            _paneGroups.Push(new PaneGroupState
            {
                Id = id,
                Count = count,
                Spacing = spacing,
                Index = 0,
                Width = width,
                Padding = padding,
                ChildFlags = childFlags,
                WindowFlags = windowFlags,
                Height = height <= 0 ? avail.Y : height
            });
        }

        public static bool BeginPane(string localId, bool showHeader = true)
        {
            var g = _paneGroups.Peek();
            if (g.Index > 0)
                ImGui.SameLine(0, g.Spacing);

            string outerId = $"{g.Id}_{g.Index}_{localId}_OUTER";
            bool outerOpen = ImGui.BeginChild(outerId, new Vector2(g.Width, g.Height), ImGuiChildFlags.None);
            if (!outerOpen)
            {
                ImGui.EndChild();
                _paneInnerStack.Push(false);
                return false;
            }

            var style = ImGui.GetStyle();
            float padX = (g.Padding?.X ?? style.WindowPadding.X);
            float padY = (g.Padding?.Y ?? style.WindowPadding.Y);

            float headerHeight = 0f;
            if (showHeader)
            {
                string headerText = localId;
                float textHeight = ImGui.GetTextLineHeight();
                headerHeight = textHeight + style.FramePadding.Y * 2f;

                Vector2 headerMin = ImGui.GetCursorScreenPos();

                Vector2 headerMax = new Vector2(headerMin.X + g.Width, headerMin.Y + headerHeight);

                var drawList = ImGui.GetWindowDrawList();
                uint bgCol = ImGui.GetColorU32(ImGuiCol.FrameBg);
                uint borderCol = ImGui.GetColorU32(ImGuiCol.Border);
                drawList.AddRectFilled(headerMin, headerMax, bgCol, style.ChildRounding);
                drawList.AddLine(new Vector2(headerMin.X, headerMax.Y - 1), new Vector2(headerMax.X, headerMax.Y - 1), borderCol);
                drawList.AddRect(headerMin, headerMax, borderCol, style.ChildRounding);

                ImGui.SetCursorPos(new Vector2(padX, style.FramePadding.Y));
                ImGui.TextUnformatted(headerText);
            }

            float innerHeight = g.Height - headerHeight;
            if (innerHeight < 0) innerHeight = 0;
            ImGui.SetCursorPos(new Vector2(0, headerHeight));

            bool pushedPad = false;
            if (g.Padding.HasValue)
            {
                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, g.Padding.Value);
                pushedPad = true;
            }

            bool innerOpen = ImGui.BeginChild($"{outerId}_INNER", new Vector2(0, innerHeight), ImGuiChildFlags.AlwaysUseWindowPadding);
            _paneInnerStack.Push(pushedPad);
            return innerOpen;
        }

        public static void EndPane()
        {
            var g = _paneGroups.Peek();
            bool hadPad = _paneInnerStack.Pop();
            ImGui.EndChild(); // inner
            if (hadPad) ImGui.PopStyleVar();
            ImGui.EndChild(); // outer
            g.Index++;
        }

        public static void EndPaneGroup()
        {
            var g = _paneGroups.Pop();
            g.Index = g.Count;
        }
    }
}