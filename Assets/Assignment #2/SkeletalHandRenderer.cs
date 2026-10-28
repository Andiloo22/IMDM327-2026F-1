using UnityEngine;
using UnityEngine.Rendering;

public class SkeletalHandRenderer : MonoBehaviour
{
    [Header("Tracking")]
    public MediaPipeBodyTracker tracker;
    public Camera trackingCamera;

    [Header("Hands")]
    public bool showLeftHand = true;
    public bool showRightHand = true;

    [Tooltip("Mirror the hand horizontally to match a mirrored webcam.")]
    public bool mirrorX = true;

    [Tooltip("Flip the vertical landmark coordinates.")]
    public bool invertY = true;

    [Tooltip("Camera distance to the hand's drawing plane. Set to 0 to auto-detect.")]
    public float depthFromCamera = 0f;

    [Header("Skeleton Appearance")]
    [Tooltip("Width of the dark outline around each bone.")]
    public float outlineWidth = 0.045f;

    [Tooltip("Width of the ivory bone inside the outline.")]
    public float boneWidth = 0.025f;

    public Color outlineColor = new Color(0.09f, 0.065f, 0.07f, 1f);
    public Color boneColor = new Color(0.68f, 0.59f, 0.45f, 1f);

    private static readonly Vector2Int[] Connections =
    {
        // Thumb
        new Vector2Int(0, 1),
        new Vector2Int(1, 2),
        new Vector2Int(2, 3),
        new Vector2Int(3, 4),

        // Index finger
        new Vector2Int(0, 5),
        new Vector2Int(5, 6),
        new Vector2Int(6, 7),
        new Vector2Int(7, 8),

        // Middle finger
        new Vector2Int(0, 9),
        new Vector2Int(9, 10),
        new Vector2Int(10, 11),
        new Vector2Int(11, 12),

        // Ring finger
        new Vector2Int(0, 13),
        new Vector2Int(13, 14),
        new Vector2Int(14, 15),
        new Vector2Int(15, 16),

        // Pinky
        new Vector2Int(0, 17),
        new Vector2Int(17, 18),
        new Vector2Int(18, 19),
        new Vector2Int(19, 20),

        // Palm crossbones
        new Vector2Int(5, 9),
        new Vector2Int(9, 13),
        new Vector2Int(13, 17)
    };

    private class BoneLine
    {
        public LineRenderer outline;
        public LineRenderer bone;
    }

    private BoneLine[] leftBones;
    private BoneLine[] rightBones;

    private readonly Vector3[] leftLandmarks = new Vector3[21];
    private readonly Vector3[] rightLandmarks = new Vector3[21];

    private Material outlineMaterial;
    private Material boneMaterial;

    private void Awake()
    {
        if (tracker == null)
            tracker = GetComponent<MediaPipeBodyTracker>();

        if (trackingCamera == null)
            trackingCamera = Camera.main;

        CreateMaterials();

        leftBones = CreateHandBones("LeftHand");
        rightBones = CreateHandBones("RightHand");
    }

    private void LateUpdate()
    {
        if (tracker == null)
        {
            SetBonesVisible(leftBones, false);
            SetBonesVisible(rightBones, false);
            return;
        }

        if (trackingCamera == null)
            trackingCamera = Camera.main;

        bool leftTracked =
            showLeftHand && tracker.LeftHandTracked;

        bool rightTracked =
            showRightHand && tracker.RightHandTracked;

        if (leftTracked && trackingCamera != null)
        {
            tracker.GetLeftHandLandmarks(leftLandmarks);

            UpdateHandBones(
                leftBones,
                leftLandmarks,
                true
            );
        }
        else
        {
            SetBonesVisible(leftBones, false);
        }

        if (rightTracked && trackingCamera != null)
        {
            tracker.GetRightHandLandmarks(rightLandmarks);

            UpdateHandBones(
                rightBones,
                rightLandmarks,
                true
            );
        }
        else
        {
            SetBonesVisible(rightBones, false);
        }
    }

