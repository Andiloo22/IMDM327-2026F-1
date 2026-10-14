using UnityEngine;

public class GradientControl : MonoBehaviour
{
    public Renderer[] outputs;
    public Color colorA = new Color(0.05f, 0.2f, 0.9f);
    public Color colorB = new Color(1f, 0.35f, 0.1f);

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
            // C# chooses the colors. The shader mixes them at each fragment.
            materials[i].SetColor("_ColorA", colorA);
            materials[i].SetColor("_ColorB", colorB);
        }
    }
}
