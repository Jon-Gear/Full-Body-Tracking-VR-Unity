using OscCore;
using UnityEngine;

namespace FullBodyTracking
{
    public enum SimulatedMotion
    {
        Idle,
        WalkInPlace,
        Squat
    }

    /// <summary>
    /// Pretends to be a tracker sender (SlimeVR, MocapForAll, ...). It animates a virtual performer sized after a
    /// reference avatar and streams the result as real VRChat-protocol OSC messages over UDP, so the complete
    /// receive pipeline can be tested without any hardware. It also moves the simulated headset and controllers
    /// that stand in for an XR rig on desktop.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    [AddComponentMenu("Full Body Tracking/OSC Tracker Simulator")]
    public class OscTrackerSimulator : MonoBehaviour
    {
        [Header("Destination")]
        [SerializeField] private string targetAddress = "127.0.0.1";
        [SerializeField] private int targetPort = 9000;
        [SerializeField, Range(10f, 120f)] private float sendRate = 60f;
        [SerializeField] private bool sending = true;

        [Header("Performer")]
        [Tooltip("The avatar whose proportions the performer copies.")]
        [SerializeField] private Animator referenceAvatar;
        [SerializeField] private SimulatedMotion motion = SimulatedMotion.WalkInPlace;

        [Tooltip("Added to every sent position. Shows that the sender's tracking space does not need to match Unity's.")]
        [SerializeField] private Vector3 trackingSpaceOffset = new Vector3(2.5f, 0f, -1.2f);

        [Header("Simulated hardware (stand-in for an XR rig)")]
        [SerializeField] private Transform simulatedHeadset;
        [SerializeField] private Transform simulatedLeftController;
        [SerializeField] private Transform simulatedRightController;

        public bool Sending { get { return sending; } set { sending = value; } }
        public SimulatedMotion Motion { get { return motion; } set { motion = value; } }
        public string TargetAddress { get { return targetAddress; } }
        public int TargetPort { get { return targetPort; } }

        public void CycleMotion()
        {
            motion = (SimulatedMotion)(((int)motion + 1) % System.Enum.GetValues(typeof(SimulatedMotion)).Length);
        }

        private OscClient client;
        private float sendTimer;
        private float clock;

        // Rest pose, captured once from the reference avatar (world space, avatar at its start position).
        private Vector3 restHips, restChest, restHead, restLeftFoot, restRightFoot;
        private Vector3 restLeftKnee, restRightKnee, restLeftElbow, restRightElbow, restLeftHand, restRightHand;
        private bool hasRest;

        // Current pose.
        private readonly Vector3[] poseP = new Vector3[TrackerSlots.Count];
        private readonly Quaternion[] poseR = new Quaternion[TrackerSlots.Count];

        private void Start()
        {
            CaptureRest();
            for (int i = 0; i < poseR.Length; i++) poseR[i] = Quaternion.identity;
        }

        private void OnEnable()
        {
            try
            {
                client = new OscClient(targetAddress, targetPort);
            }
            catch (System.Exception e)
            {
                client = null;
                Debug.LogWarning("[FullBodyTracking] Simulator could not open OSC client: " + e.Message, this);
            }
        }

        private void OnDisable()
        {
            client = null;
        }

        private Vector3 BonePos(HumanBodyBones bone, Vector3 fallback)
        {
            Transform t = referenceAvatar != null ? referenceAvatar.GetBoneTransform(bone) : null;
            return t != null ? t.position : fallback;
        }

