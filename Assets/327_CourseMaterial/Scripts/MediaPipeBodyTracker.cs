using System.Collections;
using Mediapipe;
using Mediapipe.Unity;
using Mediapipe.Unity.Sample.Holistic;
using UnityEngine;

[DisallowMultipleComponent]
public class MediaPipeBodyTracker : MonoBehaviour
{
    [Header("Graph Source")]
    [SerializeField] private HolisticTrackingGraph graphRunner;

    [Header("Hand Data")]
    [SerializeField] private Vector3 leftHandPosition;
    [SerializeField] private bool leftHandFist;
    [SerializeField] private bool leftHandOpen;
    [SerializeField] private bool leftHandOpenApart;
    [SerializeField] private Vector3 rightHandPosition;
    [SerializeField] private bool rightHandFist;
    [SerializeField] private bool rightHandOpen;
    [SerializeField] private bool rightHandOpenApart;

    [Header("Body Data")]
    [SerializeField] private Vector3 torsoPosition;
    [SerializeField] private Vector3 headPosition;

    [Header("Debug / Diagnostics")]
    [SerializeField] private bool verboseLogging = false;
    [SerializeField] private int leftHandPacketCount;
    [SerializeField] private int rightHandPacketCount;
    [SerializeField] private int posePacketCount;

    [Header("Detection Settings")]
    [SerializeField, Range(0.001f, 0.2f)] private float fistThreshold = 0.035f;
    [SerializeField, Range(0.001f, 0.2f)] private float openApartThreshold = 0.05f;

    private readonly object dataLock = new object();
    private Vector3 pendingLeftHandPosition;
    private Vector3 pendingRightHandPosition;
    private Vector3 pendingTorsoPosition;
    private Vector3 pendingHeadPosition;
    private bool pendingLeftFist;
    private bool pendingRightFist;
    private bool pendingLeftOpen;
    private bool pendingRightOpen;
    private bool pendingLeftOpenApart;
    private bool pendingRightOpenApart;
    private bool leftHandTracked;
    private bool rightHandTracked;
    private bool poseTracked;
    private bool leftDirty;
    private bool rightDirty;
    private bool poseDirty;
    private Coroutine subscribeRoutine;
    private bool hasSubscriptions;
    private HolisticTrackingGraph subscribedRunner;

    public Vector3 LeftHandPosition => leftHandPosition;
    public bool LeftHandFist => leftHandFist;
    public bool LeftHandOpen => leftHandOpen;
    public bool LeftHandOpenApart => leftHandOpenApart;
    public bool LeftHandTracked => isActiveAndEnabled && leftHandTracked;

    public Vector3 RightHandPosition => rightHandPosition;
    public bool RightHandFist => rightHandFist;
    public bool RightHandOpen => rightHandOpen;
    public bool RightHandOpenApart => rightHandOpenApart;
    public bool RightHandTracked => isActiveAndEnabled && rightHandTracked;

    public Vector3 TorsoPosition => torsoPosition;
    public Vector3 HeadPosition => headPosition;

    private void OnEnable()
    {
        if (graphRunner == null)
        {
            graphRunner = FindObjectOfType<HolisticTrackingGraph>();
        }

        if (graphRunner == null)
        {
            Debug.LogWarning($"{nameof(MediaPipeBodyTracker)} on {name} could not find a {nameof(HolisticTrackingGraph)}.");
            return;
        }

        if (!TrySubscribe())
        {
            if (verboseLogging)
            {
                Debug.Log($"{nameof(MediaPipeBodyTracker)} waiting for graph runner to initialise...");
            }
            subscribeRoutine = StartCoroutine(SubscribeWhenReady());
        }
    }

    private void OnDisable()
    {
        if (subscribeRoutine != null)
        {
            StopCoroutine(subscribeRoutine);
            subscribeRoutine = null;
        }

        Unsubscribe();
        lock (dataLock)
        {
            leftHandTracked = rightHandTracked = poseTracked = false;
            leftHandFist = leftHandOpen = leftHandOpenApart = false;
            rightHandFist = rightHandOpen = rightHandOpenApart = false;
            leftHandPosition = rightHandPosition = Vector3.zero;
        }
    }

