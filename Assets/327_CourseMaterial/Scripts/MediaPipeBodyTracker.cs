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
    [SerializeField] private bool leftHandPinch;
    [SerializeField] private Vector3 rightHandPosition;
    [SerializeField] private bool rightHandPinch;
    [SerializeField] private Vector3 rightPalmScale;
    [SerializeField] private Vector3 leftPalmScale;

    [Header("Body Data")]
    [SerializeField] private Vector3 torsoPosition;
    [SerializeField] private Vector3 headPosition;

    [Header("Debug / Diagnostics")]
    [SerializeField] private bool verboseLogging = false;
    [SerializeField] private int leftHandPacketCount;
    [SerializeField] private int rightHandPacketCount;
    [SerializeField] private int posePacketCount;

    [Header("Detection Settings")]
    [SerializeField, Range(0.001f, 0.2f)] private float pinchThreshold = 0.035f;

    private readonly object dataLock = new object();
    private Vector3 pendingLeftHandPosition;
    private Vector3 pendingRightHandPosition;
    private Vector3 pendingTorsoPosition;
    private Vector3 pendingHeadPosition;
    private Vector3 pendingLeftPalmScale;
    private Vector3 pendingRightPalmScale;
    private bool pendingLeftPinch;
    private bool pendingRightPinch;
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
    public Vector3 RightPalmScale => rightPalmScale;
    public Vector3 LeftPalmScale => leftPalmScale;
    public bool LeftHandPinch => leftHandPinch;
    public bool LeftHandTracked => isActiveAndEnabled && leftHandTracked;
    public Vector3 RightHandPosition => rightHandPosition;
    public bool RightHandPinch => rightHandPinch;
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
            leftHandPinch = rightHandPinch = false;
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
                    leftHandPinch = pendingLeftPinch;
                    leftPalmScale = pendingLeftPalmScale;
                    rightPalmScale = pendingRightPalmScale;
                }
                else
                {
                    leftHandPosition = Vector3.zero;
                    leftHandPinch = false;
                    leftPalmScale = Vector3.zero;
                    rightPalmScale = Vector3.zero;
                }

                leftDirty = false;
            }

            if (rightDirty)
            {
                if (rightHandTracked)
                {
                    rightHandPosition = pendingRightHandPosition;
                    rightHandPinch = pendingRightPinch;
                }
                else
                {
                    rightHandPosition = Vector3.zero;
                    rightHandPinch = false;
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
                pendingLeftPinch = IsPinching(landmarkList);
            }
            else
            {
                leftHandTracked = false;
                pendingLeftHandPosition = Vector3.zero;
                pendingLeftPinch = false;
            }

            leftDirty = true;
        }

        if (landmarkList != null && landmarkList.Landmark.Count > 9)
        {
            leftHandTracked = true;
            Vector3 wristPos = ToUnityVector(landmarkList.Landmark[0]);
            Vector3 middleMcpPos = ToUnityVector(landmarkList.Landmark[9]);

            pendingLeftHandPosition = wristPos;
            pendingLeftPinch = IsPinching(landmarkList);

            float palmLength = Vector3.Distance(wristPos, middleMcpPos);
            pendingLeftPalmScale = Vector3.one * palmLength;
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
                pendingRightPinch = IsPinching(landmarkList);
            }
            else
            {
                rightHandTracked = false;
                pendingRightHandPosition = Vector3.zero;
                pendingRightPinch = false;
            }

            rightDirty = true;
        }

        if (landmarkList != null && landmarkList.Landmark.Count > 9)
        {
            leftHandTracked = true;
            Vector3 wristPos = ToUnityVector(landmarkList.Landmark[0]);
            Vector3 middleMcpPos = ToUnityVector(landmarkList.Landmark[9]);

            pendingLeftHandPosition = wristPos;
            pendingLeftPinch = IsPinching(landmarkList);

            float palmLength = Vector3.Distance(wristPos, middleMcpPos);
            pendingRightPalmScale = Vector3.one * palmLength;
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

    private bool IsPinching(NormalizedLandmarkList landmarks)
    {
        if (landmarks.Landmark.Count <= 8)
        {
            return false;
        }

        var thumbTip = landmarks.Landmark[4];
        var indexTip = landmarks.Landmark[8];
        var thumb = new Vector2(thumbTip.X, thumbTip.Y);
        var index = new Vector2(indexTip.X, indexTip.Y);
        var distance = Vector2.Distance(thumb, index);

        if (distance > pinchThreshold)
        {
            return false;
        }

        if (landmarks.Landmark.Count > 12)
        {
            var middleTip = landmarks.Landmark[12];
            var middle = new Vector2(middleTip.X, middleTip.Y);
            var indexToMiddle = Vector2.Distance(index, middle);
            if (distance > indexToMiddle)
            {
                return false;
            }
        }

        return true;
    }
}
