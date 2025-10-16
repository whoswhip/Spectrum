using ImGuiNET;
using OpenCvSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Numerics;

namespace Spectrum
{
    partial class Renderer
    {
        private bool _showHSVRangeWindow = false;
        private Mat? _originalImage = null;
        private Mat? _maskedImage = null;
        private Image<Rgba32>? _originalImageSharp = null;
        private Image<Rgba32>? _maskedImageSharp = null;
        private IntPtr _originalTextureId = IntPtr.Zero;
        private IntPtr _maskedTextureId = IntPtr.Zero;
        private Scalar _lowerHSV = new Scalar(0, 0, 0);
        private Scalar _upperHSV = new Scalar(179, 255, 255);
        private string _loadedImagePath = "";
        private bool _needsTextureUpdate = false;
        private string _colorName = "";

        private int _maskDisplayMode = 0;
        private Vector3 _maskTintColor = new Vector3(1.0f, 1.0f, 1.0f);
        private float _maskOpacity = 1.0f;
        private Vector3 _backgroundColor = new Vector3(0.0f, 0.0f, 0.0f);
        private bool _showOnlyMaskedRegion = true;

        void RenderHSVRangeWindow()
        {
            if (!_showHSVRangeWindow)
                return;

            ImGui.SetNextWindowSize(new Vector2(900, 700), ImGuiCond.FirstUseEver);
            ImGui.Begin("HSV Range Selector", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar);

            ImGui.Text("HSV Range Selector");
            ImGui.SameLine();
            float currentWidth = ImGui.GetWindowWidth();
            ImGui.SetCursorPosX(currentWidth - 30);
            if (ImGui.Button("X##HSVClose", new Vector2(22, 22)))
            {
                _showHSVRangeWindow = false;
            }

            if (ImGui.Button("Load Image"))
            {
                OpenImageFileDialog();
            }

            if (!string.IsNullOrEmpty(_loadedImagePath))
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"Loaded: {Path.GetFileName(_loadedImagePath)}");
            }

            ImGui.Separator();