        private void CaptureRest()
        {
            if (referenceAvatar == null || !referenceAvatar.isHuman)
            {
                // Generic 1.75 m person, so the simulator still works without a reference avatar.
                restHips = new Vector3(0f, 0.95f, 0f);
                restChest = new Vector3(0f, 1.3f, 0f);
                restHead = new Vector3(0f, 1.6f, 0f);
                restLeftFoot = new Vector3(-0.1f, 0.08f, 0f);
                restRightFoot = new Vector3(0.1f, 0.08f, 0f);
                restLeftKnee = new Vector3(-0.1f, 0.5f, 0.02f);
                restRightKnee = new Vector3(0.1f, 0.5f, 0.02f);
                restLeftElbow = new Vector3(-0.3f, 1.2f, 0.05f);
                restRightElbow = new Vector3(0.3f, 1.2f, 0.05f);
                restLeftHand = new Vector3(-0.25f, 1.1f, 0.35f);
                restRightHand = new Vector3(0.25f, 1.1f, 0.35f);
                hasRest = true;
                return;
            }

            restHips = BonePos(HumanBodyBones.Hips, Vector3.zero);
            restChest = BonePos(HumanBodyBones.Chest, restHips + Vector3.up * 0.3f);
            restHead = BonePos(HumanBodyBones.Head, restHips + Vector3.up * 0.65f);
            restLeftFoot = BonePos(HumanBodyBones.LeftFoot, Vector3.zero);
            restRightFoot = BonePos(HumanBodyBones.RightFoot, Vector3.zero);
            restLeftKnee = BonePos(HumanBodyBones.LeftLowerLeg, Vector3.zero);
            restRightKnee = BonePos(HumanBodyBones.RightLowerLeg, Vector3.zero);

            // The imported pose is usually a T-pose; the performer holds its arms in front of the body instead.
            Vector3 lShoulder = BonePos(HumanBodyBones.LeftUpperArm, restChest + Vector3.left * 0.2f);
            Vector3 rShoulder = BonePos(HumanBodyBones.RightUpperArm, restChest + Vector3.right * 0.2f);
            restLeftHand = lShoulder + new Vector3(-0.02f, -0.30f, 0.38f);
            restRightHand = rShoulder + new Vector3(0.02f, -0.30f, 0.38f);
            restLeftElbow = lShoulder + new Vector3(-0.08f, -0.28f, 0.04f);
            restRightElbow = rShoulder + new Vector3(0.08f, -0.28f, 0.04f);
            hasRest = true;
        }

        private void Update()
        {
            if (!hasRest) return;

            clock += Time.deltaTime;
            ComputePose(clock);
            DriveSimulatedHardware();

            if (!sending || client == null) return;

            sendTimer += Time.deltaTime;
            float interval = 1f / Mathf.Max(1f, sendRate);
            if (sendTimer < interval) return;
            sendTimer = Mathf.Min(sendTimer - interval, interval);
            SendPose();
        }

