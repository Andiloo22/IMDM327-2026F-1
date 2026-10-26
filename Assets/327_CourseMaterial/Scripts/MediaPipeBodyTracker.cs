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
    [SerializeField, Range(0.001f, 0.2f)] private float fistThreshold = 0.035f;
    [SerializeField, Range(0.001f, 0.2f)] private float openApartThreshold = 0.05f;

    // Full 21 Landmark Arrays
    private readonly Vector3[] leftHandLandmarks = new Vector3[21];
    private readonly Vector3[] rightHandLandmarks = new Vector3[21];
    private readonly Vector3[] pendingLeftLandmarks = new Vector3[21];
    private readonly Vector3[] pendingRightLandmarks = new Vector3[21];

    // Visualizer instance tracking
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
            if (buffer == null || buffer.Length < 21) return;
            System.Array.Copy(leftHandLandmarks, buffer, 21);
        }
    }

    public void GetRightHandLandmarks(Vector3[] buffer)
    {
        lock (dataLock)
        {
            if (buffer == null || buffer.Length < 21) return;
            System.Array.Copy(rightHandLandmarks, buffer, 21);
        }
    }

    private void Awake()
    {
        if (spawnJointVisualizers && jointPrefab != null)
        {
            InitializeVisualizers();
        }
    }

    private void InitializeVisualizers()
    {
        if (leftHandParentTransform == null) leftHandParentTransform = new GameObject("LeftHand_MetaballRoot").transform;
        if (rightHandParentTransform == null) rightHandParentTransform = new GameObject("RightHand_MetaballRoot").transform;

        for (int i = 0; i < 21; i++)
        {
            var lObj = Instantiate(jointPrefab, leftHandParentTransform);
            leftJointTransforms[i] = lObj.transform;
            lObj.gameObject.SetActive(false);

            var rObj = Instantiate(jointPrefab, rightHandParentTransform);
            rightJointTransforms[i] = rObj.transform;
            rObj.gameObject.SetActive(false);
        }
    }

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
        if (graphRunner == null || hasSubscriptions) return hasSubscriptions;

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
        if (!hasSubscriptions) return;
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
                    System.Array.Copy(pendingLeftLandmarks, leftHandLandmarks, 21);
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
                    System.Array.Copy(pendingRightLandmarks, rightHandLandmarks, 21);
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

        // Update visualizer positions if enabled
        if (spawnJointVisualizers)
        {
            UpdateVisualizerTransforms();
        }
    }

    private void UpdateVisualizerTransforms()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        // Check if ANY of the boolean flags are active for each hand
        bool leftCorrect = leftHandTracked && (leftHandFist || leftHandOpen || leftHandOpenApart);
        bool rightCorrect = rightHandTracked && (rightHandFist || rightHandOpen || rightHandOpenApart);

        for (int i = 0; i < 21; i++)
        {
            if (leftJointTransforms[i] != null)
            {
                leftJointTransforms[i].gameObject.SetActive(leftHandTracked);
                if (leftHandTracked)
                {
                    Vector3 rawPos = leftHandLandmarks[i];
                    leftJointTransforms[i].position = cam.ViewportToWorldPoint(new Vector3(1f - rawPos.x, 1f - rawPos.y, -cam.transform.position.z));

                    var rend = leftJointTransforms[i].GetComponent<Renderer>();
                    if (rend != null)
                    {
                        Color targetColor = leftCorrect ? Color.green : Color.white;
                        rend.material.color = targetColor;

                        // Enable emission and update emission color for glowing shaders (like Universal Render Pipeline / Standard)
                        rend.material.EnableKeyword("_EMISSION");
                        rend.material.SetColor("_EmissionColor", targetColor * (leftCorrect ? 2f : 1f)); // Optional: multiplies intensity when green
                    }
                }
            }

            if (rightJointTransforms[i] != null)
            {
                rightJointTransforms[i].gameObject.SetActive(rightHandTracked);
                if (rightHandTracked)
                {
                    Vector3 rawPos = rightHandLandmarks[i];
                    rightJointTransforms[i].position = cam.ViewportToWorldPoint(new Vector3(1f - rawPos.x, 1f - rawPos.y, -cam.transform.position.z));

                    var rend = rightJointTransforms[i].GetComponent<Renderer>();
                    if (rend != null)
                    {
                        Color targetColor = rightCorrect ? Color.green : Color.white;
                        rend.material.color = targetColor;

                        // Enable emission and update emission color
                        rend.material.EnableKeyword("_EMISSION");
                        rend.material.SetColor("_EmissionColor", targetColor * (rightCorrect ? 2f : 1f));
                    }
                }
            }
        }
    }

    private void HandleLeftHand(object sender, OutputStream<NormalizedLandmarkList>.OutputEventArgs eventArgs)
    {
        leftHandPacketCount++;
        var landmarkList = ExtractNormalizedLandmarks(eventArgs.packet);

        lock (dataLock)
        {
            if (landmarkList != null && landmarkList.Landmark.Count >= 21)
            {
                leftHandTracked = true;
                pendingLeftHandPosition = ToUnityVector(landmarkList.Landmark[0]);
                pendingLeftFist = IsFist(landmarkList);
                pendingLeftOpen = IsOpenHand(landmarkList);
                pendingLeftOpenApart = IsOpenHandApart(landmarkList);

                for (int i = 0; i < 21; i++)
                {
                    pendingLeftLandmarks[i] = ToUnityVector(landmarkList.Landmark[i]);
                }
            }
            else
            {
                leftHandTracked = false;
            }
            leftDirty = true;
        }
    }

    private void HandleRightHand(object sender, OutputStream<NormalizedLandmarkList>.OutputEventArgs eventArgs)
    {
        rightHandPacketCount++;
        var landmarkList = ExtractNormalizedLandmarks(eventArgs.packet);

        lock (dataLock)
        {
            if (landmarkList != null && landmarkList.Landmark.Count >= 21)
            {
                rightHandTracked = true;
                pendingRightHandPosition = ToUnityVector(landmarkList.Landmark[0]);
                pendingRightFist = IsFist(landmarkList);
                pendingRightOpen = IsOpenHand(landmarkList);
                pendingRightOpenApart = IsOpenHandApart(landmarkList);

                for (int i = 0; i < 21; i++)
                {
                    pendingRightLandmarks[i] = ToUnityVector(landmarkList.Landmark[i]);
                }
            }
            else
            {
                rightHandTracked = false;
            }
            rightDirty = true;
        }
    }

    private void HandlePoseWorld(object sender, OutputStream<LandmarkList>.OutputEventArgs eventArgs)
    {
        posePacketCount++;
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

                pendingTorsoPosition = ((leftShoulder + rightShoulder) * 0.5f + (leftHip + rightHip) * 0.5f) * 0.5f;
            }
            else
            {
                poseTracked = false;
            }
            poseDirty = true;
        }
    }

    private NormalizedLandmarkList ExtractNormalizedLandmarks(Packet<NormalizedLandmarkList> packet) => packet?.Get(NormalizedLandmarkList.Parser);
    private LandmarkList ExtractWorldLandmarks(Packet<LandmarkList> packet) => packet?.Get(LandmarkList.Parser);
    private Vector3 ToUnityVector(NormalizedLandmark l) => new Vector3(l.X, l.Y, l.Z);
    private Vector3 ToUnityVector(Landmark l) => new Vector3(l.X, l.Y, l.Z);

    private bool IsFist(NormalizedLandmarkList landmarks)
    {
        int curled = 0;
        if (IsFingerCurled(landmarks, 8, 6)) curled++;
        if (IsFingerCurled(landmarks, 12, 10)) curled++;
        if (IsFingerCurled(landmarks, 16, 14)) curled++;
        if (IsFingerCurled(landmarks, 20, 18)) curled++;
        return curled >= 3;
    }

    private bool IsFingerCurled(NormalizedLandmarkList landmarks, int tipIndex, int pipIndex)
    {
        var wrist = landmarks.Landmark[0];
        float tipToWrist = Vector3.Distance(ToUnityVector(landmarks.Landmark[tipIndex]), ToUnityVector(wrist));
        float pipToWrist = Vector3.Distance(ToUnityVector(landmarks.Landmark[pipIndex]), ToUnityVector(wrist));
        return (tipToWrist < pipToWrist) || (tipToWrist < fistThreshold);
    }

    private bool IsOpenHand(NormalizedLandmarkList landmarks) =>
        IsFingerExtended(landmarks, 8, 6) && IsFingerExtended(landmarks, 12, 10) &&
        IsFingerExtended(landmarks, 16, 14) && IsFingerExtended(landmarks, 20, 18) && !IsOpenHandApart(landmarks);

    private bool IsOpenHandApart(NormalizedLandmarkList landmarks)
    {
        if (!IsFingerExtended(landmarks, 8, 6) || !IsFingerExtended(landmarks, 12, 10) ||
            !IsFingerExtended(landmarks, 16, 14) || !IsFingerExtended(landmarks, 20, 18)) return false;

        int spread = 0;
        if (Vector3.Distance(ToUnityVector(landmarks.Landmark[4]), ToUnityVector(landmarks.Landmark[8])) > openApartThreshold) spread++;
        if (Vector3.Distance(ToUnityVector(landmarks.Landmark[8]), ToUnityVector(landmarks.Landmark[12])) > openApartThreshold) spread++;
        if (Vector3.Distance(ToUnityVector(landmarks.Landmark[12]), ToUnityVector(landmarks.Landmark[16])) > openApartThreshold) spread++;
        if (Vector3.Distance(ToUnityVector(landmarks.Landmark[16]), ToUnityVector(landmarks.Landmark[20])) > openApartThreshold) spread++;
        return spread >= 3;
    }

    private bool IsFingerExtended(NormalizedLandmarkList landmarks, int tipIndex, int pipIndex)
    {
        var wrist = landmarks.Landmark[0];
        float tipToWrist = Vector3.Distance(ToUnityVector(landmarks.Landmark[tipIndex]), ToUnityVector(wrist));
        float pipToWrist = Vector3.Distance(ToUnityVector(landmarks.Landmark[pipIndex]), ToUnityVector(wrist));
        return tipToWrist > pipToWrist;
    }

    public void StopTracking()
    {
        OnDisable();
    }
}