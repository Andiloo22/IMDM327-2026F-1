using UnityEngine;

public class UVControl : MonoBehaviour
{
    public Renderer[] outputs;
    public Vector2 tiling = Vector2.one;
    public Vector2 offset = Vector2.zero;

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
            // SetVector sends several numbers together. The shader uses X and Y.
            materials[i].SetVector("_Tiling", new Vector4(tiling.x, tiling.y, 0f, 0f));
            materials[i].SetVector("_Offset", new Vector4(offset.x, offset.y, 0f, 0f));
        }
    }

    void OnDestroy()
    {
        // Remove the material copies we created in Start.
        if (materials == null) return;
        for (int i = 0; i < materials.Length; i++)
            Destroy(materials[i]);
    }
}
