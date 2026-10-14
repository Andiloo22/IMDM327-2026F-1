using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class CloudNoiseEditor
{
    const string folder = "Assets/327_CourseMaterial/Scenes/Game/ChickenHerd/Clouds/";

    [MenuItem("IMDM 327/Generate cloud noise")]
    public static void Generate()
    {
        ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(folder + "TileableVolumeNoise.compute");
        int size = 48;
        RenderTexture volume = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        volume.dimension = TextureDimension.Tex3D;
        volume.volumeDepth = size;
        volume.enableRandomWrite = true;
        volume.Create();
        int kernel = shader.FindKernel("Generate");
        shader.SetInt("Resolution", size);
        shader.SetTexture(kernel, "Result", volume);
        shader.Dispatch(kernel, size / 8, size / 8, size / 8);
        var readback = AsyncGPUReadback.Request(volume, 0, TextureFormat.RGBA32);
        readback.WaitForCompletion();
        if (readback.hasError)
        {
            volume.Release(); Object.DestroyImmediate(volume);
            throw new System.Exception("Cloud noise readback failed.");
        }
        Texture3D texture = new Texture3D(size, size, size, TextureFormat.RGBA32, false);
        texture.name = "Tileable cloud noise";
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Bilinear;
        Color32[] pixels = new Color32[size * size * size];
        for (int z = 0; z < size; z++)
            System.Array.Copy(readback.GetData<Color32>(z).ToArray(), 0, pixels, z * size * size, size * size);
        texture.SetPixels32(pixels);
        texture.Apply();
        string path = folder + "CloudNoise.asset";
        Texture3D old = AssetDatabase.LoadAssetAtPath<Texture3D>(path);
        if (old == null) AssetDatabase.CreateAsset(texture, path);
        else { EditorUtility.CopySerialized(texture, old); Object.DestroyImmediate(texture); }
        AssetDatabase.SaveAssets();
        volume.Release(); Object.DestroyImmediate(volume);
    }
}
