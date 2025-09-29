using LogLevel = Spectrum.LogManager.LogLevel;

namespace Spectrum.Input.InputLibraries.Makcu
{
    internal class MakcuMain
    {
        public static MakcuMouse? MakcuInstance { get; private set; }

        private static bool _isMakcuLoaded = false;

        private static bool _isSubscribedToButtonEvents = false;

        private const bool DefaultDebugLoggingForInternalCreation = false;
        private const bool DefaultSendInitCommandsForInternalCreation = true;

        public static void ConfigureMakcuInstance(bool debugEnabled, bool sendInitCmds)
        {

            LogManager.Log($"MakcuMain: Configuring MakcuInstance. Debug: {debugEnabled}, SendInitCmds: {sendInitCmds}", LogLevel.Debug);
            UnsubscribeFromButtonEvents();
            MakcuInstance?.Dispose();

            MakcuInstance = new MakcuMouse(debugEnabled, sendInitCmds);
            _isMakcuLoaded = false;
        }

        private static async Task<bool> InitializeMakcuDevice()
        {
            if (_isMakcuLoaded && MakcuInstance != null && MakcuInstance.IsInitializedAndConnected)
            {
                LogManager.Log("MakcuMain: InitializeMakcuDevice called, but Makcu is already loaded and connected.", LogLevel.Debug);
                return true;
            }

            if (MakcuInstance == null)
            {
                ConfigureMakcuInstance(true, DefaultSendInitCommandsForInternalCreation);
            }

            try
            {
                if (MakcuInstance == null || !MakcuInstance.Init())
                {
                    _isMakcuLoaded = false;
                    return false;
                }

                string? version = MakcuInstance.GetKmVersion();

                _isMakcuLoaded = true;

                SubscribeToButtonEvents();

                return true;
            }
            catch
            {
                _isMakcuLoaded = false;
                if (MakcuInstance != null && MakcuInstance.IsInitializedAndConnected)
                {
                    MakcuInstance.Close();
                }
                return false;
            }
        }

        public static async Task<bool> Load() => await InitializeMakcuDevice();

        public static void Unload()
        {
            UnsubscribeFromButtonEvents();
            MakcuInstance?.Close();
            _isMakcuLoaded = false;
            LogManager.Log("MakcuMain: Makcu device unloaded/closed.", LogLevel.Debug);
        }

        private static void SubscribeToButtonEvents()
        {
            if (MakcuInstance != null && !_isSubscribedToButtonEvents)
            {
                MakcuInstance.ButtonStateChanged += OnMakcuButtonStateChanged;
                _isSubscribedToButtonEvents = true;
                LogManager.Log("MakcuMain: Subscribed to MakcuInstance.ButtonStateChanged events.", LogLevel.Debug);
            }
        }

        private static void UnsubscribeFromButtonEvents()
        {
            if (MakcuInstance != null && _isSubscribedToButtonEvents)
            {
                MakcuInstance.ButtonStateChanged -= OnMakcuButtonStateChanged;
                _isSubscribedToButtonEvents = false;
                LogManager.Log("MakcuMain: Unsubscribed from MakcuInstance.ButtonStateChanged events.", LogLevel.Debug);
            }
        }

        private static void OnMakcuButtonStateChanged(MakcuMouseButton button, bool isPressed)
        {

            string state = isPressed ? "Pressed" : "Released";
            LogManager.Log($"{button} physical {state}!", LogLevel.Debug);
        }
        public static void DisposeInstance()
        {
            Unload();
            MakcuInstance?.Dispose();
            MakcuInstance = null;
            _isMakcuLoaded = false;
            LogManager.Log("MakcuMain: MakcuMouse instance disposed (null).", LogLevel.Debug);
        }
    }
}
