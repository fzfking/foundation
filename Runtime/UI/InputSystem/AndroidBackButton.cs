using System;
using Cysharp.Threading.Tasks;
using Foundation.Lifecycle;
using Foundation.Logging;
using UnityEngine.InputSystem;

namespace Foundation.UI
{
    /// <summary>
    /// Sends the Android back button to the popup stack of the context it lives in. The top popup decides what Back does:
    /// it closes, it presses the button that dismisses it, or it stays open when it must not be closed by Back. With no
    /// popup open the press is left to the platform, so the boot curtain and the required-update panel keep the default.
    /// </summary>
    public sealed class AndroidBackButton : ITickableEntryPoint
    {
        private readonly IUIRouter _popups;
        private readonly IAppLogger _logger;
        private bool _handling;

        public AndroidBackButton(IUIRouter popups, IAppLogger logger = null)
        {
            _popups = popups ?? throw new ArgumentNullException(nameof(popups));
            _logger = logger;
        }

        public void Tick()
        {
            // The Android back button arrives as the Escape key of the Input System keyboard.
            if (_handling || Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame)
                return;

            HandleAsync().Forget();
        }

        private async UniTaskVoid HandleAsync()
        {
            _handling = true;
            try
            {
                await _popups.BackAsync();
            }
            catch (ObjectDisposedException)
            {
                // The context is leaving and took its popups with it.
            }
            catch (Exception exception)
            {
                _logger?.Log(LogLevel.Warning, "Back", $"Back failed: {exception.Message}");
            }
            finally
            {
                _handling = false;
            }
        }
    }
}
