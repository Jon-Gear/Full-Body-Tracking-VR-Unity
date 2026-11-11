using System.Threading;
using OscCore;
using UnityEngine;

namespace FullBodyTracking
{
    /// <summary>Tracker slots of the VRChat OSC Trackers protocol that this project understands.</summary>
    public enum TrackerSlot
    {
        Head,
        Hip,
        LeftFoot,
        RightFoot,
        LeftKnee,
        RightKnee,
        Chest,
        LeftElbow,
        RightElbow
    }

    public enum TrackingAlignment
    {
        /// <summary>
        /// Tracker positions are re-based onto the headset using the OSC head position
        /// (the same idea VRChat uses to align the sender's tracking space with the app).
        /// Requires the sender to also stream /tracking/trackers/head/position.
        /// </summary>
        HeadRelative,

        /// <summary>OSC positions are used as Unity world positions as they are.</summary>
        Absolute
    }

    public static class TrackerSlots
    {
        public const int Count = 9;

        /// <summary>The id used in /tracking/trackers/{id}/position and /rotation.</summary>
        public static string OscId(TrackerSlot slot)
        {
            switch (slot)
            {
                case TrackerSlot.Head: return "head";
                case TrackerSlot.Hip: return "1";
                case TrackerSlot.LeftFoot: return "2";
                case TrackerSlot.RightFoot: return "3";
                case TrackerSlot.LeftKnee: return "4";
                case TrackerSlot.RightKnee: return "5";
                case TrackerSlot.Chest: return "6";
                case TrackerSlot.LeftElbow: return "7";
                default: return "8";
            }
        }

        public static string DisplayName(TrackerSlot slot)
        {
            switch (slot)
            {
                case TrackerSlot.Head: return "Head";
                case TrackerSlot.Hip: return "Hip";
                case TrackerSlot.LeftFoot: return "L Foot";
                case TrackerSlot.RightFoot: return "R Foot";
                case TrackerSlot.LeftKnee: return "L Knee";
                case TrackerSlot.RightKnee: return "R Knee";
                case TrackerSlot.Chest: return "Chest";
                case TrackerSlot.LeftElbow: return "L Elbow";
                default: return "R Elbow";
            }
        }

        public static string PositionAddress(TrackerSlot slot) { return "/tracking/trackers/" + OscId(slot) + "/position"; }
        public static string RotationAddress(TrackerSlot slot) { return "/tracking/trackers/" + OscId(slot) + "/rotation"; }
    }

    /// <summary>
    /// Receives VRChat-style OSC tracker data (hip, feet, knees, chest, elbows, head) and drives a
    /// humanoid avatar with Unity's built-in Animator IK. No third-party IK package is required.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    [DefaultExecutionOrder(100)]
    [AddComponentMenu("Full Body Tracking/OSC Full Body IK Controller")]
    public class OscFullBodyIKController : MonoBehaviour
    {
        static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
        static double Now { get { return Clock.Elapsed.TotalSeconds; } }

        #region Inspector

        [Header("OSC")]
        [Tooltip("UDP port to listen on. VRChat's default port for OSC trackers is 9000.")]
        [SerializeField] private int listenPort = 9000;

        [Tooltip("A tracker counts as lost when no position message arrived for this many seconds.")]
        [SerializeField, Min(0.1f)] private float trackerTimeout = 0.75f;

        [Tooltip("Print every received OSC message to the Console (very verbose).")]
        [SerializeField] private bool logMessages;

        [Header("IK")]
        [SerializeField] private bool ikEnabled = true;

        [SerializeField] private TrackingAlignment alignment = TrackingAlignment.HeadRelative;

        [Tooltip("Deactivate a tracker target object while its tracker is not sending data.")]
        [SerializeField] private bool hideTargetsWhenLost = true;

        [Tooltip("Move the avatar's root object so it follows the hip tracker on the floor plane.")]
        [SerializeField] private bool rootFollowsHip = true;

        [Range(0f, 1f)] [SerializeField] private float lookAtWeight = 1f;
        [SerializeField] private float lookAtDistance = 0.5f;

        [Range(0f, 1f)] [SerializeField] private float handRotationWeight = 1f;

