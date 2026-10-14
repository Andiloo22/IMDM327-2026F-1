using UnityEngine;

public class ColorControl : MonoBehaviour
{
    public Renderer[] outputs;
    public Color color = new Color(0.1f, 0.8f, 0.9f);

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
            // SetColor sends the Inspector color to the shader's _Color.
            materials[i].SetColor("_Color", color);
        }
    }
}
