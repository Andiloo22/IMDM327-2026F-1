using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class MetaballGraphTests
{
    const string Root = "Assets/327_CourseMaterial/07_Metaballs/";

    [Test]
    public void GraphsCompile()
    {
        foreach (string name in new[] { "Metaballs2D", "Metaballs3D" })
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "Shaders/Graph/" + name + ".shadergraph");
            Assert.That(shader, Is.Not.Null);
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
        }
    }

    [Test]
    public void All2DModesMatchIncludingSamplingAndGrid()
    {
        var code = new Material(AssetDatabase.LoadAssetAtPath<Shader>(Root + "Shaders/Metaballs2D.shader"));
        var graph = new Material(AssetDatabase.LoadAssetAtPath<Shader>(Root + "Shaders/Graph/Metaballs2D.shadergraph"));
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad); quad.layer = 31;
        Camera camera = Camera(); camera.orthographic = true; camera.orthographicSize = 0.5f;
        camera.transform.position = new Vector3(0, 0, -2);
        try
        {
            foreach (int samples in new[] { 0, 16, 32, 64 })
            foreach (int mode in new[] { 0, 1, 2, 3, 4, 5, 6 })
            {
                foreach (Material material in new[] { code, graph })
                {
                    material.SetFloat("_Mode", mode); material.SetFloat("_Samples", samples); material.SetFloat("_Grid", samples == 32 ? 1 : 0);
                    material.SetFloat("_Radius", samples == 16 ? 0.21f : 0.16f); material.SetFloat("_Threshold", 0.9f); material.SetFloat("_EdgeWidth", 0.07f);
                    material.SetVector("_Center0", new Vector4(0.25f, 0.68f, 0, 0)); material.SetVector("_Center1", new Vector4(0.61f, 0.52f, 0, 0)); material.SetVector("_Center2", new Vector4(0.48f, 0.27f, 0, 0));
                    material.SetVector("_Color", new Vector4(1, 0.95f, 0.06f, 1));
                }
                quad.GetComponent<Renderer>().sharedMaterial = code; Color[] before = Render(camera);
                quad.GetComponent<Renderer>().sharedMaterial = graph; Color[] after = Render(camera);
                Compare(before, after, 0.0005f, "2D mode=" + mode + ", samples=" + samples);
            }
        }
        finally { Object.DestroyImmediate(quad); Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(code); Object.DestroyImmediate(graph); }
    }

    [Test]
    public void All3DModesMatchWithSurfaceDepthAndMovedCenters()
    {
        EditorSceneManager.OpenScene(Root + "Scenes/02_Metaballs_3D.unity");
        var demo = Object.FindFirstObjectByType<Metaball3DDemo>();
        var comparison = Object.FindFirstObjectByType<MetaballShaderComparison>();
        Invoke(demo, "Start"); demo.showCenters = false; demo.output.gameObject.layer = 31;
        var camera = Camera(); camera.fieldOfView = demo.desktopCamera.fieldOfView;
        comparison.camera = camera;
        Material source = comparison.graph; Material instance = new Material(source); comparison.graph = instance;
        try
        {
            for (int view = 0; view < 3; view++)
            {
                if (view == 1)
                {
                    demo.centers[0].localPosition += new Vector3(-0.06f, 0.08f, -0.05f);
                    demo.centers[2].localPosition += new Vector3(0.07f, -0.03f, 0.06f);
                    demo.threshold = 1.1f; demo.radius = 0.2f; demo.steps = 48;
                    demo.transform.localScale = new Vector3(1.4f, 0.9f, 1.1f);
                }
                camera.transform.SetPositionAndRotation(demo.desktopCamera.transform.position, demo.desktopCamera.transform.rotation);
                if (view == 2) camera.transform.position += camera.transform.right * 0.064f;
                foreach (var mode in new[] { Metaball3DDemo.Mode.Bounds, Metaball3DDemo.Mode.Steps, Metaball3DDemo.Mode.Normals, Metaball3DDemo.Mode.Surface })
                {
                    demo.mode = mode; demo.Draw(); comparison.shaderGraph = false; comparison.Draw();
                    Color[] before = Render(camera); Color[] codeDepth = Render(camera, true);
                    comparison.shaderGraph = true; comparison.Draw();
                    Color[] after = Render(camera); Color[] graphDepth = Render(camera, true);
                    Compare(before, after, 0.003f, "3D " + mode + ", view=" + view);
                    Compare(codeDepth, graphDepth, 0.00001f, "Depth " + mode + ", view=" + view);
                    if (mode == Metaball3DDemo.Mode.Surface)
                    {
                        int hits = 0; float min = 1, max = 0;
                        foreach (Color pixel in graphDepth)
                            if (pixel.r > 0.000001f && pixel.r < 0.999999f) { hits++; min = Mathf.Min(min, pixel.r); max = Mathf.Max(max, pixel.r); }
                        Assert.That(hits, Is.GreaterThan(100)); Assert.That(max - min, Is.GreaterThan(0.0001f));
                    }
                }
            }
        }
        finally
        {
            comparison.graph = source; Object.DestroyImmediate(instance); Object.DestroyImmediate(camera.gameObject);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }

    [UnityTest]
    public IEnumerator ComparisonKeepsControlsAndCapturesScenes()
    {
        if (!Application.isBatchMode) typeof(MetaballDemoTests).GetMethod("ClassroomGameView", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        if (!EditorApplication.isPlaying) EditorSceneManager.OpenScene(Root + "Scenes/01_Metaballs_2D.unity");
        yield return new EnterPlayMode();
        yield return SceneManager.LoadSceneAsync("01_Metaballs_2D"); yield return Frames(3);
        var comparison = Object.FindFirstObjectByType<MetaballShaderComparison>();
        var flat = Object.FindFirstObjectByType<MetaballDemo>();
        Assert.That(flat.centers.Length, Is.EqualTo(3));
        Assert.That(flat.objectPanel.gameObject.activeSelf, Is.True);
        Assert.That(comparison.shaderGraph, Is.False);
        flat.radius = 0.2f; flat.threshold = 1.0f; flat.samples = 16; flat.showGrid = true;
        flat.centers[0].localPosition += Vector3.right * 0.07f;
        Vector3 position = flat.centers[0].localPosition;
        comparison.shaderGraph = false; yield return Frames(2);
        Assert.That(flat.output.sharedMaterial.shader, Is.EqualTo(comparison.code.shader));
        Color[] flatCode = Render(UnityEngine.Camera.main);
        comparison.shaderGraph = true; yield return Frames(2);
        Assert.That(flat.output.sharedMaterial.shader, Is.EqualTo(comparison.graph.shader));
        Compare(flatCode, Render(UnityEngine.Camera.main), 0.0005f, "Runtime 2D controls");
        Assert.That(flat.centers[0].localPosition, Is.EqualTo(position));
        flat.ResetPositions(); flat.radius = 0.16f; flat.threshold = 0.82f; flat.samples = 0; flat.showGrid = false; flat.mode = MetaballDemo.Mode.Metaballs;
        yield return Frames(2); yield return Capture("01_Metaballs_2D_Graph");
        flat.mode = MetaballDemo.Mode.CombinedField; yield return Frames(2); yield return Capture("05a_Graph_Combined_Field");

        yield return SceneManager.LoadSceneAsync("02_Metaballs_3D"); yield return Frames(3);
        comparison = Object.FindFirstObjectByType<MetaballShaderComparison>();
        var volume = Object.FindFirstObjectByType<Metaball3DDemo>();
        Assert.That(volume.output.gameObject.layer, Is.Zero);
        volume.MoveCenter(0, volume.transform.TransformPoint(new Vector3(-0.25f, 0.1f, -0.1f)));
        volume.threshold = 1.1f; volume.steps = 48; yield return Frames(2);
        position = volume.centers[0].localPosition;
        comparison.shaderGraph = false; yield return Frames(2); Assert.That(volume.output.forceRenderingOff, Is.False);
        Color[] volumeCode = Render(UnityEngine.Camera.main);
        int visible = 0;
        foreach (Color pixel in volumeCode) if (pixel.b > 0.2f) visible++;
        Assert.That(visible, Is.GreaterThan(100), "The runtime surface is not visible");
        comparison.shaderGraph = true; yield return Frames(2); Assert.That(volume.output.forceRenderingOff, Is.True);
        Compare(volumeCode, Render(UnityEngine.Camera.main), 0.003f, "Runtime 3D controls");
        Assert.That(volume.centers[0].localPosition, Is.EqualTo(position));
        volume.ResetPositions(); volume.threshold = 0.82f; volume.steps = 96; volume.mode = Metaball3DDemo.Mode.Surface;
        typeof(Metaball3DDemo).GetField("distance", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(volume, 1.8f);
        yield return Frames(2); yield return Capture("02_Metaballs_3D_Graph");
        volume.mode = Metaball3DDemo.Mode.Normals; yield return Frames(2); yield return Capture("06a_Graph_Surface_Normals");
        yield return new ExitPlayMode();
    }

    static void Invoke(object value, string method) { value.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(value, null); }
    static Camera Camera()
    {
        var camera = new GameObject("Graph test camera").AddComponent<Camera>(); camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.cullingMask = 1 << 31;
        camera.allowHDR = camera.allowMSAA = false; camera.aspect = 1f;
        camera.nearClipPlane = 0.1f; camera.farClipPlane = 20;
        return camera;
    }
    static Color[] Render(Camera camera, bool depth = false)
    {
        var target = new RenderTexture(160, 160, 24, RenderTextureFormat.ARGBFloat);
        var previous = RenderTexture.active; camera.targetTexture = target;
        RenderTexture copy = null; Material probe = null;
        var image = new Texture2D(160, 160, TextureFormat.RGBAFloat, false, true);
        try
        {
            camera.Render(); RenderTexture.active = target;
            if (depth)
            {
                copy = new RenderTexture(160, 160, 0, RenderTextureFormat.ARGBFloat);
                probe = new Material(Shader.Find("Hidden/IMDM327/DepthProbe"));
                probe.SetTexture("_Depth", target, RenderTextureSubElement.Depth); Graphics.Blit(target, copy, probe); RenderTexture.active = copy;
            }
            image.ReadPixels(new Rect(0, 0, 160, 160), 0, 0); image.Apply(); return image.GetPixels();
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous;
            Object.DestroyImmediate(image); Object.DestroyImmediate(target);
            if (copy != null) Object.DestroyImmediate(copy); if (probe != null) Object.DestroyImmediate(probe);
        }
    }
    static void Compare(Color[] a, Color[] b, float tolerance, string label)
    {
        float sum = 0;
        for (int i = 0; i < a.Length; i++) sum += Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b);
        Assert.That(sum / (a.Length * 3), Is.LessThan(tolerance), label);
    }
    static IEnumerator Frames(int count)
    {
        int target = Time.frameCount + count;
        double deadline = EditorApplication.timeSinceStartup + 5;
        while (Time.frameCount < target)
        {
            Assert.That(EditorApplication.timeSinceStartup, Is.LessThan(deadline));
            yield return null;
        }
    }
    static IEnumerator Capture(string name)
    {
        if (Application.isBatchMode) yield break;
        string path = Path.GetFullPath("Docs/Week06/screenshots/" + name + ".png");
        if (File.Exists(path)) File.Delete(path);
        ScreenCapture.CaptureScreenshot(path);
        for (int i = 0; i < 30; i++) { yield return null; if (File.Exists(path)) break; }
        Assert.That(File.Exists(path), Is.True);
    }
}