        [Tooltip("Rotation of the avatar's hand relative to the XR controller (in controller space). The defaults suit Mixamo-style T-pose rigs: fingers forward, thumbs up. Tune per avatar.")]
        [SerializeField] private Vector3 leftHandRotationOffset = new Vector3(270f, 90f, 0f);
        [SerializeField] private Vector3 rightHandRotationOffset = new Vector3(270f, 270f, 0f);

        [Header("Avatar")]
        [Tooltip("Object that is moved when 'Root Follows Hip' is on. Defaults to this object.")]
        [SerializeField] private Transform avatarRoot;

        [Header("Headset & Controllers (XR rig or simulation)")]
        [SerializeField] private Transform headset;
        [SerializeField] private Transform leftController;
        [SerializeField] private Transform rightController;

        [Header("Tracker Targets (optional, updated every frame - handy for debugging)")]
        [SerializeField] private Transform headTarget;
        [SerializeField] private Transform hipTarget;
        [SerializeField] private Transform chestTarget;
        [SerializeField] private Transform leftFootTarget;
        [SerializeField] private Transform rightFootTarget;
        [SerializeField] private Transform leftKneeTarget;
        [SerializeField] private Transform rightKneeTarget;
        [SerializeField] private Transform leftElbowTarget;
        [SerializeField] private Transform rightElbowTarget;

        #endregion

        #region Public API

        public int ListenPort { get { return listenPort; } }
        public OscServer Server { get; private set; }

        /// <summary>Set when the OSC server could not be started (for example the port is already in use).</summary>
        public string ServerError { get; private set; }

        public bool IkEnabled { get { return ikEnabled; } set { ikEnabled = value; } }

        public TrackingAlignment Alignment { get { return alignment; } set { alignment = value; } }

        /// <summary>Total number of OSC messages received since start.</summary>
        public long MessageCount { get { return Interlocked.Read(ref messageCount); } }

        /// <summary>True while position data for the slot is arriving (within the timeout).</summary>
        public bool IsReceiving(TrackerSlot slot) { return receiving[(int)slot]; }

        /// <summary>True if the slot is both receiving and usable for IK given the current alignment mode.</summary>
        public bool IsUsable(TrackerSlot slot) { return usable[(int)slot]; }

        /// <summary>Seconds since the last position message for the slot, or -1 if none was ever received.</summary>
        public float SecondsSinceLastMessage(TrackerSlot slot)
        {
            double t;
            lock (gate) { t = lastPositionAt[(int)slot]; }
            return t < 0 ? -1f : (float)(Now - t);
        }

        public void SetHeadsetAndControllers(Transform newHeadset, Transform newLeft, Transform newRight)
        {
            headset = newHeadset;
            leftController = newLeft;
            rightController = newRight;
        }

        #endregion

        #region State

        private Animator animator;
        private Transform root;
        private Vector3 rootStartPosition;
        private Quaternion rootStartRotation;

        private readonly object gate = new object();
        private readonly Vector3[] rawPosition = new Vector3[TrackerSlots.Count];
        private readonly Vector3[] rawEuler = new Vector3[TrackerSlots.Count];
        private readonly double[] lastPositionAt = new double[TrackerSlots.Count];
        private long messageCount;

        private readonly bool[] receiving = new bool[TrackerSlots.Count];
        private readonly bool[] usable = new bool[TrackerSlots.Count];
        private readonly Transform[] targets = new Transform[TrackerSlots.Count];

        private readonly OscActionPair[] positionPairs = new OscActionPair[TrackerSlots.Count];
        private readonly OscActionPair[] rotationPairs = new OscActionPair[TrackerSlots.Count];

        // Rest-pose calibration (captured on the first IK pass, avatar in its imported pose).
        private bool calibrated;
        private Quaternion restBodyRotation;
        private Quaternion restLeftFootGoal, restRightFootGoal, restLeftHandGoal, restRightHandGoal;
        private Quaternion restHeadBone, restChestBone;
        private Quaternion bodyDelta = Quaternion.identity;

        #endregion

        #region Unity lifecycle

