using UnityEngine;

public class MetaballShaderComparison : MonoBehaviour
{
    public bool shaderGraph;
    public Renderer output;
    public Material code, graph;
    public Metaball3DDemo volume;
    public Camera camera;
    Material graphInstance;
    MaterialPropertyBlock properties;
    MetaballDemo flat;

    void Start()
    {
        graphInstance = new Material(graph);
        graph = graphInstance;
        flat = GetComponent<MetaballDemo>();
        properties = new MaterialPropertyBlock();
        Draw();
    }

    void LateUpdate() { Draw(); }

    public void Draw()
    {
        output.sharedMaterial = shaderGraph && volume == null ? graph : code;
        // The 3D graph draws through a fullscreen pass instead of the cube.
        output.forceRenderingOff = shaderGraph && volume != null;
        if (shaderGraph && flat != null)
        {
            // Graph number properties are floats, including the mode and toggles.
            output.GetPropertyBlock(properties);
            properties.SetFloat("_Mode", (int)flat.mode);
            properties.SetFloat("_Grid", flat.showGrid ? 1 : 0);
            properties.SetFloat("_Samples", flat.samples);
            output.SetPropertyBlock(properties);
        }
    }

    void OnDisable() { output.forceRenderingOff = false; }
    void OnDestroy() { if (graphInstance != null) Destroy(graphInstance); }
}