        private void ComputePose(float t)
        {
            Vector3 hip = restHips, chest = restChest, head = restHead;
            Vector3 lFoot = restLeftFoot, rFoot = restRightFoot, lKnee = restLeftKnee, rKnee = restRightKnee;
            Vector3 lElbow = restLeftElbow, rElbow = restRightElbow, lHand = restLeftHand, rHand = restRightHand;
            float headYaw = 0f, headPitch = 0f, hipYaw = 0f, hipPitch = 0f, chestPitch = 0f;
            float lFootPitch = 0f, rFootPitch = 0f;

            switch (motion)
            {
                case SimulatedMotion.Idle:
                {
                    float breathe = Mathf.Sin(t * 1.6f) * 0.004f;
                    float shift = Mathf.Sin(t * 0.5f) * 0.03f;
                    hip += new Vector3(shift, breathe, 0f);
                    chest += new Vector3(shift * 1.2f, breathe * 1.5f, 0f);
                    head += new Vector3(shift * 1.4f, breathe * 2f, 0f);
                    headYaw = Mathf.Sin(t * 0.45f) * 35f;
                    headPitch = Mathf.Sin(t * 0.8f) * 8f;
                    lHand += new Vector3(0f, Mathf.Sin(t * 0.9f) * 0.03f, 0f);
                    rHand += new Vector3(0f, Mathf.Sin(t * 0.9f + 1.5f) * 0.03f, 0f);
                    break;
                }

                case SimulatedMotion.WalkInPlace:
                {
                    float phase = t * Mathf.PI * 2f * 0.9f;
                    float liftL = Mathf.Max(0f, Mathf.Sin(phase)) * 0.16f;
                    float liftR = Mathf.Max(0f, Mathf.Sin(phase + Mathf.PI)) * 0.16f;
                    float sweepL = Mathf.Cos(phase) * 0.14f;
                    float sweepR = -Mathf.Cos(phase) * 0.14f;

                    lFoot += new Vector3(0f, liftL, sweepL);
                    rFoot += new Vector3(0f, liftR, sweepR);
                    lFootPitch = liftL / 0.16f * 25f;
                    rFootPitch = liftR / 0.16f * 25f;
                    lKnee += new Vector3(0f, liftL * 0.7f, sweepL * 0.6f + liftL * 0.5f);
                    rKnee += new Vector3(0f, liftR * 0.7f, sweepR * 0.6f + liftR * 0.5f);

                    float bob = -Mathf.Abs(Mathf.Sin(phase)) * 0.025f;
                    float sway = Mathf.Sin(phase) * 0.02f;
                    hip += new Vector3(sway, bob, 0f);
                    chest += new Vector3(sway * 0.8f, bob, 0f);
                    head += new Vector3(sway * 0.6f, bob, 0f);
                    hipYaw = Mathf.Sin(phase) * 6f;

                    lHand += new Vector3(0f, 0f, -Mathf.Sin(phase) * 0.12f);
                    rHand += new Vector3(0f, 0f, Mathf.Sin(phase) * 0.12f);
                    lElbow += new Vector3(0f, 0f, -Mathf.Sin(phase) * 0.05f);
                    rElbow += new Vector3(0f, 0f, Mathf.Sin(phase) * 0.05f);
                    headYaw = Mathf.Sin(t * 0.35f) * 20f;
                    break;
                }

                default: // Squat
                {
                    float depth = (0.5f - 0.5f * Mathf.Cos(t * 1.3f)) * 0.38f;
                    hip += new Vector3(0f, -depth, -depth * 0.55f);
                    chest += new Vector3(0f, -depth * 0.9f, -depth * 0.2f);
                    head += new Vector3(0f, -depth * 0.9f, depth * 0.05f);
                    lKnee += new Vector3(-depth * 0.1f, -depth * 0.45f, depth * 0.7f);
                    rKnee += new Vector3(depth * 0.1f, -depth * 0.45f, depth * 0.7f);
                    hipPitch = depth / 0.38f * 28f;
                    chestPitch = depth / 0.38f * 18f;
                    headPitch = -depth / 0.38f * 10f;
                    lHand += new Vector3(0f, -depth * 0.5f, depth * 0.3f);
                    rHand += new Vector3(0f, -depth * 0.5f, depth * 0.3f);
                    lElbow += new Vector3(0f, -depth * 0.6f, 0f);
                    rElbow += new Vector3(0f, -depth * 0.6f, 0f);
                    break;
                }
            }

            SetPose(TrackerSlot.Head, head + new Vector3(0f, 0.09f, 0.08f), Quaternion.Euler(headPitch, headYaw, 0f));
            SetPose(TrackerSlot.Hip, hip, Quaternion.Euler(hipPitch, hipYaw, 0f));
            SetPose(TrackerSlot.Chest, chest, Quaternion.Euler(chestPitch, hipYaw * 0.5f, 0f));
            SetPose(TrackerSlot.LeftFoot, lFoot, Quaternion.Euler(lFootPitch, 0f, 0f));
            SetPose(TrackerSlot.RightFoot, rFoot, Quaternion.Euler(rFootPitch, 0f, 0f));
            SetPose(TrackerSlot.LeftKnee, lKnee, Quaternion.identity);
            SetPose(TrackerSlot.RightKnee, rKnee, Quaternion.identity);
            SetPose(TrackerSlot.LeftElbow, lElbow, Quaternion.identity);
            SetPose(TrackerSlot.RightElbow, rElbow, Quaternion.identity);

            // Controllers are not part of the OSC stream; they come from the (simulated) headset and hand devices.
            controllerPose[0] = lHand;
            controllerPose[1] = rHand;
        }

        private readonly Vector3[] controllerPose = new Vector3[2];

        private void SetPose(TrackerSlot slot, Vector3 position, Quaternion rotation)
        {
            poseP[(int)slot] = position;
            poseR[(int)slot] = rotation;
        }

        private void DriveSimulatedHardware()
        {
            if (simulatedHeadset != null)
                simulatedHeadset.SetPositionAndRotation(poseP[(int)TrackerSlot.Head], poseR[(int)TrackerSlot.Head]);
            if (simulatedLeftController != null)
                simulatedLeftController.SetPositionAndRotation(controllerPose[0], Quaternion.identity);
            if (simulatedRightController != null)
                simulatedRightController.SetPositionAndRotation(controllerPose[1], Quaternion.identity);
        }

        private void SendPose()
        {
            for (int i = 0; i < TrackerSlots.Count; i++)
            {
                TrackerSlot slot = (TrackerSlot)i;
                client.Send(TrackerSlots.PositionAddress(slot), poseP[i] + trackingSpaceOffset);
                client.Send(TrackerSlots.RotationAddress(slot), poseR[i].eulerAngles);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;
            Gizmos.color = new Color(1f, 0.6f, 0.1f);
            for (int i = 0; i < poseP.Length; i++) Gizmos.DrawWireSphere(poseP[i], 0.04f);
        }
    }
}