        private void Awake()
        {
            animator = GetComponent<Animator>();
            root = avatarRoot != null ? avatarRoot : transform;
            rootStartPosition = root.position;
            rootStartRotation = root.rotation;

            for (int i = 0; i < TrackerSlots.Count; i++) lastPositionAt[i] = -1.0;

            targets[(int)TrackerSlot.Head] = headTarget;
            targets[(int)TrackerSlot.Hip] = hipTarget;
            targets[(int)TrackerSlot.Chest] = chestTarget;
            targets[(int)TrackerSlot.LeftFoot] = leftFootTarget;
            targets[(int)TrackerSlot.RightFoot] = rightFootTarget;
            targets[(int)TrackerSlot.LeftKnee] = leftKneeTarget;
            targets[(int)TrackerSlot.RightKnee] = rightKneeTarget;
            targets[(int)TrackerSlot.LeftElbow] = leftElbowTarget;
            targets[(int)TrackerSlot.RightElbow] = rightElbowTarget;
        }

        private void OnEnable()
        {
            StartServer();
            RegisterAddresses();
        }

        private void OnDisable()
        {
            UnregisterAddresses();
        }

        private void OnDestroy()
        {
            if (Server != null)
            {
                Server.Dispose();
                Server = null;
            }
        }

        private void Update()
        {
            if (Server != null) Server.Update();
            RefreshTrackerTargets();
        }

        #endregion

        #region OSC

        private void StartServer()
        {
            if (Server != null) return;

            try
            {
                Server = OscServer.GetOrCreate(listenPort);
                ServerError = null;
            }
            catch (System.Exception e)
            {
                ServerError = "Could not listen on UDP port " + listenPort + ": " + e.Message;
                Debug.LogWarning("[FullBodyTracking] " + ServerError, this);
            }
        }

        private void RegisterAddresses()
        {
            if (Server == null) return;

            for (int i = 0; i < TrackerSlots.Count; i++)
            {
                int slot = i; // captured by the lambdas below
                positionPairs[i] = new OscActionPair(values => OnPosition(slot, values), null);
                rotationPairs[i] = new OscActionPair(values => OnRotation(slot, values), null);
                Server.TryAddMethodPair(TrackerSlots.PositionAddress((TrackerSlot)i), positionPairs[i]);
                Server.TryAddMethodPair(TrackerSlots.RotationAddress((TrackerSlot)i), rotationPairs[i]);
            }
        }

        private void UnregisterAddresses()
        {
            if (Server == null) return;

            for (int i = 0; i < TrackerSlots.Count; i++)
            {
                if (positionPairs[i] != null) Server.RemoveMethodPair(TrackerSlots.PositionAddress((TrackerSlot)i), positionPairs[i]);
                if (rotationPairs[i] != null) Server.RemoveMethodPair(TrackerSlots.RotationAddress((TrackerSlot)i), rotationPairs[i]);
                positionPairs[i] = null;
                rotationPairs[i] = null;
            }
        }

        // NOTE: OSC value callbacks run on OscCore's receive thread, not on the main thread.
        private void OnPosition(int slot, OscMessageValues values)
        {
            if (values.ElementCount < 3) return;
            var v = new Vector3(values.ReadFloatElement(0), values.ReadFloatElement(1), values.ReadFloatElement(2));
            lock (gate)
            {
                rawPosition[slot] = v;
                lastPositionAt[slot] = Now;
            }
            Interlocked.Increment(ref messageCount);
            if (logMessages) Debug.Log("OSC " + TrackerSlots.PositionAddress((TrackerSlot)slot) + "  " + v);
        }

        private void OnRotation(int slot, OscMessageValues values)
        {
            if (values.ElementCount < 3) return;
            var v = new Vector3(values.ReadFloatElement(0), values.ReadFloatElement(1), values.ReadFloatElement(2));
            lock (gate) { rawEuler[slot] = v; }
            Interlocked.Increment(ref messageCount);
            if (logMessages) Debug.Log("OSC " + TrackerSlots.RotationAddress((TrackerSlot)slot) + "  " + v);
        }

        #endregion

        #region Tracker targets

