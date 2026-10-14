// IMDM 327
// Import an image and plot into cubes + interactive update
// Based on Myungin Lee's 2025 ImagePlay example
using UnityEngine;

public class ImagePlay : MonoBehaviour
{
    public string imageFileName = "wave";
    [Range(1, 64)] public int decimate = 32;
    public float depthOffset = 8f;
    [Range(0f, 1f)] public float alphaThreshold = 0.02f;
    public float timescale = 0.1f;

    GameObject[] cubes;
    int columns, rows;
    float time;
    Material material;
    Color32[] pixels;
    void Start()
    {
        var texture = Resources.Load<Texture2D>(imageFileName);
        if (texture == null || !texture.isReadable)
        {
            Debug.LogWarning("ImagePlay: image missing or Read/Write disabled: " + imageFileName, this);
            return;
        }

        pixels = texture.GetPixels32();
        decimate = Mathf.Clamp(decimate, 1, 64);
        columns = Mathf.Max(1, texture.width / decimate);
        rows = Mathf.Max(1, texture.height / decimate);
        cubes = new GameObject[columns * rows];
        material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        var color = new MaterialPropertyBlock();

        // Sample the image pixels and build a grid of colored cubes.
        for (int x = 0; x < columns; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                Color pixel = pixels[y * decimate * texture.width + x * decimate];
                if (pixel.a < alphaThreshold) continue;

                
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(transform, false);
                cube.transform.localPosition = new Vector3(x - (columns - 1) * 0.5f,
                    y - (rows - 1) * 0.5f, depthOffset);
                cube.transform.localScale = new Vector3(1f, 1f, 1f + 30f * pixel.r);
                Destroy(cube.GetComponent<Collider>());
                Renderer renderer = cube.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                color.SetColor("_BaseColor", pixel);
                renderer.SetPropertyBlock(color);
                cubes[x * rows + y] = cube;
            }
        }
    }

    void FixedUpdate()
    {
        for (int x = 0; x < columns; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                GameObject cube = cubes[x * rows + y];
                if (cube == null) continue;
                float z = depthOffset * (1f + Mathf.Sin(x * time * timescale));
                cube.transform.localPosition 
                    = new Vector3(x - (columns - 1) * 0.5f,
                                    y - (rows - 1) * 0.5f, 
                                    z);
                    // cube.transform.Rotate(0f, time * timescale, 0f);
            }
        }
        time += Time.fixedDeltaTime;
    }
}
