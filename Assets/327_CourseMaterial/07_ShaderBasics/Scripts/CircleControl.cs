using UnityEngine;

public class CircleControl : MonoBehaviour
{
    public Renderer[] outputs;
    public Color color = new Color(0.1f, 0.8f, 0.9f);
    public Vector2 center = new Vector2(0.5f, 0.5f);
    [Range(0.05f, 0.45f)] public float radius = 0.28f;
    [Range(0.001f, 0.1f)] public float edgeWidth = 0.01f;

    Material[] materials;

    void Start()
    {
        // Each renderer gets its own material, leaving the saved asset unchanged.
        materials = new Material[outputs.Length];
        for (int i = 0; i < outputs.Length; i++)
            materials[i] = outputs[i].material;
    }

    void Update()
    {
        // Give both examples the same values so we can compare them.
        for (int i = 0; i < materials.Length; i++)
        {
            // The names must match the shader properties (Graph: Reference).
            materials[i].SetColor("_Color", color);
            materials[i].SetVector("_Center", new Vector4(center.x, center.y, 0f, 0f));
            materials[i].SetFloat("_Radius", radius);
            materials[i].SetFloat("_EdgeWidth", edgeWidth);
        }
    }
}
