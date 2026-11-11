using System.Text;
using BlobHandles;
using UnityEngine;

namespace OscCore.Demo
{
    public class MonitorToDebugText : MonoBehaviour
    {
        const int k_LineCount = 9;
        const int k_LastIndex = k_LineCount - 1;
        static readonly StringBuilder k_StringBuilder = new StringBuilder();
        
        public OscReceiver Receiver;

        public TextMesh IpAddressText;
        public TextMesh RecentValueText;

        int m_ReplaceLineIndex;
        bool m_Dirty;

        readonly string[] m_ReceivedAsString = new string[k_LineCount];

        public void Awake()
        {
            IpAddressText.text = $"Local IP: {Utils.GetLocalIpAddress()} , Port {Receiver.Port}";
            
            Receiver.Server.AddMonitorCallback(Monitor);

            // POINT 2: THE CATCH-ALL
            // This hooks into the server at the lowest level. 
            // If ANY data arrives on this port, this WILL fire.
            Receiver.Server.AddMonitorCallback((address, values) =>
            {
                // This confirms the phone is hitting the script
                Debug.Log($"[RAW DATA DETECTED] Address: {address}");

                // Let's force it to show up on your VR screen
                m_Dirty = true;

                // We push the latest address to the top of your debug list
                m_ReceivedAsString[0] = $"> {address}";
            });
        }

        void Update()
        {
            if (m_Dirty)
            {
                RecentValueText.text = BuildMultiLine();
                m_Dirty = false;
            }
        }

        void Monitor(BlobString address, OscMessageValues values)
        {
            Debug.Log("Monitor received: " + Utils.MonitorMessageToString(address, values));

            m_Dirty = true;

            if (m_ReplaceLineIndex == k_LastIndex)
            {
                for (int i = 0; i < k_LastIndex; i++)
                {
                    m_ReceivedAsString[i] = m_ReceivedAsString[i + 1];
                }
            }

            m_ReceivedAsString[m_ReplaceLineIndex] = Utils.MonitorMessageToString(address, values);
            
            if (m_ReplaceLineIndex < k_LastIndex) 
                m_ReplaceLineIndex++;
        }

        string BuildMultiLine()
        {
            k_StringBuilder.Clear();
            for (int i = 0; i <= m_ReplaceLineIndex; i++)
            {
                k_StringBuilder.AppendLine(m_ReceivedAsString[i]);
                k_StringBuilder.AppendLine();
            }

            return k_StringBuilder.ToString();
        }
    }
}