            if (_originalImage != null && !_originalImage.Empty())
            {
                float availWidth = ImGui.GetContentRegionAvail().X;
                float availHeight = ImGui.GetContentRegionAvail().Y;
                
                float controlsHeight = 500;
                float maxImageHeight = Math.Max(100, availHeight - controlsHeight);
                
                float imageDisplayWidth = (availWidth - 20) / 2;
                float aspectRatio = (float)_originalImage.Height / _originalImage.Width;
                float imageDisplayHeight = Math.Min(imageDisplayWidth * aspectRatio, maxImageHeight);
                imageDisplayWidth = imageDisplayHeight / aspectRatio;

                if (_needsTextureUpdate)
                {
                    UpdateTextures();
                    _needsTextureUpdate = false;
                }

                ImGui.BeginGroup();
                ImGui.Text("Original Image");
                if (_originalTextureId != IntPtr.Zero)
                {
                    ImGui.Image(_originalTextureId, new Vector2(imageDisplayWidth, imageDisplayHeight));
                }
                ImGui.EndGroup();

                ImGui.SameLine();

                ImGui.BeginGroup();
                ImGui.Text("Masked Image");
                if (_maskedTextureId != IntPtr.Zero)
                {
                    ImGui.Image(_maskedTextureId, new Vector2(imageDisplayWidth, imageDisplayHeight));
                }
                ImGui.EndGroup();

                ImGui.Separator();

                if (ImGui.CollapsingHeader("Visual Settings", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    bool visualChanged = false;

                    ImGui.Text("Display Mode");
                    if (ImGui.RadioButton("Masked Result", ref _maskDisplayMode, 0))
                        visualChanged = true;
                    ImGui.SameLine();
                    if (ImGui.RadioButton("Mask Only", ref _maskDisplayMode, 1))
                        visualChanged = true;
                    ImGui.SameLine();
                    if (ImGui.RadioButton("Overlay", ref _maskDisplayMode, 2))
                        visualChanged = true;

                    ImGui.Spacing();

                    if (_maskDisplayMode == 1 || _maskDisplayMode == 2)
                    {
                        if (ImGui.ColorEdit3("Mask Tint", ref _maskTintColor))
                            visualChanged = true;
                    }

                    if (_maskDisplayMode == 2)
                    {
                        if (ImGui.SliderFloat("Mask Opacity", ref _maskOpacity, 0.0f, 1.0f, "%.2f"))
                            visualChanged = true;
                    }

                    if (_maskDisplayMode == 0)
                        if (ImGui.Checkbox("Show Only Masked Region", ref _showOnlyMaskedRegion))
                            visualChanged = true;

                    if (!_showOnlyMaskedRegion && _maskDisplayMode == 0)
                    {
                        ImGui.Indent();
                        if (ImGui.ColorEdit3("Background Color", ref _backgroundColor))
                            visualChanged = true;
                        ImGui.Unindent();
                    }

                    if (visualChanged)
                    {
                        UpdateMask();
                    }
                }

                ImGui.Separator();

                bool changed = false;

                ImGui.Text("Lower HSV Bound");

                int lowerH = (int)_lowerHSV.Val0;
                int lowerS = (int)_lowerHSV.Val1;
                int lowerV = (int)_lowerHSV.Val2;

                ImGui.PushItemWidth(-1);
                if (ImGui.SliderInt("##LowerH", ref lowerH, 0, 179, "H: %d"))
                    changed = true;
                if (ImGui.SliderInt("##LowerS", ref lowerS, 0, 255, "S: %d"))
                    changed = true;
                if (ImGui.SliderInt("##LowerV", ref lowerV, 0, 255, "V: %d"))
                    changed = true;
                ImGui.PopItemWidth();

                ImGui.Spacing();
                ImGui.Text("Upper HSV Bound");

                int upperH = (int)_upperHSV.Val0;
                int upperS = (int)_upperHSV.Val1;
                int upperV = (int)_upperHSV.Val2;

                ImGui.PushItemWidth(-1);
                if (ImGui.SliderInt("##UpperH", ref upperH, 0, 179, "H: %d"))
                    changed = true;
                if (ImGui.SliderInt("##UpperS", ref upperS, 0, 255, "S: %d"))
                    changed = true;
                if (ImGui.SliderInt("##UpperV", ref upperV, 0, 255, "V: %d"))
                    changed = true;
                ImGui.PopItemWidth();

                if (changed)
                {
                    _lowerHSV = new Scalar(
                        Math.Clamp(lowerH, 0, 179),
                        Math.Clamp(lowerS, 0, 255),
                        Math.Clamp(lowerV, 0, 255)
                    );

                    _upperHSV = new Scalar(
                        Math.Clamp(upperH, 0, 179),
                        Math.Clamp(upperS, 0, 255),
                        Math.Clamp(upperV, 0, 255)
                    );

                    UpdateMask();
                }

                ImGui.Separator();
                ImGui.Spacing();
                ImGui.Text("Save as New Color");
                ImGui.PushItemWidth(-1);
                ImGui.InputText("##ColorName", ref _colorName, 256);
                ImGui.PopItemWidth();
                ImGui.Spacing();

                if (ImGui.Button("Create Color", new Vector2(-1, 0)))
                {
                    if (!string.IsNullOrWhiteSpace(_colorName))
                    {
                        var existingColor = Program.colorConfig.Data.Colors.Find(c => c.Name == _colorName);
                        if (existingColor != null)
                        {
                            existingColor.Lower = _lowerHSV;
                            existingColor.Upper = _upperHSV;
                            LogManager.Log($"Updated existing color: {_colorName}", LogManager.LogLevel.Info);
                        }
                        else
                        {
                            Program.colorConfig.Data.Colors.Add(new ColorInfo(_colorName, _upperHSV, _lowerHSV));
                        }
                        Program.colorConfig.SaveConfig();
                        _colorName = "";
                    }
                    else
                    {
                        LogManager.Log("Please enter a color name", LogManager.LogLevel.Warning);
                    }
                }
            }

            ImGui.End();
        }

