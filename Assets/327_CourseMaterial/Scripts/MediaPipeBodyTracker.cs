
using System.Collections;
using System.Collections.Generic;
using Mediapipe;
using Mediapipe.Unity;
using Mediapipe.Unity.Sample.Holistic;
using UnityEngine;
using Color = UnityEngine.Color;

[DisallowMultipleComponent]
public class MediaPipeBodyTracker : MonoBehaviour
{
    [Header("Graph Source")]
    [SerializeField] private HolisticTrackingGraph graphRunner;

    [Header("Hand Data - Palm Center")]
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

    [Header("Metaball Visual Setup")]
    [Tooltip("Optional: Assign prefabs or primitive spheres for the 21 joints to visualize metaballs.")]
    [SerializeField] private bool spawnJointVisualizers = false;
    [SerializeField] private GameObject jointPrefab;
    [SerializeField] private Transform leftHandParentTransform;
    [SerializeField] private Transform rightHandParentTransform;

    [Header("Debug / Diagnostics")]
    [SerializeField] private bool verboseLogging = false;
    [SerializeField] private int leftHandPacketCount;
    [SerializeField] private int rightHandPacketCount;
    [SerializeField] private int posePacketCount;

    [Header("Detection Settings")]
    [SerializeField, Range(0.001f, 0.2f)]
    private float fistThreshold = 0.035f;

    [SerializeField, Range(0.001f, 0.2f)]
    private float openApartThreshold = 0.05f;

    // Full 21-landmark arrays.
    private readonly Vector3[] leftHandLandmarks = new Vector3[21];
    private readonly Vector3[] rightHandLandmarks = new Vector3[21];
    private readonly Vector3[] pendingLeftLandmarks = new Vector3[21];
    private readonly Vector3[] pendingRightLandmarks = new Vector3[21];

    // Joint visualizer transforms.
    private Transform[] leftJointTransforms = new Transform[21];
    private Transform[] rightJointTransforms = new Transform[21];

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

    // Timeout tracking.
    private int leftFramesSincePacket = 0;
    private int rightFramesSincePacket = 0;
    private const int trackingTimeoutLimit = 15;

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

    public void GetLeftHandLandmarks(Vector3[] buffer)
    {
        lock (dataLock)
        {
            if (buffer == null || buffer.Length < 21)
                return;

            System.Array.Copy(leftHandLandmarks, buffer, 21);
        }
    }

    public void GetRightHandLandmarks(Vector3[] buffer)
    {
        lock (dataLock)
        {
            if (buffer == null || buffer.Length < 21)
                return;

            System.Array.Copy(rightHandLandmarks, buffer, 21);
        }
    }

    private void Awake()
    {
        if (spawnJointVisualizers && jointPrefab != null)
            InitializeVisualizers();
    }

    private void InitializeVisualizers()
    {
        if (leftHandParentTransform == null)
        {
            leftHandParentTransform =
                new GameObject("LeftHand_MetaballRoot").transform;
        }

        if (rightHandParentTransform == null)
        {
            rightHandParentTransform =
                new GameObject("RightHand_MetaballRoot").transform;
        }

        for (int i = 0; i < 21; i++)
        {
            GameObject leftObject =
                Instantiate(jointPrefab, leftHandParentTransform);

            leftJointTransforms[i] = leftObject.transform;
            leftObject.SetActive(false);

            GameObject rightObject =
                Instantiate(jointPrefab, rightHandParentTransform);

            rightJointTransforms[i] = rightObject.transform;
            rightObject.SetActive(false);
        }
    }

    private void OnEnable()
    {
        if (graphRunner == null)
            graphRunner = FindObjectOfType<HolisticTrackingGraph>();

        if (graphRunner == null)
        {
            Debug.LogWarning(
                $"{nameof(MediaPipeBodyTracker)} on {name} could not find a " +
                $"{nameof(HolisticTrackingGraph)}."
            );

            return;
        }

        if (!TrySubscribe())
            subscribeRoutine = StartCoroutine(SubscribeWhenReady());
    }

    private void OnDisable()
    {
        if (subscribeRoutine != null)
        {
            StopCoroutine(subscribeRoutine);
            subscribeRoutine = null;
        }

        Unsubscribe();
        ResetAllTrackingData();
    }

