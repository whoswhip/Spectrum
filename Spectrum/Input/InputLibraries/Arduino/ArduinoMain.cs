using System.Diagnostics;
using System.IO.Ports;
using LogLevel = Spectrum.LogManager.LogLevel;

namespace Spectrum.Input.InputLibraries.Arduino
{
    class ArduinoMain
    {
        private static readonly byte PingCommand = 0x02;
        private static readonly byte PingResponse = 0xAA;
        private const int BaudRate = 115200;
        private const int HandshakeTimeoutMs = 1500;
        private const int OpenResetDelayMs = 1200;
        private static SerialPort? serial;
        private static readonly object sync = new();

        public static bool Load()
        {
            lock (sync)
            {
                if (serial != null && serial.IsOpen)
                    return true;

                serial = FindArduino();
                if (serial != null)
                {
                    LogManager.Log($"ArduinoMain: Connected to Arduino on {serial.PortName}.", LogLevel.Info);
                    return true;
                }

                LogManager.Log("ArduinoMain: Failed to connect to Arduino.", LogLevel.Warning);
                return false;
            }
        }

        private static SerialPort? FindArduino()
        {
            string[] candidates = SerialPort.GetPortNames();

            if (candidates.Length == 0)
            {
                LogManager.Log("ArduinoMain: No serial ports detected.", LogLevel.Debug);
            }

            foreach (string portName in candidates)
            {
                try
                {
                    LogManager.Log($"ArduinoMain: Probing {portName}…", LogLevel.Debug);
                    var sp = new SerialPort(portName, BaudRate)
                    {
                        ReadTimeout = 250,
                        WriteTimeout = 500,
                        DtrEnable = true,
                        RtsEnable = true
                    };

                    sp.Open();

                    Thread.Sleep(OpenResetDelayMs);

                    sp.DiscardInBuffer();
                    sp.DiscardOutBuffer();

                    sp.Write([PingCommand], 0, 1);

                    if (AwaitResponse(sp, PingResponse, HandshakeTimeoutMs))
                    {
                        LogManager.Log($"ArduinoMain: Handshake success on {portName}.", LogLevel.Debug);
                        return sp;
                    }

                    LogManager.Log($"ArduinoMain: No valid response on {portName}.", LogLevel.Debug);
                    sp.Close();
                    sp.Dispose();
                }
                catch (Exception ex)
                {
                    LogManager.Log($"ArduinoMain: Probe failed on {portName}: {ex.Message}", LogLevel.Debug);
                }
            }

            return null;
        }

        private static bool AwaitResponse(SerialPort sp, byte expected, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                try
                {
                    while (sp.BytesToRead > 0)
                    {
                        int b = sp.ReadByte();
                        if (b < 0) break;
                        LogManager.Log($"ArduinoMain: Received 0x{b:X2} (expect 0x{expected:X2}).", LogLevel.Debug);
                        if (b == expected)
                            return true;
                    }
                }
                catch (TimeoutException) { }
                Thread.Sleep(10);
            }
            return false;
        }

        public static void Close()
        {
            lock (sync)
            {
                if (serial != null)
                {
                    try
                    {
                        serial.Close();
                        serial.Dispose();
                        LogManager.Log("ArduinoMain: Serial port closed.", LogLevel.Debug);
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log($"ArduinoMain: Error closing port: {ex.Message}", LogLevel.Debug);
                    }
                    finally
                    {
                        serial = null;
                    }
                }
            }
        }

        public static void Move(int x, int y)
        {
            SerialPort? sp;
            lock (sync) sp = serial;
            if (sp == null || !sp.IsOpen) return;

            while (x != 0 || y != 0)
            {
                int stepX = Math.Max(-127, Math.Min(127, x));
                int stepY = Math.Max(-127, Math.Min(127, y));
                byte bX = unchecked((byte)stepX);
                byte bY = unchecked((byte)stepY);

                var packet = new byte[] { 0x01, bX, bY };
                try
                {
                    sp.Write(packet, 0, packet.Length);
                }
                catch (Exception ex)
                {
                    LogManager.Log($"ArduinoMain: Move write failed: {ex.Message}", LogLevel.Debug);
                    return;
                }

                x -= stepX;
                y -= stepY;
            }
        }
        public static void ClickDown(int button = 0x03)
        {
            SendClick(button, true);
        }

        public static void ClickUp(int button = 0x03)
        {
            SendClick(button, false);
        }

        private static void SendClick(int buttonBase, bool expectDown)
        {
            SerialPort? sp;
            lock (sync) sp = serial;
            if (sp == null || !sp.IsOpen) return;

            if (buttonBase != 0x03 && buttonBase != 0x05)
                throw new ArgumentException("Button must be 0x03 (left) or 0x05 (right).", nameof(buttonBase));

            byte code = (byte)(expectDown ? buttonBase : (buttonBase + 1));

            try
            {
                sp.Write([code], 0, 1);
            }
            catch (Exception ex)
            {
                LogManager.Log($"ArduinoMain: Click {(expectDown ? "down" : "up")} failed: {ex.Message}", LogLevel.Debug);
            }
        }
    }
}