        private readonly Vector3[] snapshotPosition = new Vector3[TrackerSlots.Count];
        private readonly Vector3[] snapshotEuler = new Vector3[TrackerSlots.Count];

        private void RefreshTrackerTargets()
        {
            double now = Now;
            lock (gate)
            {
                for (int i = 0; i < TrackerSlots.Count; i++)
                {
                    receiving[i] = lastPositionAt[i] >= 0.0 && now - lastPositionAt[i] <= trackerTimeout;
                    snapshotPosition[i] = rawPosition[i];
                    snapshotEuler[i] = rawEuler[i];
                }
            }

            // The head always comes from the headset (XR or simulated), never from OSC.
            bool hasHeadset = headset != null;
            usable[(int)TrackerSlot.Head] = hasHeadset;
            if (hasHeadset && headTarget != null) headTarget.SetPositionAndRotation(headset.position, headset.rotation);

            bool headReferenceOk = alignment == TrackingAlignment.Absolute || (receiving[(int)TrackerSlot.Head] && hasHeadset);
            Vector3 headOsc = snapshotPosition[(int)TrackerSlot.Head];
            Vector3 headWorld = hasHeadset ? headset.position : Vector3.zero;

            for (int i = 1; i < TrackerSlots.Count; i++)
            {
                usable[i] = receiving[i] && headReferenceOk;
                if (targets[i] == null) continue;

                if (hideTargetsWhenLost && targets[i].gameObject.activeSelf != usable[i]) targets[i].gameObject.SetActive(usable[i]);
                if (!usable[i]) continue;

                Vector3 position = alignment == TrackingAlignment.Absolute
                    ? snapshotPosition[i]
                    : headWorld + (snapshotPosition[i] - headOsc);

                targets[i].SetPositionAndRotation(position, Quaternion.Euler(snapshotEuler[i]));
            }

            if (rootFollowsHip && usable[(int)TrackerSlot.Hip] && hipTarget != null)
            {
                Vector3 hip = hipTarget.position;
                root.position = new Vector3(hip.x, rootStartPosition.y, hip.z);
            }
        }

        #endregion

        #region Animator IK

        private void OnAnimatorIK(int layerIndex)
        {
            if (animator == null) return;
            if (!calibrated) Calibrate();

            if (!ikEnabled)
            {
                ReleaseIk();
                return;
            }

            ApplyBody();
            ApplyHead();
            ApplyHands();
            ApplyFoot(TrackerSlot.LeftFoot, AvatarIKGoal.LeftFoot, restLeftFootGoal);
            ApplyFoot(TrackerSlot.RightFoot, AvatarIKGoal.RightFoot, restRightFootGoal);
            ApplyHint(TrackerSlot.LeftKnee, AvatarIKHint.LeftKnee);
            ApplyHint(TrackerSlot.RightKnee, AvatarIKHint.RightKnee);
            ApplyHint(TrackerSlot.LeftElbow, AvatarIKHint.LeftElbow);
            ApplyHint(TrackerSlot.RightElbow, AvatarIKHint.RightElbow);
            ApplyChest();
        }

        // Rest rotations let a tracker with identity rotation mean 'the avatar in its imported pose, facing forward'.
        private void Calibrate()
        {
            restBodyRotation = animator.bodyRotation;
            restLeftFootGoal = animator.GetIKRotation(AvatarIKGoal.LeftFoot);
            restRightFootGoal = animator.GetIKRotation(AvatarIKGoal.RightFoot);
            restLeftHandGoal = animator.GetIKRotation(AvatarIKGoal.LeftHand);
            restRightHandGoal = animator.GetIKRotation(AvatarIKGoal.RightHand);

            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            restHeadBone = head != null ? head.rotation : Quaternion.identity;
            restChestBone = chest != null ? chest.rotation : Quaternion.identity;
            calibrated = true;
        }

        private Quaternion Relative(Quaternion rest)
        {
            return Quaternion.Inverse(rootStartRotation) * rest;
        }

