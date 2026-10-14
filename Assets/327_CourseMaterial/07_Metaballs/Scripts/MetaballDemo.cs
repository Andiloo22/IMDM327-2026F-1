using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

public class MetaballDemo : MonoBehaviour
{
    public enum Mode { UV, Distance, OneInfluence, CombinedField, Threshold, SmoothEdge, Metaballs }
    public Mode mode = Mode.Metaballs;
    public Renderer output;
    public Transform objectPanel;
    public Transform[] centers, disks, leftMarkers;
    [Range(0.04f, 0.3f)] public float radius = 0.16f;
    [Range(0.2f, 1.8f)] public float threshold = 0.82f;
    [Range(0.001f, 0.1f)] public float edgeWidth = 0.03f;
    public float speed = 0.7f;
    public bool animate, showCenters = true, showGrid;
    public int samples;
    public int selected;

    Camera cam;
    MaterialPropertyBlock properties;
    Transform dragPanel;
    int dragging = -1;
    float time;
    Key[] modeKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7 };
    string[] centerNames = { "_Center0", "_Center1", "_Center2" };

    void Start()
    {
        cam = Camera.main;
        properties = new MaterialPropertyBlock();
        ResetPositions();
        Draw();
    }

    void Update()
    {
        cam.orthographicSize = Mathf.Max(4.2f, 7.2f / cam.aspect);
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            for (int i = 0; i < modeKeys.Length; i++)
                if (keyboard[modeKeys[i]].wasPressedThisFrame) mode = (Mode)i;
            if (keyboard.spaceKey.wasPressedThisFrame) animate = !animate;
            if (keyboard.rKey.wasPressedThisFrame) ResetPositions();
            if (keyboard.nKey.wasPressedThisFrame) Step();
            if (keyboard.cKey.wasPressedThisFrame) showCenters = !showCenters;
            if (keyboard.gKey.wasPressedThisFrame) showGrid = !showGrid;
            if (keyboard.qKey.wasPressedThisFrame) samples = samples == 0 ? 16 : samples == 16 ? 32 : samples == 32 ? 64 : 0;
            if (keyboard.tabKey.wasPressedThisFrame) selected = (selected + 1) % 3;
            Vector3 movement = Vector3.zero;
            if (keyboard.leftArrowKey.isPressed) movement.x--;
            if (keyboard.rightArrowKey.isPressed) movement.x++;
            if (keyboard.downArrowKey.isPressed) movement.y--;
            if (keyboard.upArrowKey.isPressed) movement.y++;
            if (movement.sqrMagnitude > 0f)
            {
                animate = false;
                centers[selected].localPosition += movement * Time.deltaTime * 0.25f;
            }
        }
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            if (mouse.leftButton.wasPressedThisFrame && !EventSystem.current.IsPointerOverGameObject())
            {
                Vector2 p;
                dragPanel = output.transform;
                if (!PointerPoint(dragPanel, out p)) dragPanel = objectPanel;
                if (PointerPoint(dragPanel, out p))
                {
                    for (int i = 0; i < 3; i++)
                        if (Vector2.Distance(p, Center(i)) < 0.06f) { dragging = selected = i; animate = false; break; }
                }
            }
            if (mouse.leftButton.isPressed && dragging >= 0)
            {
                PointerPoint(dragPanel, out Vector2 p);
                centers[dragging].localPosition = new Vector3(Mathf.Clamp01(p.x) - 0.5f, Mathf.Clamp01(p.y) - 0.5f, -0.04f);
            }
            if (mouse.leftButton.wasReleasedThisFrame) dragging = -1;
        }
        if (animate) MoveCenters(Time.deltaTime);
        Draw();
    }

    void MoveCenters(float delta)
    {
        float before = Mathf.Sin(time);
        time += delta * speed;
        float shift = (Mathf.Sin(time) - before) * 0.15f;
        centers[0].localPosition += Vector3.right * shift;
        centers[1].localPosition -= Vector3.right * shift;
        centers[2].localPosition += Vector3.up * shift * 0.5f;
    }

    public void Step() { animate = false; MoveCenters(1f / 30f); }

    public void ResetPositions()
    {
        time = 0f; animate = false; selected = 0; dragging = -1;
        centers[0].localPosition = new Vector3(-0.2f, 0.12f, -0.04f);
        centers[1].localPosition = new Vector3(0.2f, 0.12f, -0.04f);
        centers[2].localPosition = new Vector3(0f, -0.28f, -0.04f);
    }

    public Vector2 Center(int i) { return (Vector2)centers[i].localPosition + Vector2.one * 0.5f; }

    public float Field(Vector2 p)
    {
        p = GaussianField.SamplePoint(p, samples);
        float field = 0f;
        for (int i = 0; i < 3; i++) field += GaussianField.Influence(p, Center(i), radius);
        return field;
    }

    public bool PointerPoint(Transform panel, out Vector2 p)
    {
        p = Vector2.zero;
        if (Mouse.current == null || !panel.gameObject.activeInHierarchy) return false;
        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        Plane plane = new Plane(panel.forward, panel.position);
        if (!plane.Raycast(ray, out float distance)) return false;
        p = (Vector2)panel.InverseTransformPoint(ray.GetPoint(distance)) + Vector2.one * 0.5f;
        return p.x >= 0f && p.x <= 1f && p.y >= 0f && p.y <= 1f;
    }

    void Draw()
    {
        for (int i = 0; i < 3; i++)
        {
            Vector2 p = Center(i);
            centers[i].localPosition = new Vector3(Mathf.Clamp(p.x, 0.02f, 0.98f) - 0.5f, Mathf.Clamp(p.y, 0.02f, 0.98f) - 0.5f, -0.04f);
            centers[i].gameObject.SetActive(showCenters);
            centers[i].localScale = Vector3.one * (i == selected ? 0.026f : 0.018f);
            disks[i].localPosition = centers[i].localPosition;
            disks[i].localScale = Vector3.one * radius * 1.26f;
            leftMarkers[i].localPosition = centers[i].localPosition + Vector3.back * 0.02f;
            leftMarkers[i].localScale = centers[i].localScale;
            leftMarkers[i].gameObject.SetActive(showCenters);
            p = Center(i);
            properties.SetVector(centerNames[i], new Vector4(p.x, p.y, 0f, 0f));
        }
        properties.SetFloat("_Radius", radius);
        properties.SetFloat("_Threshold", threshold);
        properties.SetFloat("_EdgeWidth", edgeWidth);
        properties.SetInt("_Mode", (int)mode);
        properties.SetInt("_Grid", showGrid ? 1 : 0);
        properties.SetInt("_Samples", samples);
        output.SetPropertyBlock(properties);
    }
}