    public void StopTracking()
    {
        OnDisable();
    }

    private IEnumerator SubscribeWhenReady()
    {
        while (isActiveAndEnabled)
        {
            if (TrySubscribe())
            {
                subscribeRoutine = null;
                yield break;
            }

            yield return null;
        }
    }

    private bool TrySubscribe()
    {
        if (graphRunner == null || hasSubscriptions)
        {
            return hasSubscriptions;
        }

        try
        {
            graphRunner.OnLeftHandLandmarksOutput += HandleLeftHand;
            graphRunner.OnRightHandLandmarksOutput += HandleRightHand;
            graphRunner.OnPoseWorldLandmarksOutput += HandlePoseWorld;
            subscribedRunner = graphRunner;
            hasSubscriptions = true;

            if (verboseLogging)
            {
                Debug.Log($"{nameof(MediaPipeBodyTracker)} listening to {graphRunner.name}");
            }

            return true;
        }
        catch (System.NullReferenceException)
        {
            return false;
        }
    }

    private void Unsubscribe()
    {
        if (!hasSubscriptions)
        {
            subscribedRunner = null;
            hasSubscriptions = false;
            return;
        }

        var runner = subscribedRunner != null ? subscribedRunner : graphRunner;
        if (runner != null)
        {
            try
            {
                runner.OnLeftHandLandmarksOutput -= HandleLeftHand;
                runner.OnRightHandLandmarksOutput -= HandleRightHand;
                runner.OnPoseWorldLandmarksOutput -= HandlePoseWorld;
            }
            catch (System.NullReferenceException) { }
        }

        subscribedRunner = null;
        hasSubscriptions = false;
    }

    private void Update()
    {
        lock (dataLock)
        {
            if (leftDirty)
            {
                if (leftHandTracked)
                {
                    leftHandPosition = pendingLeftHandPosition;
                    leftHandFist = pendingLeftFist;
                    leftHandOpen = pendingLeftOpen;
                    leftHandOpenApart = pendingLeftOpenApart;
                }
                else
                {
                    leftHandPosition = Vector3.zero;
                    leftHandFist = false;
                    leftHandOpen = false;
                    leftHandOpenApart = false;
                }

                leftDirty = false;
            }

            if (rightDirty)
            {
                if (rightHandTracked)
                {
                    rightHandPosition = pendingRightHandPosition;
                    rightHandFist = pendingRightFist;
                    rightHandOpen = pendingRightOpen;
                    rightHandOpenApart = pendingRightOpenApart;
                }
                else
                {
                    rightHandPosition = Vector3.zero;
                    rightHandFist = false;
                    rightHandOpen = false;
                    rightHandOpenApart = false;
                }

                rightDirty = false;
            }

            if (poseDirty)
            {
                if (poseTracked)
                {
                    torsoPosition = pendingTorsoPosition;
                    headPosition = pendingHeadPosition;
                }
                else
                {
                    torsoPosition = Vector3.zero;
                    headPosition = Vector3.zero;
                }

                poseDirty = false;
            }
        }
    }

    private void HandleLeftHand(object sender, OutputStream<NormalizedLandmarkList>.OutputEventArgs eventArgs)
    {
        leftHandPacketCount++;
        if (verboseLogging && (leftHandPacketCount % 30 == 0))
        {
            Debug.Log($"{nameof(MediaPipeBodyTracker)} received {leftHandPacketCount} left-hand packets");
        }

        var landmarkList = ExtractNormalizedLandmarks(eventArgs.packet);

        lock (dataLock)
        {
            if (landmarkList != null && landmarkList.Landmark.Count > 0)
            {
                leftHandTracked = true;
                pendingLeftHandPosition = ToUnityVector(landmarkList.Landmark[0]);
                pendingLeftFist = IsFist(landmarkList);
                pendingLeftOpen = IsOpenHand(landmarkList);
                pendingLeftOpenApart = IsOpenHandApart(landmarkList);
            }
            else
            {
                leftHandTracked = false;
                pendingLeftHandPosition = Vector3.zero;
                pendingLeftFist = false;
                pendingLeftOpen = false;
                pendingLeftOpenApart = false;
            }

            leftDirty = true;
        }
    }