    private void CreateMaterials()
    {
        Shader shader = Shader.Find("Sprites/Default");

        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        if (shader == null)
        {
            Debug.LogError(
                "SkeletalHandRenderer: No compatible shader was found."
            );
            enabled = false;
            return;
        }

        outlineMaterial = new Material(shader);
        outlineMaterial.color = Color.white;
        outlineMaterial.name = "Skeleton Dark Outline";

        boneMaterial = new Material(shader);
        boneMaterial.color = Color.white;
        boneMaterial.name = "Skeleton Aged Bone";
    }

    private BoneLine[] CreateHandBones(string handName)
    {
        BoneLine[] lines = new BoneLine[Connections.Length];

        for (int i = 0; i < Connections.Length; i++)
        {
            GameObject boneObject = new GameObject(
                handName + "_Bone_" + i
            );

            boneObject.transform.SetParent(transform, false);

            LineRenderer outline =
                boneObject.AddComponent<LineRenderer>();

            ConfigureLine(
                outline,
                outlineMaterial,
                outlineWidth,
                outlineColor,
                0
            );

            GameObject innerObject = new GameObject(
                handName + "_BoneCore_" + i
            );

            innerObject.transform.SetParent(
                boneObject.transform,
                false
            );

            LineRenderer bone =
                innerObject.AddComponent<LineRenderer>();

            ConfigureLine(
                bone,
                boneMaterial,
                boneWidth,
                boneColor,
                1
            );

            lines[i] = new BoneLine
            {
                outline = outline,
                bone = bone
            };
        }

        return lines;
    }

    private void ConfigureLine(
        LineRenderer line,
        Material material,
        float width,
        Color color,
        int sortingOrder)
    {
        line.useWorldSpace = true;
        line.positionCount = 2;

        line.sharedMaterial = material;

        line.startWidth = width;
        line.endWidth = width;

        line.startColor = color;
        line.endColor = color;

        line.numCapVertices = 4;
        line.numCornerVertices = 4;

        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;

        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sortingOrder = sortingOrder;

        line.enabled = false;
    }

    private void UpdateHandBones(
        BoneLine[] lines,
        Vector3[] landmarks,
        bool tracked)
    {
        if (!tracked || trackingCamera == null)
        {
            SetBonesVisible(lines, false);
            return;
        }

        for (int i = 0; i < Connections.Length; i++)
        {
            int startIndex = Connections[i].x;
            int endIndex = Connections[i].y;

            Vector3 start = LandmarkToWorld(
                landmarks[startIndex]
            );

            Vector3 end = LandmarkToWorld(
                landmarks[endIndex]
            );

            BoneLine line = lines[i];

            line.outline.SetPosition(0, start);
            line.outline.SetPosition(1, end);

            line.bone.SetPosition(0, start);
            line.bone.SetPosition(1, end);

            line.outline.startWidth = outlineWidth;
            line.outline.endWidth = outlineWidth;

            line.bone.startWidth = boneWidth;
            line.bone.endWidth = boneWidth;

            line.outline.enabled = true;
            line.bone.enabled = true;
        }
    }

    private Vector3 LandmarkToWorld(Vector3 landmark)
    {
        float x = mirrorX
            ? 1f - landmark.x
            : landmark.x;

        float y = invertY
            ? 1f - landmark.y
            : landmark.y;

        float depth = depthFromCamera;

        if (depth <= 0f)
        {
            depth = Mathf.Abs(
                trackingCamera.transform.position.z
            );

            if (depth < 0.01f)
                depth = 10f;
        }

        return trackingCamera.ViewportToWorldPoint(
            new Vector3(x, y, depth)
        );
    }

    private void SetBonesVisible(
        BoneLine[] lines,
        bool visible)
    {
        if (lines == null)
            return;

        foreach (BoneLine line in lines)
        {
            if (line == null)
                continue;

            if (line.outline != null)
                line.outline.enabled = visible;

            if (line.bone != null)
                line.bone.enabled = visible;
        }
    }

    private void OnDestroy()
    {
        if (outlineMaterial != null)
            Destroy(outlineMaterial);

        if (boneMaterial != null)
            Destroy(boneMaterial);
    }
}