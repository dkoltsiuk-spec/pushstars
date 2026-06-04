using UnityEngine;

namespace PushStars.UI
{
    /// <summary>
    /// Workaround for a Unity Device Simulator rendering bug
    /// (com.unity.device-simulator) where Screen Space - Overlay canvases are
    /// rendered rotated 180° inside the Simulator view. Attach this component
    /// to a full-stretch "MirrorRoot" RectTransform that sits between the
    /// Canvas and all of its UI content.
    ///
    /// When the editor is rendering through the Device Simulator we rotate
    /// the host RectTransform by 180° around the Z axis, which cancels the
    /// simulator's flip. In the regular Game view and in real device builds
    /// nothing is changed.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public class DeviceSimulatorMirrorFix : MonoBehaviour
    {
        private RectTransform _rt;
        private bool          _loggedOnce;

        private void OnEnable()
        {
            _rt = GetComponent<RectTransform>();
            Apply();
        }

        private void Update() => Apply();

        private void Apply()
        {
            if (_rt == null) _rt = GetComponent<RectTransform>();

            bool flip = IsRunningInsideDeviceSimulator();

#if UNITY_EDITOR
            if (!_loggedOnce)
            {
                Debug.Log($"[DeviceSimulatorMirrorFix] flip={flip} " +
                          $"Device.platform={UnityEngine.Device.Application.platform} " +
                          $"App.platform={Application.platform}", this);
                _loggedOnce = true;
            }
#endif

            // Restore identity scale (in case an earlier version of this
            // component left it at -1) and use Z-rotation instead — a 180°
            // rotation around Z cancels both the X and Y flip the simulator
            // applies, while keeping the layout system happy.
            var s = _rt.localScale;
            if (s.x < 0 || s.y < 0)
            {
                s.x            = Mathf.Abs(s.x);
                s.y            = Mathf.Abs(s.y);
                _rt.localScale = s;
            }

            float targetZ  = flip ? 180f : 0f;
            var   euler    = _rt.localEulerAngles;
            float currentZ = Mathf.Repeat(euler.z + 360f, 360f);
            if (Mathf.Abs(Mathf.DeltaAngle(currentZ, targetZ)) > 0.5f)
            {
                _rt.localRotation = Quaternion.Euler(0f, 0f, targetZ);
            }
        }

        /// <summary>
        /// Detects if the editor is currently rendering through the Device
        /// Simulator. Returns false in builds and in the regular Game view.
        /// </summary>
        private static bool IsRunningInsideDeviceSimulator()
        {
#if UNITY_EDITOR
            try
            {
                return UnityEngine.Device.Application.platform != Application.platform;
            }
            catch
            {
                return false;
            }
#else
            return false;
#endif
        }
    }
}
