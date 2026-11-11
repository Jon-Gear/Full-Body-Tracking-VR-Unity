using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using TMPro;
using UnityEngine;

namespace FullBodyTracking
{
    /// <summary>
    /// Small status panel: where the app listens (so you know which IP/port to give your tracker sender),
    /// which trackers are alive, and a few demo switches. Drawn with IMGUI on desktop and optionally mirrored
    /// to a world-space TextMeshPro label so it is readable inside a headset.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Full Body Tracking/Tracker Status HUD")]
    public class TrackerStatusHud : MonoBehaviour
    {
        [SerializeField] private OscFullBodyIKController controller;
        [SerializeField] private OscTrackerSimulator simulator;
        [SerializeField] private FbtDemoManager demo;

        [Tooltip("Optional 3D TextMeshPro object (not UI) that receives the same text, for use in VR.")]
        [SerializeField] private TMP_Text vrLabel;

        [SerializeField] private bool showOnScreen = true;

        public bool ShowOnScreen { get { return showOnScreen; } set { showOnScreen = value; } }

        private string localIp = "unknown";
        private float messagesPerSecond;
        private long lastCount;
        private float lastSampleTime;
        private float nextLabelUpdate;
        private bool collapsed;
        private GUIStyle textStyle, titleStyle, buttonStyle;

        private void Start()
        {
            localIp = FindLocalIPv4();
            lastSampleTime = Time.unscaledTime;
        }

        private void Update()
        {
            if (controller == null) return;

            float now = Time.unscaledTime;
            if (now - lastSampleTime >= 0.5f)
            {
                long count = controller.MessageCount;
                messagesPerSecond = (count - lastCount) / (now - lastSampleTime);
                lastCount = count;
                lastSampleTime = now;
            }

            if (vrLabel != null && now >= nextLabelUpdate)
            {
                nextLabelUpdate = now + 0.25f;
                vrLabel.text = BuildStatusText();
            }
        }

        private string BuildStatusText()
        {
            var sb = new StringBuilder(256);
            if (controller.ServerError != null)
            {
                sb.AppendLine("<color=#FF6B6B>" + controller.ServerError + "</color>");
            }
            else
            {
                sb.AppendLine("Listening on <b>" + localIp + ":" + controller.ListenPort + "</b> (UDP)");
            }

            string source = demo != null && demo.UsingXr ? "XR headset" : "desktop simulation";
            sb.AppendLine("Input: " + source + "    " + messagesPerSecond.ToString("0") + " msg/s");
            sb.AppendLine();

            for (int i = 0; i < TrackerSlots.Count; i++)
            {
                TrackerSlot slot = (TrackerSlot)i;
                bool live = controller.IsReceiving(slot);
                float age = controller.SecondsSinceLastMessage(slot);
                string color = live ? "#4CD964" : "#8A8A8A";
                string info = age < 0f ? "no data" : (live ? "ok" : "lost");
                sb.AppendLine("<color=" + color + ">●</color> " + TrackerSlots.DisplayName(slot).PadRight(8) + info);
            }

            if (controller.Alignment == TrackingAlignment.HeadRelative && !controller.IsReceiving(TrackerSlot.Head))
            {
                sb.AppendLine();
                sb.AppendLine("<color=#FFB84D>Head position is not arriving - tracker positions cannot be aligned.</color>");
            }

            return sb.ToString();
        }

        private void EnsureStyles()
        {
            if (textStyle != null) return;
            textStyle = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 14, wordWrap = true };
            textStyle.normal.textColor = Color.white;
            titleStyle = new GUIStyle(textStyle) { fontSize = 17, fontStyle = FontStyle.Bold };
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 13 };
        }

        private void OnGUI()
        {
            if (!showOnScreen || controller == null) return;
            EnsureStyles();

            float scale = Mathf.Max(1f, Screen.height / 900f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            const float width = 330f;
            float height = collapsed ? 34f : 400f;
            GUI.Box(new Rect(10, 10, width, height), GUIContent.none);

            GUILayout.BeginArea(new Rect(18, 14, width - 16f, height - 8f));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Full-Body Tracking Demo", titleStyle);
            if (GUILayout.Button(collapsed ? "+" : "–", buttonStyle, GUILayout.Width(26))) collapsed = !collapsed;
            GUILayout.EndHorizontal();

            if (!collapsed)
            {
                GUILayout.Label(BuildStatusText(), textStyle);
                GUILayout.Space(6f);

                if (simulator != null && simulator.isActiveAndEnabled)
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Simulator: " + (simulator.Sending ? "ON" : "OFF"), buttonStyle)) simulator.Sending = !simulator.Sending;
                    if (GUILayout.Button("Motion: " + simulator.Motion, buttonStyle)) simulator.CycleMotion();
                    GUILayout.EndHorizontal();
                }

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("IK: " + (controller.IkEnabled ? "ON" : "OFF"), buttonStyle)) controller.IkEnabled = !controller.IkEnabled;
                if (demo != null && GUILayout.Button("Markers: " + (demo.MarkersVisible ? "ON" : "OFF"), buttonStyle)) demo.MarkersVisible = !demo.MarkersVisible;
                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();
        }

        /// <summary>Best guess at the address other devices should send OSC to.</summary>
        public static string FindLocalIPv4()
        {
            try
            {
                string fallback = null;
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                    IPInterfaceProperties props = ni.GetIPProperties();
                    foreach (UnicastIPAddressInformation address in props.UnicastAddresses)
                    {
                        if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string text = address.Address.ToString();
                        if (text.StartsWith("169.254.")) continue;
                        if (props.GatewayAddresses.Count > 0) return text;
                        if (fallback == null) fallback = text;
                    }
                }
                return fallback ?? "unknown";
            }
            catch (System.Exception)
            {
                return "unknown";
            }
        }
    }
}
