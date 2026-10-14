// IMDM 327
// Import an image and draw on a plane as a material
// Based on Myungin Lee's 2025 ImageLoad example
using UnityEngine;

public class ImageLoad : MonoBehaviour
{
    public string imageFileName = "UMD-logo";
    public float imageSize = 4.5f;
    Material material;

    void Start()
    {
        var texture = Resources.Load<Texture2D>(imageFileName);
        var palette = GameObject.CreatePrimitive(PrimitiveType.Plane);
        palette.transform.SetParent(transform, false);
        palette.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        palette.transform.localScale = new Vector3(
            imageSize * texture.width / texture.height / 10f, 1f, imageSize / 10f);

        material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.mainTexture = texture;
        material.color = Color.white;
        material.SetFloat("_AlphaClip", 1f);
        material.SetFloat("_Cutoff", 0.02f);
        material.EnableKeyword("_ALPHATEST_ON");
        material.renderQueue = 2450;
        palette.GetComponent<MeshRenderer>().sharedMaterial = material;
    }
}
