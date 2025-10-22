using System.Runtime.InteropServices;

namespace Spectrum
{
    class Win32
    {
        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;
        public static bool IsWindowHidden = false;

        public static (int Width, int Height) GetPrimaryScreenSize()
        {
            return (GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));
        }

        public static (int X, int Y, int Width, int Height) GetVirtualScreenBounds()
        {
            return (
                GetSystemMetrics(SM_XVIRTUALSCREEN),
                GetSystemMetrics(SM_YVIRTUALSCREEN),
                GetSystemMetrics(SM_CXVIRTUALSCREEN),
                GetSystemMetrics(SM_CYVIRTUALSCREEN)
            );
        }
        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

        private const uint WDA_NONE = 0x00000000;
        private const uint WDA_MONITOR = 0x00000001;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        public static bool EnableAntiCapture(IntPtr windowHandle)
        {
            IsWindowHidden = true;
            return SetWindowDisplayAffinity(windowHandle, WDA_EXCLUDEFROMCAPTURE);
        }

        public static bool DisableAntiCapture(IntPtr windowHandle)
        {
            IsWindowHidden = false;
            return SetWindowDisplayAffinity(windowHandle, WDA_NONE);
        }
    }
}
