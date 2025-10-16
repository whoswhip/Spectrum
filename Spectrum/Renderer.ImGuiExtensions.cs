using ImGuiNET;
using OpenCvSharp;
using System.Globalization;
using System.Numerics;
using Spectrum.Input;

namespace Spectrum
{
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

        public static bool SliderFill(string label, ref float value, float min, float max, string format = "%.2f", bool enforceRange = true)
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
                        float clamped;
                        if (enforceRange)
                            clamped = Math.Clamp(typed, min, max);
                        else
                            clamped = typed;
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
            fillWidth = Math.Clamp(fillWidth, 0f, p1.X - p0.X);
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

        private static readonly Dictionary<string, bool> _keybindListening = [];
        private static readonly Dictionary<string, Keys> _keybindPending = [];
        private static readonly Dictionary<string, Task?> _keybindTasks = [];

        public static bool KeybindInput(string label, ref Keybind keybind, bool inline = false)
        {
            bool changed = false;
            ImGui.PushID(label);

            if (!inline)
                ImGui.TextUnformatted(label);

            string buttonText = _keybindListening.GetValueOrDefault(label) 
                ? "Listening..." 
                : keybind.Key.ToString();

            ImGui.SameLine(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(buttonText).X);

            if (!_keybindListening.GetValueOrDefault(label, false))
            {
                if (ImGui.Button($"{buttonText}###{label}_Button"))
                {
                    _keybindListening[label] = true;
                    _keybindPending[label] = Keys.None;
                    _keybindTasks[label] = Task.Run(async () =>
                    {
                        var key = await InputManager.ListenForNextKeyOrMouseAsync();
                        _keybindPending[label] = key;
                    });
                }

                if (ImGui.BeginPopupContextItem($"###{label}_Context"))
                {
                    bool isHold = keybind.Type == KeybindType.Hold;
                    if (ImGui.MenuItem("Hold", null, isHold))
                    {
                        keybind.Type = KeybindType.Hold;
                        changed = true;
                    }

                    bool isToggle = keybind.Type == KeybindType.Toggle;
                    if (ImGui.MenuItem("Toggle", null, isToggle))
                    {
                        keybind.Type = KeybindType.Toggle;
                        changed = true;
                    }

                    bool isAlways = keybind.Type == KeybindType.Always;
                    if (ImGui.MenuItem("Always", null, isAlways))
                    {
                        keybind.Type = KeybindType.Always;
                        changed = true;
                    }

                    ImGui.EndPopup();
                }
            }
            else
            {
                ImGui.Button($"{buttonText}###{label}_Listening");
                if (_keybindPending.GetValueOrDefault(label, Keys.None) != Keys.None)
                {
                    keybind.Key = _keybindPending[label];
                    _keybindPending[label] = Keys.None;
                    _keybindListening[label] = false;
                    changed = true;
                }
            }

            ImGui.PopID();
            return changed;
        }
    }
}