        private void ApplyBody()
        {
            bodyDelta = Quaternion.identity;
            if (!usable[(int)TrackerSlot.Hip] || hipTarget == null) return;

            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Vector3 centerOffset = hips != null ? animator.bodyPosition - hips.position : Vector3.zero;

            Quaternion desired = hipTarget.rotation * Relative(restBodyRotation);
            bodyDelta = desired * Quaternion.Inverse(animator.bodyRotation);

            animator.bodyPosition = hipTarget.position + centerOffset;
            animator.bodyRotation = desired;
        }

        private void ApplyHead()
        {
            if (!usable[(int)TrackerSlot.Head] || headset == null)
            {
                animator.SetLookAtWeight(0f);
                return;
            }

            animator.SetLookAtWeight(lookAtWeight);
            animator.SetLookAtPosition(headset.position + headset.forward * lookAtDistance);

            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null) head.rotation = headset.rotation * Relative(restHeadBone);
        }

        private void ApplyHands()
        {
            ApplyHand(AvatarIKGoal.LeftHand, leftController, restLeftHandGoal, leftHandRotationOffset);
            ApplyHand(AvatarIKGoal.RightHand, rightController, restRightHandGoal, rightHandRotationOffset);
        }

        private void ApplyHand(AvatarIKGoal goal, Transform controller, Quaternion restGoal, Vector3 offsetEuler)
        {
            if (controller == null)
            {
                animator.SetIKPositionWeight(goal, 0f);
                animator.SetIKRotationWeight(goal, 0f);
                return;
            }

            animator.SetIKPositionWeight(goal, 1f);
            animator.SetIKRotationWeight(goal, handRotationWeight);
            animator.SetIKPosition(goal, controller.position);
            animator.SetIKRotation(goal, controller.rotation * Quaternion.Euler(offsetEuler) * Relative(restGoal));
        }

        private void ApplyFoot(TrackerSlot slot, AvatarIKGoal goal, Quaternion restGoal)
        {
            Transform target = targets[(int)slot];
            if (!usable[(int)slot] || target == null)
            {
                animator.SetIKPositionWeight(goal, 0f);
                animator.SetIKRotationWeight(goal, 0f);
                return;
            }

            animator.SetIKPositionWeight(goal, 1f);
            animator.SetIKRotationWeight(goal, 1f);
            animator.SetIKPosition(goal, target.position);
            animator.SetIKRotation(goal, target.rotation * Relative(restGoal));
        }

        private void ApplyHint(TrackerSlot slot, AvatarIKHint hint)
        {
            Transform target = targets[(int)slot];
            if (!usable[(int)slot] || target == null)
            {
                animator.SetIKHintPositionWeight(hint, 0f);
                return;
            }

            animator.SetIKHintPositionWeight(hint, 1f);
            animator.SetIKHintPosition(hint, target.position);
        }

        private void ApplyChest()
        {
            Transform target = targets[(int)TrackerSlot.Chest];
            if (!usable[(int)TrackerSlot.Chest] || target == null) return;

            Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            if (chest == null || chest.parent == null) return;

            // The parent bone will be rotated by the body rotation applied above; account for that.
            Quaternion desiredWorld = target.rotation * Relative(restChestBone);
            Quaternion parentWorld = bodyDelta * chest.parent.rotation;
            animator.SetBoneLocalRotation(HumanBodyBones.Chest, Quaternion.Inverse(parentWorld) * desiredWorld);
        }

        private void ReleaseIk()
        {
            animator.SetLookAtWeight(0f);

            AvatarIKGoal[] goals = { AvatarIKGoal.LeftHand, AvatarIKGoal.RightHand, AvatarIKGoal.LeftFoot, AvatarIKGoal.RightFoot };
            foreach (AvatarIKGoal goal in goals)
            {
                animator.SetIKPositionWeight(goal, 0f);
                animator.SetIKRotationWeight(goal, 0f);
            }

            AvatarIKHint[] hints = { AvatarIKHint.LeftKnee, AvatarIKHint.RightKnee, AvatarIKHint.LeftElbow, AvatarIKHint.RightElbow };
            foreach (AvatarIKHint hint in hints) animator.SetIKHintPositionWeight(hint, 0f);

            if (root != null && rootFollowsHip)
            {
                root.position = rootStartPosition;
                root.rotation = rootStartRotation;
            }
        }

        #endregion
    }
}
