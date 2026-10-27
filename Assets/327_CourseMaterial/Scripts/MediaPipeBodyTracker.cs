
using System.Collections;
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

    [Header("Hand Visualizers")]
    [SerializeField] private bool spawnJointVisualizers = false;
    [SerializeField] private GameObject jointPrefab;
    [SerializeField] private Transform leftHandParentTransform;
    [SerializeField] private Transform rightHandParentTransform;

    [Header("Hand Color Fade")]
    [SerializeField, Min(0.1f)]
    private float handColorFadeSpeed = 8f;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = false;
    [SerializeField] private int leftHandPacketCount;
    [SerializeField] private int rightHandPacketCount;
    [SerializeField] private int posePacketCount;

    [Header("Detection Settings")]
    [SerializeField, Range(0.001f, 0.2f)]
    private float openApartThreshold = 0.05f;

    private readonly Vector3[] leftHandLandmarks = new Vector3[21];
    private readonly Vector3[] rightHandLandmarks = new Vector3[21];
    private readonly Vector3[] pendingLeftLandmarks = new Vector3[21];
    private readonly Vector3[] pendingRightLandmarks = new Vector3[21];

    private Transform[] leftJointTransforms = new Transform[21];
    private Transform[] rightJointTransforms = new Transform[21];

    // Cache the joint materials so their colors can be smoothly animated.
    private Material[] leftJointMaterials = new Material[21];
    private Material[] rightJointMaterials = new Material[21];

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

    private int leftFramesSincePacket;
    private int rightFramesSincePacket;
    private const int TrackingTimeoutLimit = 15;

    private Coroutine subscribeRoutine;
    private bool hasSubscriptions;
    private HolisticTrackingGraph subscribedRunner;

    private bool interactionLeftActive;
    private bool interactionRightActive;

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

    public void SetInteractionActionActive(
        bool leftActive,
        bool rightActive
    )
    {
        interactionLeftActive = leftActive;
        interactionRightActive = rightActive;
    }

    public void GetLeftHandLandmarks(Vector3[] buffer)
    {
        if (buffer == null || buffer.Length < 21)
            return;

        lock (dataLock)
        {
            System.Array.Copy(leftHandLandmarks, buffer, 21);
        }
    }

    public void GetRightHandLandmarks(Vector3[] buffer)
    {
        if (buffer == null || buffer.Length < 21)
            return;

        lock (dataLock)
        {
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
            leftHandParentTransform =
                new GameObject("LeftHand_MetaballRoot").transform;

        if (rightHandParentTransform == null)
            rightHandParentTransform =
                new GameObject("RightHand_MetaballRoot").transform;

        for (int i = 0; i < 21; i++)
        {
            GameObject leftObject =
                Instantiate(jointPrefab, leftHandParentTransform);

            leftJointTransforms[i] = leftObject.transform;
            leftJointMaterials[i] = GetJointMaterial(leftObject);
            leftObject.SetActive(false);

            GameObject rightObject =
                Instantiate(jointPrefab, rightHandParentTransform);

            rightJointTransforms[i] = rightObject.transform;
            rightJointMaterials[i] = GetJointMaterial(rightObject);
            rightObject.SetActive(false);
        }
    }

    private Material GetJointMaterial(GameObject jointObject)
    {
        Renderer renderer = jointObject.GetComponent<Renderer>();

        if (renderer == null)
            renderer = jointObject.GetComponentInChildren<Renderer>();

        if (renderer == null)
            return null;

        // Make a unique instance so each joint can be animated independently.
        return renderer.material;
    }

    private void OnEnable()
    {
        if (graphRunner == null)
            graphRunner = FindObjectOfType<HolisticTrackingGraph>();

        if (graphRunner == null)
        {
            Debug.LogWarning(
                $"{nameof(MediaPipeBodyTracker)} on {name} could not find " +
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
            rightHandFist = false;
            leftHandOpen = false;
            rightHandOpen = false;
            leftHandOpenApart = false;
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

        interactionLeftActive = false;
        interactionRightActive = false;
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
            leftFramesSincePacket++;

            if (leftFramesSincePacket > TrackingTimeoutLimit)
            {
                leftHandTracked = false;
                leftHandFist = false;
                leftHandOpen = false;
                leftHandOpenApart = false;
            }

            rightFramesSincePacket++;

            if (rightFramesSincePacket > TrackingTimeoutLimit)
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
            (interactionLeftActive || leftHandOpenApart);

        bool rightCorrect = rightHandTracked &&
            (interactionRightActive || rightHandOpenApart);

        float blend = 1f - Mathf.Exp(
            -handColorFadeSpeed * Time.deltaTime
        );

        for (int i = 0; i < 21; i++)
        {
            UpdateOneVisualizer(
                leftJointTransforms[i],
                leftJointMaterials[i],
                leftHandLandmarks[i],
                leftHandTracked,
                leftCorrect,
                cam,
                blend
            );

            UpdateOneVisualizer(
                rightJointTransforms[i],
                rightJointMaterials[i],
                rightHandLandmarks[i],
                rightHandTracked,
                rightCorrect,
                cam,
                blend
            );
        }
    }

    private void UpdateOneVisualizer(
        Transform joint,
        Material jointMaterial,
        Vector3 rawPosition,
        bool tracked,
        bool active,
        Camera cam,
        float blend
    )
    {
        if (joint == null)
            return;

        joint.gameObject.SetActive(tracked);

        if (!tracked)
            return;

        joint.position = cam.ViewportToWorldPoint(
            new Vector3(
                1f - rawPosition.x,
                1f - rawPosition.y,
                -cam.transform.position.z
            )
        );

        if (jointMaterial == null)
            return;

        Color targetColor = active ? Color.green : Color.white;

        jointMaterial.color = Color.Lerp(
            jointMaterial.color,
            targetColor,
            blend
        );

        if (jointMaterial.HasProperty("_EmissionColor"))
        {
            Color targetEmission =
                targetColor * (active ? 2f : 1f);

            Color currentEmission =
                jointMaterial.GetColor("_EmissionColor");

            jointMaterial.EnableKeyword("_EMISSION");

            jointMaterial.SetColor(
                "_EmissionColor",
                Color.Lerp(currentEmission, targetEmission, blend)
            );
        }
    }

    private void HandleLeftHand(
        object sender,
        OutputStream<NormalizedLandmarkList>.OutputEventArgs eventArgs
    )
    {
        leftHandPacketCount++;

        NormalizedLandmarkList landmarks =
            ExtractNormalizedLandmarks(eventArgs.packet);

        lock (dataLock)
        {
            leftFramesSincePacket = 0;

            if (landmarks != null && landmarks.Landmark.Count >= 21)
            {
                leftHandTracked = true;
                pendingLeftHandPosition = GetPalmCenter(landmarks);

                pendingLeftFist = IsFist(landmarks);
                pendingLeftOpen = IsOpenHand(landmarks);
                pendingLeftOpenApart = IsOpenHandApart(landmarks);

                for (int i = 0; i < 21; i++)
                    pendingLeftLandmarks[i] =
                        ToUnityVector(landmarks.Landmark[i]);
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

        NormalizedLandmarkList landmarks =
            ExtractNormalizedLandmarks(eventArgs.packet);

        lock (dataLock)
        {
            rightFramesSincePacket = 0;

            if (landmarks != null && landmarks.Landmark.Count >= 21)
            {
                rightHandTracked = true;
                pendingRightHandPosition = GetPalmCenter(landmarks);

                pendingRightFist = IsFist(landmarks);
                pendingRightOpen = IsOpenHand(landmarks);
                pendingRightOpenApart = IsOpenHandApart(landmarks);

                for (int i = 0; i < 21; i++)
                    pendingRightLandmarks[i] =
                        ToUnityVector(landmarks.Landmark[i]);
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

        LandmarkList landmarks = ExtractWorldLandmarks(eventArgs.packet);

        lock (dataLock)
        {
            if (landmarks != null && landmarks.Landmark.Count >= 33)
            {
                poseTracked = true;
                pendingHeadPosition =
                    ToUnityVector(landmarks.Landmark[0]);

                Vector3 leftShoulder =
                    ToUnityVector(landmarks.Landmark[11]);
                Vector3 rightShoulder =
                    ToUnityVector(landmarks.Landmark[12]);
                Vector3 leftHip =
                    ToUnityVector(landmarks.Landmark[23]);
                Vector3 rightHip =
                    ToUnityVector(landmarks.Landmark[24]);

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

        bool isStraight = Vector3.Dot(segment1, segment2) > 0.85f;

        bool isExtendedOut =
            Vector3.Distance(tip, wrist) >
            Vector3.Distance(mcp, wrist);

        return isExtendedOut && isStraight;
    }

    private void OnDestroy()
    {
        for (int i = 0; i < 21; i++)
        {
            if (leftJointMaterials[i] != null)
                Destroy(leftJointMaterials[i]);

            if (rightJointMaterials[i] != null)
                Destroy(rightJointMaterials[i]);
        }
    }

    public void StopTracking()
    {
        OnDisable();
    }
}
