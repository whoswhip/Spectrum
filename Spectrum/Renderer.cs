using ClickableTransparentOverlay;
using ImGuiNET;
using OpenCvSharp;
using Spectrum.Input;
using Spectrum.Input.InputLibraries.Makcu;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Globalization;
using Spectrum.Detection;

namespace Spectrum
{
    class Renderer : Overlay
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
                    ImGui.Text("Enable Aiming");

                    ImGui.SameLine(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(mainConfig.Data.Keybind.ToString()).X);
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
                    if (ImGui.Checkbox("Closest to Mouse", ref closestToMouse))
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
                            if (ImGui.Selectable(type.ToString(), isSelected))
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
                    if (ImGui.Checkbox("Enabled", ref triggerBot))
                    {
                        mainConfig.Data.TriggerBot = triggerBot;
                    }
                    ImGui.SameLine(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(mainConfig.Data.TriggerKey.ToString()).X);
                    if (!_waitingForKeybind.GetValueOrDefault("Trigger Key", false))
                    {
                        if (ImGui.Button($"{mainConfig.Data.TriggerKey.ToString()}###TriggerKeybind"))
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
                    else if (triggerBot)
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
                    }
                    ImGuiExtensions.EndPane();
                }

                ImGuiExtensions.EndPaneGroup();

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Detection"))
            {
                ImGuiExtensions.BeginPaneGroup("Detection Panes", 1, 12f, new Vector2(12, 10), ImGuiChildFlags.AlwaysUseWindowPadding, ImGuiWindowFlags.None, 0f);

                if (ImGuiExtensions.BeginPane("Detection Settings"))
                {
                    int ImageWidth = mainConfig.Data.ImageWidth;
                    if (ImGui.InputInt("Image Width", ref ImageWidth, 1, screenSize.width))
                    {
                        if (ImageWidth < 1)
                            ImageWidth = 1;
                        else if (ImageWidth > screenSize.width)
                            ImageWidth = screenSize.width;
                        mainConfig.Data.ImageWidth = ImageWidth;
                    }

                    int ImageHeight = mainConfig.Data.ImageHeight;
                    if (ImGui.InputInt("Image Height", ref ImageHeight, 1, screenSize.height))
                    {
                        if (ImageHeight < 1)
                            ImageHeight = 1;
                        else if (ImageHeight > screenSize.height)
                            ImageHeight = screenSize.height;
                        mainConfig.Data.ImageHeight = ImageHeight;
                    }

                    Scalar UpperHSV = mainConfig.Data.UpperHSV;
                    Vector3 UpperHSVVec3 = new Vector3((float)UpperHSV.Val0, (float)UpperHSV.Val1, (float)UpperHSV.Val2);
                    if (ImGui.InputFloat3("Upper HSV", ref UpperHSVVec3, "%.0f", ImGuiInputTextFlags.CharsDecimal))
                    {
                        mainConfig.Data.UpperHSV = new Scalar(UpperHSVVec3.X, UpperHSVVec3.Y, UpperHSVVec3.Z);
                    }


                    Scalar LowerHSV = mainConfig.Data.LowerHSV;
                    Vector3 LowerHSVVec3 = new Vector3((float)LowerHSV.Val0, (float)LowerHSV.Val1, (float)LowerHSV.Val2);
                    if (ImGui.InputFloat3("Lower HSV", ref LowerHSVVec3, "%.0f", ImGuiInputTextFlags.CharsDecimal))
                    {
                        mainConfig.Data.LowerHSV = new Scalar(LowerHSVVec3.X, LowerHSVVec3.Y, LowerHSVVec3.Z);
                    }

                    List<ColorInfo> colors = colorConfig.Data.Colors;
                    if (colors.Count != 0)
                    {
                        if (ImGui.BeginCombo("Colors", mainConfig.Data.SelectedColor))
                        {
                            foreach (ColorInfo color in colors)
                            {
                                bool isSelected = (color.Name == mainConfig.Data.SelectedColor);
                                if (ImGui.Selectable(color.Name, isSelected))
                                {
                                    mainConfig.Data.SelectedColor = color.Name;
                                    mainConfig.Data.UpperHSV = color.Upper;
                                    mainConfig.Data.LowerHSV = color.Lower;
                                    ColorName = color.Name;
                                }
                                if (isSelected)
                                {
                                    ImGui.SetItemDefaultFocus();
                                }
                            }
                            ImGui.EndCombo();
                        }
                    }

                    ImGui.InputText("##ColorNameInput", ref ColorName, 32);
                    ImGui.SameLine();
                    if (ImGui.Button("Delete Color"))
                    {
                        if (colors.Any(c => c.Name.Equals(mainConfig.Data.SelectedColor, StringComparison.OrdinalIgnoreCase)))
                        {
                            colors.RemoveAll(c => c.Name.Equals(mainConfig.Data.SelectedColor, StringComparison.OrdinalIgnoreCase));
                            colorConfig.SaveConfig();
                            mainConfig.Data.SelectedColor = "Arsenal [Magenta]";
                            mainConfig.Data.UpperHSV = new Scalar(150, 255, 229);
                            mainConfig.Data.LowerHSV = new Scalar(150, 255, 229);
                        }
                        else
                        {
                            LogManager.Log($"Color '{mainConfig.Data.SelectedColor}' does not exist.", LogManager.LogLevel.Warning);
                        }
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Save Color"))
                    {
                        var color = new ColorInfo(ColorName, mainConfig.Data.UpperHSV, mainConfig.Data.LowerHSV);
                        if (!string.IsNullOrEmpty(ColorName))
                        {
                            colors.Add(color);
                            colorConfig.SaveConfig();
                            mainConfig.Data.SelectedColor = ColorName;
                        }
                        else
                        {
                            LogManager.Log($"Colorname is empty.", LogManager.LogLevel.Warning);
                        }
                    }
                }
                ImGuiExtensions.EndPane();

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
                                            var ok = MakcuMain.Load().GetAwaiter().GetResult();
                                            if (!ok)
                                            {
                                                LogManager.Log("Makcu failed to initialize. Falling back to MouseEvent.", LogManager.LogLevel.Warning);
                                                mainConfig.Data.MovementMethod = MovementMethod.MouseEvent;
                                                MakcuMain.Unload();
                                            }
                                            break;
                                        }
                                    default:
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

                    var _LogEntries = LogManager.LogEntries.ToArray();
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