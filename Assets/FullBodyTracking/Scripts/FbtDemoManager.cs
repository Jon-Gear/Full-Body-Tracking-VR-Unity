using System.Collections;
using UnityEngine;
using UnityEngine.XR;

namespace FullBodyTracking
{
    public enum DemoInputMode
    {
        /// <summary>Use the XR headset when one is present, otherwise fall back to desktop simulation.</summary>
        Auto,
        DesktopSimulation,
        XrHeadset
    }

    /// <summary>
    /// Wires the demo scene together at startup: picks XR or simulated head/hands, and toggles the tracker markers.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Full Body Tracking/Demo Manager")]
    public class FbtDemoManager : MonoBehaviour
    {
        [SerializeField] private DemoInputMode inputMode = DemoInputMode.Auto;

        [Tooltip("How long Auto mode waits for an XR headset before using desktop simulation.")]
        [SerializeField, Min(0f)] private float headsetWaitSeconds = 1.5f;

        [Header("Core")]
        [SerializeField] private OscFullBodyIKController controller;
        [SerializeField] private OscTrackerSimulator simulator;

        [Header("XR rig")]
        [SerializeField] private GameObject xrRig;
        [SerializeField] private Transform xrHeadset;
        [SerializeField] private Transform xrLeftController;
        [SerializeField] private Transform xrRightController;

        [Header("Desktop simulation")]
        [SerializeField] private GameObject desktopRig;
        [SerializeField] private Transform simulatedHeadset;
        [SerializeField] private Transform simulatedLeftController;
        [SerializeField] private Transform simulatedRightController;

        [Header("Mode-specific objects")]
        [Tooltip("Only active when an XR headset is used (for example the world-space status label).")]
        [SerializeField] private GameObject[] xrOnlyObjects;
        [SerializeField] private TrackerStatusHud hud;

        [Header("Debug visuals")]
        [SerializeField] private GameObject trackerMarkers;

        public bool UsingXr { get; private set; }

        private bool markersVisible = true;

        public bool MarkersVisible
        {
            get { return markersVisible; }
            set
            {
                markersVisible = value;
                if (trackerMarkers == null) return;
                foreach (Renderer r in trackerMarkers.GetComponentsInChildren<Renderer>(true)) r.enabled = value;
            }
        }

        private IEnumerator Start()
        {
            bool xr;
            switch (inputMode)
            {
                case DemoInputMode.XrHeadset:
                    xr = true;
                    break;
                case DemoInputMode.DesktopSimulation:
                    xr = false;
                    break;
                default:
                    xr = false;
                    float waited = 0f;
                    while (waited < headsetWaitSeconds)
                    {
                        if (HeadsetPresent()) { xr = true; break; }
                        waited += Time.unscaledDeltaTime;
                        yield return null;
                    }
                    break;
            }

            Apply(xr);
        }

        private static bool HeadsetPresent()
        {
            InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            return head.isValid;
        }

        private void Apply(bool xr)
        {
            UsingXr = xr;

            if (xrRig != null) xrRig.SetActive(xr);
            if (desktopRig != null) desktopRig.SetActive(!xr);

            if (xrOnlyObjects != null)
            {
                foreach (GameObject go in xrOnlyObjects) if (go != null) go.SetActive(xr);
            }

            if (hud != null) hud.ShowOnScreen = !xr;

            if (simulator != null)
            {
                // With a real headset the OSC data is expected from real trackers, so stay quiet.
                simulator.Sending = !xr;
            }

            if (controller != null)
            {
                if (xr) controller.SetHeadsetAndControllers(xrHeadset, xrLeftController, xrRightController);
                else controller.SetHeadsetAndControllers(simulatedHeadset, simulatedLeftController, simulatedRightController);
            }
        }
    }
}
