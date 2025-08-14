using System.Runtime.InteropServices;

namespace Spectrum.Input.InputLibraries.MouseEvent
{
    public class MouseMain
    {
        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

        public static void Move(int x, int y)
        {
            mouse_event(0x0001, (uint)x, (uint)y, 0, 0);
        }

        public static void ClickDown()
        {
            mouse_event(0x0002, 0, 0, 0, 0);
        }
        public static void ClickUp()
        {
            mouse_event(0x0004, 0, 0, 0, 0);
        }
    }
}