        private void OpenImageFileDialog()
        {
            Thread thread = new Thread(() =>
            {
                using (OpenFileDialog openFileDialog = new OpenFileDialog())
                {
                    openFileDialog.Filter = "Image Files|*.jpg;*.jpeg;*.png|All Files|*.*";
                    openFileDialog.Title = "Select an Image";
                    openFileDialog.Multiselect = false;

                    var windowWrapper = new WindowWrapper(window.Handle);
                    if (openFileDialog.ShowDialog(windowWrapper) == DialogResult.OK)
                    {
                        LoadImage(openFileDialog.FileName);
                    }
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private class WindowWrapper : IWin32Window
        {
            private readonly IntPtr _handle;

            public WindowWrapper(IntPtr handle)
            {
                _handle = handle;
            }

            public IntPtr Handle => _handle;
        }

        private void LoadImage(string path)
        {
            try
            {
                DisposeImageResources();

                _originalImage = Cv2.ImRead(path, ImreadModes.Color);

                if (_originalImage == null || _originalImage.Empty())
                {
                    LogManager.Log($"Failed to load image: {path}");
                    return;
                }

                _loadedImagePath = path;
                UpdateMask();
                _needsTextureUpdate = true;

                LogManager.Log($"Loaded image: {path} ({_originalImage.Width}x{_originalImage.Height})", LogManager.LogLevel.Debug);
            }
            catch (Exception ex)
            {
                LogManager.Log($"Error loading image: {ex.Message}");
            }
        }

        private void UpdateMask()
        {
            if (_originalImage == null || _originalImage.Empty())
                return;

            try
            {
                using (Mat hsvImage = new Mat())
                {
                    Cv2.CvtColor(_originalImage, hsvImage, ColorConversionCodes.BGR2HSV);

                    using (Mat mask = new Mat())
                    {
                        Cv2.InRange(hsvImage, _lowerHSV, _upperHSV, mask);

                        _maskedImage?.Dispose();
                        _maskedImage = new Mat();

                        switch (_maskDisplayMode)
                        {
                            case 0:
                                if (_showOnlyMaskedRegion)
                                {
                                    Cv2.BitwiseAnd(_originalImage, _originalImage, _maskedImage, mask);
                                }
                                else
                                {
                                    using (Mat background = new Mat(_originalImage.Size(), _originalImage.Type(),
                                        new Scalar(_backgroundColor.Z * 255, _backgroundColor.Y * 255, _backgroundColor.X * 255)))
                                    {
                                        using (Mat maskedForeground = new Mat())
                                        {
                                            Cv2.BitwiseAnd(_originalImage, _originalImage, maskedForeground, mask);

                                            using (Mat invertedMask = new Mat())
                                            {
                                                Cv2.BitwiseNot(mask, invertedMask);
                                                using (Mat maskedBackground = new Mat())
                                                {
                                                    Cv2.BitwiseAnd(background, background, maskedBackground, invertedMask);
                                                    Cv2.Add(maskedForeground, maskedBackground, _maskedImage);
                                                }
                                            }
                                        }
                                    }
                                }
                                break;

                            case 1:
                                using (Mat coloredMask = new Mat(_originalImage.Size(), _originalImage.Type()))
                                {
                                    Cv2.CvtColor(mask, coloredMask, ColorConversionCodes.GRAY2BGR);

                                    using (Mat tintMat = new Mat(_originalImage.Size(), MatType.CV_32FC3,
                                        new Scalar(_maskTintColor.Z, _maskTintColor.Y, _maskTintColor.X)))
                                    {
                                        using (Mat coloredMaskFloat = new Mat())
                                        using (Mat tinted = new Mat())
                                        {
                                            coloredMask.ConvertTo(coloredMaskFloat, MatType.CV_32FC3, 1.0 / 255.0);
                                            Cv2.Multiply(coloredMaskFloat, tintMat, tinted);
                                            tinted.ConvertTo(_maskedImage, _originalImage.Type(), 255.0);
                                        }
                                    }
                                }
                                break;

                            case 2:
                                using (Mat maskedResult = new Mat())
                                {
                                    Cv2.BitwiseAnd(_originalImage, _originalImage, maskedResult, mask);

                                    using (Mat maskColored = new Mat(_originalImage.Size(), _originalImage.Type()))
                                    {
                                        Cv2.CvtColor(mask, maskColored, ColorConversionCodes.GRAY2BGR);

                                        using (Mat tintMat = new Mat(_originalImage.Size(), MatType.CV_32FC3,
                                            new Scalar(_maskTintColor.Z * _maskOpacity, _maskTintColor.Y * _maskOpacity, _maskTintColor.X * _maskOpacity)))
                                        {
                                            using (Mat maskColoredFloat = new Mat())
                                            using (Mat tinted = new Mat())
                                            using (Mat tintedByte = new Mat())
                                            {
                                                maskColored.ConvertTo(maskColoredFloat, MatType.CV_32FC3, 1.0 / 255.0);
                                                Cv2.Multiply(maskColoredFloat, tintMat, tinted);
                                                tinted.ConvertTo(tintedByte, _originalImage.Type(), 255.0);

                                                using (Mat originalFloat = new Mat())
                                                using (Mat tintedFloat = new Mat())
                                                using (Mat blended = new Mat())
                                                {
                                                    _originalImage.ConvertTo(originalFloat, MatType.CV_32FC3);
                                                    tintedByte.ConvertTo(tintedFloat, MatType.CV_32FC3);
                                                    Cv2.AddWeighted(originalFloat, 1.0 - _maskOpacity, tintedFloat, _maskOpacity, 0, blended);
                                                    blended.ConvertTo(_maskedImage, _originalImage.Type());
                                                }
                                            }
                                        }
                                    }
                                }
                                break;
                        }
                    }
                }

                _needsTextureUpdate = true;
            }
            catch (Exception ex)
            {
                LogManager.Log($"Error updating mask: {ex.Message}");
            }
        }

        private void UpdateTextures()
        {
            try
            {
                if (_originalImage != null && !_originalImage.Empty())
                {
                    _originalImageSharp?.Dispose();
                    _originalImageSharp = MatToImageSharp(_originalImage);

                    if (_originalTextureId != IntPtr.Zero)
                        RemoveImage("hsv_original");

                    AddOrGetImagePointer("hsv_original", _originalImageSharp, false, out _originalTextureId);
                }

                if (_maskedImage != null && !_maskedImage.Empty())
                {
                    _maskedImageSharp?.Dispose();
                    _maskedImageSharp = MatToImageSharp(_maskedImage);

                    if (_maskedTextureId != IntPtr.Zero)
                        RemoveImage("hsv_masked");

                    AddOrGetImagePointer("hsv_masked", _maskedImageSharp, false, out _maskedTextureId);
                }
            }
            catch (Exception ex)
            {
                LogManager.Log($"Error updating textures: {ex.Message}");
            }
        }

        private Image<Rgba32> MatToImageSharp(Mat mat)
        {
            Mat rgbaMat = new Mat();
            Cv2.CvtColor(mat, rgbaMat, ColorConversionCodes.BGR2RGBA);

            int width = rgbaMat.Width;
            int height = rgbaMat.Height;

            var config = new SixLabors.ImageSharp.Configuration();
            config.PreferContiguousImageBuffers = true;
            var image = new Image<Rgba32>(config, width, height);

            if (image.DangerousTryGetSinglePixelMemory(out Memory<Rgba32> memory))
            {
                unsafe
                {
                    byte* matData = (byte*)rgbaMat.Data;
                    var span = memory.Span;

                    fixed (Rgba32* destPtr = &span[0])
                    {
                        byte* dest = (byte*)destPtr;
                        int totalBytes = width * height * 4;
                        Buffer.MemoryCopy(matData, dest, totalBytes, totalBytes);
                    }
                }
            }
            else
            {
                unsafe
                {
                    byte* matData = (byte*)rgbaMat.Data;

                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int idx = (y * width + x) * 4;
                            image[x, y] = new Rgba32(
                                matData[idx + 0],
                                matData[idx + 1],
                                matData[idx + 2],
                                matData[idx + 3]
                            );
                        }
                    }
                }
            }

            rgbaMat.Dispose();
            return image;
        }

        private void DisposeImageResources()
        {
            _originalImage?.Dispose();
            _originalImage = null;

            _maskedImage?.Dispose();
            _maskedImage = null;

            _originalImageSharp?.Dispose();
            _originalImageSharp = null;

            _maskedImageSharp?.Dispose();
            _maskedImageSharp = null;

            if (_originalTextureId != IntPtr.Zero)
            {
                RemoveImage("hsv_original");
                _originalTextureId = IntPtr.Zero;
            }

            if (_maskedTextureId != IntPtr.Zero)
            {
                RemoveImage("hsv_masked");
                _maskedTextureId = IntPtr.Zero;
            }

            _loadedImagePath = "";
        }
    }
}
