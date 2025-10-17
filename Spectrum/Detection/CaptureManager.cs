using System.Drawing.Imaging;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Drawing.Size;

namespace Spectrum.Detection
{
    public class CaptureManager : IDisposable
    {
        private readonly object _sync = new();
        private ID3D11Device? _device;
        private ID3D11DeviceContext? _context;
        private IDXGIOutputDuplication? _duplication;
        private ID3D11Texture2D? _stagingTex;
        private Size _desktopSize;
        public bool IsDirectXAvailable { get; private set; }
        public bool IsInitialized => _device != null && _duplication != null && _stagingTex != null;

        public bool TryInitialize(int adapterIndex = 0, int outputIndex = 0)
        {
            lock (_sync)
            {
                try
                {
                    DisposeDxgiResources();

                    using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
                    if (factory.EnumAdapters1(adapterIndex, out var adapter).Failure)
                        return false;
                    if (adapter.EnumOutputs(outputIndex, out var output).Failure)
                    {
                        adapter.Dispose();
                        return false;
                    }

                    var hr = D3D11.D3D11CreateDevice(
                        adapter,
                        DriverType.Unknown,
                        DeviceCreationFlags.BgraSupport,
                        [
                            FeatureLevel.Level_12_1,
                            FeatureLevel.Level_12_0,
                            FeatureLevel.Level_11_1,
                            FeatureLevel.Level_11_0
                        ],
                        out _device);

                    if (hr.Failure || _device == null)
                    {
                        output.Dispose();
                        adapter.Dispose();
                        return false;
                    }

                    _context = _device.ImmediateContext;

                    _duplication = output.QueryInterface<IDXGIOutput1>().DuplicateOutput(_device);
                    var desc = output.Description;
                    _desktopSize = new Size(desc.DesktopCoordinates.Right - desc.DesktopCoordinates.Left,
                                            desc.DesktopCoordinates.Bottom - desc.DesktopCoordinates.Top);

                    output.Dispose();
                    adapter.Dispose();

                    EnsureStagingTexture(_desktopSize);

                    IsDirectXAvailable = true;
                    return true;
                }
                catch
                {
                    IsDirectXAvailable = false;
                    DisposeDxgiResources();
                    return false;
                }
            }
        }

        public Bitmap? CaptureScreenshotDirectX(Rectangle bounds)
        {
            lock (_sync)
            {
                if (!IsInitialized)
                {
                    if (!TryInitialize())
                        return null;
                }

                bounds = Rectangle.Intersect(new Rectangle(Point.Empty, _desktopSize), bounds);
                if (bounds.Width <= 0 || bounds.Height <= 0) return null;

                EnsureStagingTexture(_desktopSize);

                IDXGIResource? desktopResource = null;
                bool frameAcquired = false;
                try
                {
                    var result = _duplication!.AcquireNextFrame(16, out var frameInfo, out desktopResource);
                    if (result.Failure)
                    {
                        if (result.Code == unchecked((int)Vortice.DXGI.ResultCode.AccessLost) ||
                            result.Code == unchecked((int)Vortice.DXGI.ResultCode.DeviceRemoved))
                        {
                            TryInitialize();
                        }
                        return null;
                    }
                    frameAcquired = true;

                    using var fullTex = desktopResource.QueryInterface<ID3D11Texture2D>();

                    _context!.CopySubresourceRegion(
                        _stagingTex!,
                        0,
                        bounds.X,
                        bounds.Y,
                        0,
                        fullTex,
                        0,
                        new Vortice.Mathematics.Box(bounds.X, bounds.Y, 0, bounds.Right, bounds.Bottom, 1)
                    );

                    var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                    var map = _context.Map(_stagingTex!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                    var bmpData = bmp.LockBits(new Rectangle(0, 0, bounds.Width, bounds.Height),
                                               ImageLockMode.WriteOnly,
                                               PixelFormat.Format32bppArgb);

                    try
                    {
                        unsafe
                        {
                            byte* srcBase = (byte*)map.DataPointer + bounds.Y * map.RowPitch + bounds.X * 4;
                            int srcStride = map.RowPitch;
                            int dstStride = bmpData.Stride;
                            int rowBytes = bounds.Width * 4;

                            byte* dst = (byte*)bmpData.Scan0;
                            byte* src = srcBase;
                            for (int y = 0; y < bounds.Height; y++)
                            {
                                Buffer.MemoryCopy(src, dst, rowBytes, rowBytes);
                                src += srcStride;
                                dst += dstStride;
                            }
                        }
                    }
                    finally
                    {
                        bmp.UnlockBits(bmpData);
                        _context.Unmap(_stagingTex!, 0);
                    }

                    return bmp;
                }
                finally
                {
                    desktopResource?.Dispose();
                    if (frameAcquired)
                    {
                        try { _duplication?.ReleaseFrame(); } catch { }
                    }
                }
            }
        }

        private void EnsureStagingTexture(Size desktopSize)
        {
            if (_device == null) return;
            if (_stagingTex != null &&
                _stagingTex.Description.Width == desktopSize.Width &&
                _stagingTex.Description.Height == desktopSize.Height)
                return;

            _stagingTex?.Dispose();
            _stagingTex = _device.CreateTexture2D(new Texture2DDescription
            {
                Width = desktopSize.Width,
                Height = desktopSize.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new(1, 0),
                Usage = ResourceUsage.Staging,
                CPUAccessFlags = CpuAccessFlags.Read,
                BindFlags = BindFlags.None
            });
        }

        public Bitmap? CaptureScreenshotGdi(Rectangle bounds)
        {
            var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            return bmp;
        }
        public bool TryInitializeDirectX(int adapterIndex = 0, int outputIndex = 0) => TryInitialize(adapterIndex, outputIndex);

        private void DisposeDxgiResources()
        {
            _duplication?.Dispose();
            _stagingTex?.Dispose();
            _context?.Dispose();
            _device?.Dispose();
            _duplication = null;
            _stagingTex = null;
            _context = null;
            _device = null;
            IsDirectXAvailable = false;
        }

        public void Dispose()
        {
            lock (_sync)
            {
                DisposeDxgiResources();
            }
            GC.SuppressFinalize(this);
        }
    }
}