    private void HandleRightHand(object sender, OutputStream<NormalizedLandmarkList>.OutputEventArgs eventArgs)
    {
        rightHandPacketCount++;
        if (verboseLogging && (rightHandPacketCount % 30 == 0))
        {
            Debug.Log($"{nameof(MediaPipeBodyTracker)} received {rightHandPacketCount} right-hand packets");
        }

        var landmarkList = ExtractNormalizedLandmarks(eventArgs.packet);

        lock (dataLock)
        {
            if (landmarkList != null && landmarkList.Landmark.Count > 0)
            {
                rightHandTracked = true;
                pendingRightHandPosition = ToUnityVector(landmarkList.Landmark[0]);
                pendingRightFist = IsFist(landmarkList);
                pendingRightOpen = IsOpenHand(landmarkList);
                pendingRightOpenApart = IsOpenHandApart(landmarkList);
            }
            else
            {
                rightHandTracked = false;
                pendingRightHandPosition = Vector3.zero;
                pendingRightFist = false;
                pendingRightOpen = false;
                pendingRightOpenApart = false;
            }

            rightDirty = true;
        }
    }

    private void HandlePoseWorld(object sender, OutputStream<LandmarkList>.OutputEventArgs eventArgs)
    {
        posePacketCount++;
        if (verboseLogging && (posePacketCount % 30 == 0))
        {
            Debug.Log($"{nameof(MediaPipeBodyTracker)} received {posePacketCount} pose packets");
        }

        var worldLandmarks = ExtractWorldLandmarks(eventArgs.packet);

        lock (dataLock)
        {
            if (worldLandmarks != null && worldLandmarks.Landmark.Count >= 33)
            {
                poseTracked = true;
                pendingHeadPosition = ToUnityVector(worldLandmarks.Landmark[0]);

                var leftShoulder = ToUnityVector(worldLandmarks.Landmark[11]);
                var rightShoulder = ToUnityVector(worldLandmarks.Landmark[12]);
                var leftHip = ToUnityVector(worldLandmarks.Landmark[23]);
                var rightHip = ToUnityVector(worldLandmarks.Landmark[24]);

                var shouldersCenter = (leftShoulder + rightShoulder) * 0.5f;
                var hipsCenter = (leftHip + rightHip) * 0.5f;
                pendingTorsoPosition = (shouldersCenter + hipsCenter) * 0.5f;
            }
            else
            {
                poseTracked = false;
                pendingHeadPosition = Vector3.zero;
                pendingTorsoPosition = Vector3.zero;
            }

            poseDirty = true;
        }
    }

    private NormalizedLandmarkList ExtractNormalizedLandmarks(Packet<NormalizedLandmarkList> packet)
    {
        return packet == null ? null : packet.Get(NormalizedLandmarkList.Parser);
    }

    private LandmarkList ExtractWorldLandmarks(Packet<LandmarkList> packet)
    {
        return packet == null ? null : packet.Get(LandmarkList.Parser);
    }

    private Vector3 ToUnityVector(NormalizedLandmark landmark)
    {
        return new Vector3(landmark.X, landmark.Y, landmark.Z);
    }

    private Vector3 ToUnityVector(Landmark landmark)
    {
        return new Vector3(landmark.X, landmark.Y, landmark.Z);
    }

    private bool IsFist(NormalizedLandmarkList landmarks)
    {
        if (landmarks.Landmark.Count < 21)
        {
            return false;
        }

        int curledFingersCount = 0;

        if (IsFingerCurled(landmarks, 8, 6)) curledFingersCount++;
        if (IsFingerCurled(landmarks, 12, 10)) curledFingersCount++;
        if (IsFingerCurled(landmarks, 16, 14)) curledFingersCount++;
        if (IsFingerCurled(landmarks, 20, 18)) curledFingersCount++;

        return curledFingersCount >= 3;
    }

