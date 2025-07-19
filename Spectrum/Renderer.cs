using ClickableTransparentOverlay;
using ImGuiNET;
using OpenCvSharp;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;

namespace Spectrum
{
    class Renderer : Overlay
    {
        static (int width, int height) screenSize = SystemHelper.GetPrimaryScreenSize();
        private List<Action<ImDrawListPtr>> drawCommands = new List<Action<ImDrawListPtr>>();
        private readonly object drawCommandsLock = new object();

        private bool _vsync = true;
        private int _fpsLimit = 240;
        private readonly Dictionary<string, bool> _waitingForKeybind = new();
        private readonly Dictionary<string, Keys> _pendingKeybind = new();
        private readonly Dictionary<string, Task?> _keybindTask = new();
        private readonly ConcurrentQueue<Keys> _keybindResults = new();
        private ConfigManager<ConfigData> mainConfig = Program.mainConfig;
        private ConfigManager<ColorData> colorConfig = Program.colorConfig;
        private string ColorName = "New Color";

        public Renderer() : base("Spectrum", screenSize.width, screenSize.width)
        {
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

            ImGui.SetNextWindowSize(new Vector2(600, 500), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowPos(new Vector2(screenSize.width / 2 - 300, screenSize.height / 2 - 250), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSizeConstraints(new Vector2(600, 500), new Vector2(float.MaxValue, float.MaxValue));

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
                bool enableAiming = mainConfig.Data.EnableAim;
                if (ImGui.Checkbox("##Enable Aiming", ref enableAiming))
                {
                    mainConfig.Data.EnableAim = enableAiming;
                }

                ImGui.SameLine();
                if (!_waitingForKeybind.GetValueOrDefault("Aiming", false))
                {
                    if (ImGui.Button(mainConfig.Data.Keybind.ToString()))
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
                    ImGui.Button("Listening...");
                    if (_pendingKeybind.GetValueOrDefault("Aiming", Keys.None) != Keys.None)
                    {
                        mainConfig.Data.Keybind = _pendingKeybind["Aiming"];
                        _pendingKeybind["Aiming"] = Keys.None;
                        _waitingForKeybind["Aiming"] = false;
                    }
                }
                ImGui.SameLine();
                ImGui.Text("Enable Aiming");

                bool closestToMouse = mainConfig.Data.ClosestToMouse;
                if (ImGui.Checkbox("Closest to Mouse", ref closestToMouse))
                {
                    mainConfig.Data.ClosestToMouse = closestToMouse;
                }
                bool triggerBot = mainConfig.Data.TriggerBot;
                if (ImGui.Checkbox("Trigger Bot", ref triggerBot))
                {
                    mainConfig.Data.TriggerBot = triggerBot;
                }

                float sensitivity = (float)mainConfig.Data.Sensitivity;
                if (ImGui.SliderFloat("Sensitivity", ref sensitivity, 0.1f, 2.0f, "%.1f"))
                {
                    mainConfig.Data.Sensitivity = sensitivity;
                }

                float EmaSmootheningFactor = (float)mainConfig.Data.EmaSmootheningFactor;
                bool EmaSmoothening = mainConfig.Data.EmaSmoothening;
                if (ImGui.Checkbox("Ema Smoothening", ref EmaSmoothening))
                {
                    mainConfig.Data.EmaSmoothening = EmaSmoothening;
                }

                if (ImGui.SliderFloat("Ema Smoothening Factor", ref EmaSmootheningFactor, 0.01f, 1.0f, "%.2f"))
                {
                    mainConfig.Data.EmaSmootheningFactor = EmaSmootheningFactor;
                }

                MovementType AimPath = mainConfig.Data.AimMovementType;
                if (ImGui.BeginCombo("Movement Type", AimPath.ToString()))
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

                int YOffset = (int)(mainConfig.Data.YOffsetPercent * 100);
                if (ImGui.SliderInt("Y Offset (%)", ref YOffset, 0, 100, "%d%%"))
                {
                    mainConfig.Data.YOffsetPercent = ((double)YOffset / 100);
                }

                int XOffset = (int)(mainConfig.Data.XOffsetPercent * 100);
                if (ImGui.SliderInt("X Offset (%)", ref XOffset, 0, 100, "%d%%"))
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
                Vector4 FOVColor = mainConfig.Data.FOVColor;
                if (ImGui.ColorEdit4("FOV Color", ref FOVColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
                {
                    mainConfig.Data.FOVColor = FOVColor;
                }
                ImGui.SameLine();
                ImGui.Text("Draw FOV");

                bool DrawDetections = mainConfig.Data.DrawDetections;
                if (ImGui.Checkbox("##Draw Detections", ref DrawDetections))
                {
                    mainConfig.Data.DrawDetections = DrawDetections;
                }
                ImGui.SameLine();
                Vector4 DetectionColor = mainConfig.Data.DetectionColor;
                if (ImGui.ColorEdit4("Draw Color", ref DetectionColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
                {
                    mainConfig.Data.DetectionColor = DetectionColor;
                }
                ImGui.SameLine();
                ImGui.Text("Draw Detections");

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Detection"))
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
                    if (!string.IsNullOrEmpty(ColorName) && !colors.Any(c => c.Name.Equals(ColorName, StringComparison.OrdinalIgnoreCase)))
                    {
                        colors.Add(color);
                        colorConfig.SaveConfig();
                        mainConfig.Data.SelectedColor = ColorName;
                    }
                    else
                    {
                        LogManager.Log($"Color with name '{ColorName}' already exists or is empty.", LogManager.LogLevel.Warning);
                    }
                }

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Settings"))
            {
                bool ShowDetectionWindow = mainConfig.Data.ShowDetectionWindow;
                if (ImGui.Checkbox("Show Detection Window", ref ShowDetectionWindow))
                {
                    mainConfig.Data.ShowDetectionWindow = ShowDetectionWindow;
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

                if (ImGui.SliderInt("FPS Limit", ref _fpsLimit, 30, 480, "%d"))
                {
                    FPSLimit = _fpsLimit;
                }
                int BackgroundImageInterval = mainConfig.Data.BackgroundImageInterval;
                if (ImGui.InputInt("Background Image Interval", ref BackgroundImageInterval, 1, 500))
                {
                    if (BackgroundImageInterval < 1)
                        BackgroundImageInterval = 1;
                    else if (BackgroundImageInterval > 500)
                        BackgroundImageInterval = 500;

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
                ImGui.SeparatorText("Logs");

                ImGui.BeginChild("Logs", new Vector2(0, -34), ImGuiChildFlags.AlwaysUseWindowPadding);

                var _LogEntries = LogManager.LogEntries;
                foreach (var log in _LogEntries)
                {
                    ImGui.Text(log.ToString());
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
                AddRect(new Rectangle(
                    ((screenSize.width - mainConfig.Data.ImageWidth) / 2) - 1,
                    ((screenSize.height - mainConfig.Data.ImageHeight) / 2) - 1,
                    mainConfig.Data.ImageWidth + 2,
                    mainConfig.Data.ImageHeight + 2
                ), mainConfig.Data.FOVColor);
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

        public void AddText(Vector2 pos, Vector4 color, string text)
        {
            if (string.IsNullOrEmpty(text))
                return;
            lock (drawCommandsLock)
            {
                drawCommands.Add(drawList => drawList.AddText(pos, ColorFromVector4(color), text));
            }
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
