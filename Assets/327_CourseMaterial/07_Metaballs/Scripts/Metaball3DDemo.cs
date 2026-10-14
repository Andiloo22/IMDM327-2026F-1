using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

public class Metaball3DDemo : MonoBehaviour
{
    public enum Mode { Bounds, Steps, Normals, Surface }
    public Mode mode = Mode.Surface;
    public Renderer output;
    public Transform[] centers;
    public Camera desktopCamera;
    [Range(0.08f, 0.3f)] public float radius = 0.16f;
    [Range(0.2f, 1.8f)] public float threshold = 0.82f;
    [Range(32, 128)] public int steps = 96;
    public bool animate, showCenters = true, desktopInput = true;
    public float centerSize = 0.025f;
    public int selected;
    public Vector3 boundsMin, boundsMax;

    MaterialPropertyBlock properties;
    float time, yaw = 12f, pitch = 15f, distance = 2.5f;
    Vector3 pan;
    Plane dragPlane;
    int dragging = -1;
    string[] centerNames = { "_Center0", "_Center1", "_Center2" };

    void Start()
    {
        properties = new MaterialPropertyBlock();
        ResetPositions();
        Draw(); if (desktopInput) Orbit();
    }
    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.spaceKey.wasPressedThisFrame) animate = !animate;
            if (keyboard.rKey.wasPressedThisFrame) ResetPositions();
            if (keyboard.cKey.wasPressedThisFrame) showCenters = !showCenters;
            if (keyboard.nKey.wasPressedThisFrame) Step();
            if (keyboard.digit1Key.wasPressedThisFrame) mode = Mode.Bounds;
            if (keyboard.digit2Key.wasPressedThisFrame) mode = Mode.Steps;
            if (keyboard.digit3Key.wasPressedThisFrame) mode = Mode.Normals;
            if (keyboard.digit4Key.wasPressedThisFrame) mode = Mode.Surface;
            if (keyboard.tabKey.wasPressedThisFrame) selected = (selected + 1) % 3;
        }
        if (desktopInput) DesktopInput();
        if (animate) MoveCenters(Time.deltaTime);
        Draw();
    }
    void DesktopInput()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;
        Vector2 delta = mouse.delta.ReadValue();
        if (mouse.rightButton.isPressed) { yaw += delta.x * 0.25f; pitch = Mathf.Clamp(pitch - delta.y * 0.25f, -70f, 70f); }
        if (mouse.middleButton.isPressed) pan -= (desktopCamera.transform.right * delta.x + desktopCamera.transform.up * delta.y) * distance * 0.0015f;
        distance = Mathf.Clamp(distance - mouse.scroll.ReadValue().y / 120f * 0.2f, 0.8f, 5f);
        Orbit();
        Ray ray = desktopCamera.ScreenPointToRay(mouse.position.ReadValue());
        if (mouse.leftButton.wasPressedThisFrame && !EventSystem.current.IsPointerOverGameObject())
        {
            for (int i = 0; i < 3; i++)
            {
                Vector3 screen = desktopCamera.WorldToScreenPoint(centers[i].position);
                if (screen.z > 0f && Vector2.Distance(mouse.position.ReadValue(), screen) < 22f)
                {
                    dragging = selected = i; animate = false;
                    dragPlane = new Plane(desktopCamera.transform.forward, centers[i].position);
                    break;
                }
            }
        }
        if (mouse.leftButton.isPressed && dragging >= 0 && dragPlane.Raycast(ray, out float t))
            MoveCenter(dragging, ray.GetPoint(t));
        if (mouse.leftButton.wasReleasedThisFrame) dragging = -1;
    }
    void Orbit()
    {
        Vector3 target = transform.position + pan;
        desktopCamera.transform.position = target + Quaternion.Euler(pitch, yaw, 0f) * Vector3.back * distance;
        desktopCamera.transform.LookAt(target);
    }
    public void MoveCenter(int center, Vector3 worldPosition)
    {
        animate = false;
        Vector3 p = transform.InverseTransformPoint(worldPosition);
        centers[center].localPosition = new Vector3(Mathf.Clamp(p.x, -0.4f, 0.4f), Mathf.Clamp(p.y, -0.4f, 0.4f), Mathf.Clamp(p.z, -0.4f, 0.4f));
    }
    void MoveCenters(float delta)
    {
        float before = Mathf.Sin(time); time += delta * 0.7f;
        float shift = (Mathf.Sin(time) - before) * 0.12f;
        centers[0].localPosition += Vector3.right * shift;
        centers[1].localPosition -= Vector3.right * shift;
        centers[2].localPosition += Vector3.forward * shift;
    }
    public void Step() { animate = false; MoveCenters(1f / 30f); }
    public void ResetPositions()
    {
        time = 0f; animate = false; selected = 0; dragging = -1;
        centers[0].localPosition = new Vector3(-0.17f, 0.05f, 0f);
        centers[1].localPosition = new Vector3(0.17f, 0.05f, 0f);
        centers[2].localPosition = new Vector3(0f, -0.18f, 0.08f);
    }
    public float Field(Vector3 p)
    {
        float field = 0f;
        for (int i = 0; i < 3; i++) field += GaussianField.Influence(p, centers[i].localPosition, radius);
        return field;
    }
    public void Draw()
    {
        boundsMin = boundsMax = centers[0].localPosition;
        for (int i = 0; i < 3; i++)
        {
            Vector3 p = centers[i].localPosition;
            boundsMin = Vector3.Min(boundsMin, p); boundsMax = Vector3.Max(boundsMax, p);
            centers[i].gameObject.SetActive(showCenters);
            centers[i].localScale = Vector3.one * centerSize * (selected == i ? 1.4f : 1f);
            properties.SetVector(centerNames[i], new Vector4(p.x, p.y, p.z, 0f));
        }
        // Outside these bounds, each Gaussian is weaker than T / 3.
        float margin = radius * Mathf.Sqrt(-2f * Mathf.Log(threshold / 3f)) + 0.01f;
        boundsMin -= Vector3.one * margin; boundsMax += Vector3.one * margin;
        output.transform.localPosition = (boundsMin + boundsMax) * 0.5f;
        output.transform.localScale = boundsMax - boundsMin;
        properties.SetVector("_BoundsMin", boundsMin); properties.SetVector("_BoundsMax", boundsMax);
        properties.SetMatrix("_WorldToField", transform.worldToLocalMatrix);
        properties.SetMatrix("_FieldToWorld", transform.localToWorldMatrix);
        properties.SetFloat("_Radius", radius); properties.SetFloat("_Threshold", threshold);
        properties.SetInt("_Steps", steps); properties.SetInt("_Mode", (int)mode);
        output.SetPropertyBlock(properties);
    }
}