    private bool IsFingerCurled(NormalizedLandmarkList landmarks, int tipIndex, int pipIndex)
    {
        var wrist = landmarks.Landmark[0];
        var tip = landmarks.Landmark[tipIndex];
        var pip = landmarks.Landmark[pipIndex];

        var wristPos = new Vector3(wrist.X, wrist.Y, wrist.Z);
        var tipPos = new Vector3(tip.X, tip.Y, tip.Z);
        var pipPos = new Vector3(pip.X, pip.Y, pip.Z);

        float tipToWrist = Vector3.Distance(tipPos, wristPos);
        float pipToWrist = Vector3.Distance(pipPos, wristPos);

        return (tipToWrist < pipToWrist) || (tipToWrist < fistThreshold);
    }

    private bool IsOpenHand(NormalizedLandmarkList landmarks)
    {
        if (landmarks.Landmark.Count < 21)
        {
            return false;
        }

        return IsFingerExtended(landmarks, 8, 6) &&
               IsFingerExtended(landmarks, 12, 10) &&
               IsFingerExtended(landmarks, 16, 14) &&
               IsFingerExtended(landmarks, 20, 18) &&
               !IsOpenHandApart(landmarks);
    }

    private bool IsOpenHandApart(NormalizedLandmarkList landmarks)
    {
        if (landmarks.Landmark.Count < 21)
        {
            return false;
        }

        if (!IsFingerExtended(landmarks, 8, 6) ||
            !IsFingerExtended(landmarks, 12, 10) ||
            !IsFingerExtended(landmarks, 16, 14) ||
            !IsFingerExtended(landmarks, 20, 18))
        {
            return false;
        }

        var thumbTip = new Vector3(landmarks.Landmark[4].X, landmarks.Landmark[4].Y, landmarks.Landmark[4].Z);
        var indexTip = new Vector3(landmarks.Landmark[8].X, landmarks.Landmark[8].Y, landmarks.Landmark[8].Z);
        var middleTip = new Vector3(landmarks.Landmark[12].X, landmarks.Landmark[12].Y, landmarks.Landmark[12].Z);
        var ringTip = new Vector3(landmarks.Landmark[16].X, landmarks.Landmark[16].Y, landmarks.Landmark[16].Z);
        var pinkyTip = new Vector3(landmarks.Landmark[20].X, landmarks.Landmark[20].Y, landmarks.Landmark[20].Z);

        float thumbIndexDist = Vector3.Distance(thumbTip, indexTip);
        float indexMiddleDist = Vector3.Distance(indexTip, middleTip);
        float middleRingDist = Vector3.Distance(middleTip, ringTip);
        float ringPinkyDist = Vector3.Distance(ringTip, pinkyTip);

        int spreadCount = 0;
        if (thumbIndexDist > openApartThreshold) spreadCount++;
        if (indexMiddleDist > openApartThreshold) spreadCount++;
        if (middleRingDist > openApartThreshold) spreadCount++;
        if (ringPinkyDist > openApartThreshold) spreadCount++;

        return spreadCount >= 3;
    }

    private bool IsFingerExtended(NormalizedLandmarkList landmarks, int tipIndex, int pipIndex)
    {
        var wrist = landmarks.Landmark[0];
        var tip = landmarks.Landmark[tipIndex];
        var pip = landmarks.Landmark[pipIndex];

        var wristPos = new Vector3(wrist.X, wrist.Y, wrist.Z);
        var tipPos = new Vector3(tip.X, tip.Y, tip.Z);
        var pipPos = new Vector3(pip.X, pip.Y, pip.Z);

        float tipToWrist = Vector3.Distance(tipPos, wristPos);
        float pipToWrist = Vector3.Distance(pipPos, wristPos);

        return tipToWrist > pipToWrist;
    }
}