    private void ResetAllTrackingData()
    {
        lock (dataLock)
        {
            leftHandTracked = false;
            rightHandTracked = false;
            poseTracked = false;

            leftHandFist = false;
            leftHandOpen = false;
            leftHandOpenApart = false;

            rightHandFist = false;
            rightHandOpen = false;
            rightHandOpenApart = false;

            leftHandPosition = Vector3.zero;
            rightHandPosition = Vector3.zero;
            torsoPosition = Vector3.zero;
            headPosition = Vector3.zero;

            leftDirty = false;
            rightDirty = false;
            poseDirty = false;

            leftFramesSincePacket = 0;
            rightFramesSincePacket = 0;
        }
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
            return hasSubscriptions;

        try
        {
            graphRunner.OnLeftHandLandmarksOutput += HandleLeftHand;
            graphRunner.OnRightHandLandmarksOutput += HandleRightHand;
            graphRunner.OnPoseWorldLandmarksOutput += HandlePoseWorld;

            subscribedRunner = graphRunner;
            hasSubscriptions = true;

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
            return;

        HolisticTrackingGraph runner =
            subscribedRunner != null ? subscribedRunner : graphRunner;

        if (runner != null)
        {
            try
            {
                runner.OnLeftHandLandmarksOutput -= HandleLeftHand;
                runner.OnRightHandLandmarksOutput -= HandleRightHand;
                runner.OnPoseWorldLandmarksOutput -= HandlePoseWorld;
            }
            catch (System.NullReferenceException)
            {
                // The graph may already have been disposed.
            }
        }

        subscribedRunner = null;
        hasSubscriptions = false;
    }

    private void Update()
    {
        lock (dataLock)
        {
            // Timeout checks for the left hand.
            leftFramesSincePacket++;

            if (leftFramesSincePacket > trackingTimeoutLimit)
            {
                leftHandTracked = false;
                leftHandFist = false;
                leftHandOpen = false;
                leftHandOpenApart = false;
            }

            // Timeout checks for the right hand.
            rightFramesSincePacket++;

            if (rightFramesSincePacket > trackingTimeoutLimit)
            {
                rightHandTracked = false;
                rightHandFist = false;
                rightHandOpen = false;
                rightHandOpenApart = false;
            }

            if (leftDirty)
            {
                if (leftHandTracked)
                {
                    leftHandPosition = pendingLeftHandPosition;
                    leftHandFist = pendingLeftFist;
                    leftHandOpen = pendingLeftOpen;
                    leftHandOpenApart = pendingLeftOpenApart;

                    System.Array.Copy(
                        pendingLeftLandmarks,
                        leftHandLandmarks,
                        21
                    );
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

                    System.Array.Copy(
                        pendingRightLandmarks,
                        rightHandLandmarks,
                        21
                    );
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

                poseDirty = false;
            }
        }

        if (spawnJointVisualizers)
            UpdateVisualizerTransforms();
    }

    private void UpdateVisualizerTransforms()
    {
        Camera cam = Camera.main;

        if (cam == null)
            return;

        bool leftCorrect = leftHandTracked &&
            (leftHandFist || leftHandOpen || leftHandOpenApart);

        bool rightCorrect = rightHandTracked &&
            (rightHandFist || rightHandOpen || rightHandOpenApart);

        for (int i = 0; i < 21; i++)
        {
            if (leftJointTransforms[i] != null)
            {
                leftJointTransforms[i].gameObject.SetActive(leftHandTracked);

                if (leftHandTracked)
                {
                    Vector3 rawPosition = leftHandLandmarks[i];

                    leftJointTransforms[i].position =
                        cam.ViewportToWorldPoint(
                            new Vector3(
                                1f - rawPosition.x,
                                1f - rawPosition.y,
                                -cam.transform.position.z
                            )
                        );

                    Renderer rend =
                        leftJointTransforms[i].GetComponent<Renderer>();

                    if (rend != null)
                    {
                        Color targetColor = leftCorrect
                            ? Color.green
                            : Color.white;

                        rend.material.color = targetColor;
                        rend.material.EnableKeyword("_EMISSION");

                        rend.material.SetColor(
                            "_EmissionColor",
                            targetColor * (leftCorrect ? 2f : 1f)
                        );
                    }
                }
            }

            if (rightJointTransforms[i] != null)
            {
                rightJointTransforms[i].gameObject.SetActive(rightHandTracked);

                if (rightHandTracked)
                {
                    Vector3 rawPosition = rightHandLandmarks[i];

                    rightJointTransforms[i].position =
                        cam.ViewportToWorldPoint(
                            new Vector3(
                                1f - rawPosition.x,
                                1f - rawPosition.y,
                                -cam.transform.position.z
                            )
                        );

                    Renderer rend =
                        rightJointTransforms[i].GetComponent<Renderer>();

                    if (rend != null)
                    {
                        Color targetColor = rightCorrect
                            ? Color.green
                            : Color.white;

                        rend.material.color = targetColor;
                        rend.material.EnableKeyword("_EMISSION");

                        rend.material.SetColor(
                            "_EmissionColor",
                            targetColor * (rightCorrect ? 2f : 1f)
                        );
                    }
                }
            }
        }
    }

    private void HandleLeftHand(
        object sender,
        OutputStream<NormalizedLandmarkList>.OutputEventArgs eventArgs
    )
    {
        leftHandPacketCount++;

        NormalizedLandmarkList landmarkList =
            ExtractNormalizedLandmarks(eventArgs.packet);

        lock (dataLock)
        {
            leftFramesSincePacket = 0;

            if (landmarkList != null &&
                landmarkList.Landmark.Count >= 21)
            {
                leftHandTracked = true;

                // FIX: save the center of the palm, not landmark 0 (wrist).
                pendingLeftHandPosition = GetPalmCenter(landmarkList);

                pendingLeftFist = IsFist(landmarkList);
                pendingLeftOpen = IsOpenHand(landmarkList);
                pendingLeftOpenApart = IsOpenHandApart(landmarkList);

                for (int i = 0; i < 21; i++)
                {
                    pendingLeftLandmarks[i] =
                        ToUnityVector(landmarkList.Landmark[i]);
                }
            }
            else
            {
                leftHandTracked = false;
                pendingLeftFist = false;
                pendingLeftOpen = false;
                pendingLeftOpenApart = false;
            }

            leftDirty = true;
        }
    }

    private void HandleRightHand(
        object sender,
        OutputStream<NormalizedLandmarkList>.OutputEventArgs eventArgs
    )
    {
        rightHandPacketCount++;

        NormalizedLandmarkList landmarkList =
            ExtractNormalizedLandmarks(eventArgs.packet);

        lock (dataLock)
        {
            rightFramesSincePacket = 0;

            if (landmarkList != null &&
                landmarkList.Landmark.Count >= 21)
            {
                rightHandTracked = true;

                // FIX: save the center of the palm, not landmark 0 (wrist).
                pendingRightHandPosition = GetPalmCenter(landmarkList);

                pendingRightFist = IsFist(landmarkList);
                pendingRightOpen = IsOpenHand(landmarkList);
                pendingRightOpenApart = IsOpenHandApart(landmarkList);

                for (int i = 0; i < 21; i++)
                {
                    pendingRightLandmarks[i] =
                        ToUnityVector(landmarkList.Landmark[i]);
                }
            }
            else
            {
                rightHandTracked = false;
                pendingRightFist = false;
                pendingRightOpen = false;
                pendingRightOpenApart = false;
            }

            rightDirty = true;
        }
    }

    // Average wrist and the four finger-base knuckles to estimate
    // the middle of the palm in normalized camera coordinates.
    private Vector3 GetPalmCenter(NormalizedLandmarkList landmarks)
    {
        return (
            ToUnityVector(landmarks.Landmark[0]) +
            ToUnityVector(landmarks.Landmark[5]) +
            ToUnityVector(landmarks.Landmark[9]) +
            ToUnityVector(landmarks.Landmark[13]) +
            ToUnityVector(landmarks.Landmark[17])
        ) / 5f;
    }

    private void HandlePoseWorld(
        object sender,
        OutputStream<LandmarkList>.OutputEventArgs eventArgs
    )
    {
        posePacketCount++;

        LandmarkList worldLandmarks =
            ExtractWorldLandmarks(eventArgs.packet);

        lock (dataLock)
        {
            if (worldLandmarks != null &&
                worldLandmarks.Landmark.Count >= 33)
            {
                poseTracked = true;

                pendingHeadPosition =
                    ToUnityVector(worldLandmarks.Landmark[0]);

                Vector3 leftShoulder =
                    ToUnityVector(worldLandmarks.Landmark[11]);

                Vector3 rightShoulder =
                    ToUnityVector(worldLandmarks.Landmark[12]);

                Vector3 leftHip =
                    ToUnityVector(worldLandmarks.Landmark[23]);

                Vector3 rightHip =
                    ToUnityVector(worldLandmarks.Landmark[24]);

                pendingTorsoPosition =
                    (
                        (leftShoulder + rightShoulder) * 0.5f +
                        (leftHip + rightHip) * 0.5f
                    ) * 0.5f;
            }
            else
            {
                poseTracked = false;
            }

            poseDirty = true;
        }
    }

    private NormalizedLandmarkList ExtractNormalizedLandmarks(
        Packet<NormalizedLandmarkList> packet
    )
    {
        return packet?.Get(NormalizedLandmarkList.Parser);
    }

    private LandmarkList ExtractWorldLandmarks(Packet<LandmarkList> packet)
    {
        return packet?.Get(LandmarkList.Parser);
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
            return false;

        int curledFingersCount = 0;

        if (IsFingerCurled(landmarks, 5, 6, 8))
            curledFingersCount++;

        if (IsFingerCurled(landmarks, 9, 10, 12))
            curledFingersCount++;

        if (IsFingerCurled(landmarks, 13, 14, 16))
            curledFingersCount++;

        if (IsFingerCurled(landmarks, 17, 18, 20))
            curledFingersCount++;

        return curledFingersCount == 4;
    }

    private bool IsFingerCurled(
        NormalizedLandmarkList landmarks,
        int mcpIndex,
        int pipIndex,
        int tipIndex
    )
    {
        Vector3 wrist = ToUnityVector(landmarks.Landmark[0]);
        Vector3 mcp = ToUnityVector(landmarks.Landmark[mcpIndex]);
        Vector3 pip = ToUnityVector(landmarks.Landmark[pipIndex]);
        Vector3 tip = ToUnityVector(landmarks.Landmark[tipIndex]);

        float tipToWrist = Vector3.Distance(tip, wrist);
        float mcpToWrist = Vector3.Distance(mcp, wrist);

        bool closerToWrist = tipToWrist < mcpToWrist * 0.95f;

        Vector3 segment1 = (pip - mcp).normalized;
        Vector3 segment2 = (tip - pip).normalized;

        bool isBent = Vector3.Dot(segment1, segment2) < 0.4f;

        return closerToWrist && isBent;
    }

    private bool IsOpenHand(NormalizedLandmarkList landmarks)
    {
        if (landmarks.Landmark.Count < 21)
            return false;

        return IsFingerExtended(landmarks, 5, 6, 8) &&
               IsFingerExtended(landmarks, 9, 10, 12) &&
               IsFingerExtended(landmarks, 13, 14, 16) &&
               IsFingerExtended(landmarks, 17, 18, 20) &&
               !IsOpenHandApart(landmarks);
    }

    private bool IsOpenHandApart(NormalizedLandmarkList landmarks)
    {
        if (landmarks.Landmark.Count < 21)
            return false;

        if (!IsFingerExtended(landmarks, 5, 6, 8) ||
            !IsFingerExtended(landmarks, 9, 10, 12) ||
            !IsFingerExtended(landmarks, 13, 14, 16) ||
            !IsFingerExtended(landmarks, 17, 18, 20))
        {
            return false;
        }

        Vector3 thumbTip = ToUnityVector(landmarks.Landmark[4]);
        Vector3 indexTip = ToUnityVector(landmarks.Landmark[8]);
        Vector3 middleTip = ToUnityVector(landmarks.Landmark[12]);
        Vector3 ringTip = ToUnityVector(landmarks.Landmark[16]);
        Vector3 pinkyTip = ToUnityVector(landmarks.Landmark[20]);

        float thumbIndexDist = Vector3.Distance(thumbTip, indexTip);
        float indexMiddleDist = Vector3.Distance(indexTip, middleTip);
        float middleRingDist = Vector3.Distance(middleTip, ringTip);
        float ringPinkyDist = Vector3.Distance(ringTip, pinkyTip);

        int spreadCount = 0;

        if (thumbIndexDist > openApartThreshold)
            spreadCount++;

        if (indexMiddleDist > openApartThreshold)
            spreadCount++;

        if (middleRingDist > openApartThreshold)
            spreadCount++;

        if (ringPinkyDist > openApartThreshold)
            spreadCount++;

        return spreadCount >= 3;
    }

    private bool IsFingerExtended(
        NormalizedLandmarkList landmarks,
        int mcpIndex,
        int pipIndex,
        int tipIndex
    )
    {
        Vector3 wrist = ToUnityVector(landmarks.Landmark[0]);
        Vector3 mcp = ToUnityVector(landmarks.Landmark[mcpIndex]);
        Vector3 pip = ToUnityVector(landmarks.Landmark[pipIndex]);
        Vector3 tip = ToUnityVector(landmarks.Landmark[tipIndex]);

        Vector3 segment1 = (pip - mcp).normalized;
        Vector3 segment2 = (tip - pip).normalized;

        float segmentAlignment = Vector3.Dot(segment1, segment2);
        bool isStraight = segmentAlignment > 0.85f;

        bool isExtendedOut =
            Vector3.Distance(tip, wrist) >
            Vector3.Distance(mcp, wrist);

        return isExtendedOut && isStraight;
    }

    public void StopTracking()
    {
        OnDisable();
    }
}
