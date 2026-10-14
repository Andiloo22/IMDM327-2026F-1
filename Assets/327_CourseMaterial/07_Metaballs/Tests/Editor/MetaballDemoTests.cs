using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class MetaballDemoTests
{
    const string Root = "Assets/327_CourseMaterial/07_Metaballs/";
    InputSettings previousSettings;
    Keyboard testKeyboard;
    Mouse testMouse;

    [Test]
    public void FragmentFieldMatchesCSharpIncludingCoarseSampling()
    {
        Shader shader = Shader.Find("IMDM327/Metaballs2D");
        Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad); quad.layer = 31;
        Material material = new Material(shader); quad.GetComponent<Renderer>().sharedMaterial = material;
        material.SetInt("_Mode", 3);
        Camera cam = TestCamera(); cam.orthographic = true; cam.orthographicSize = 0.5f;
        cam.transform.position = new Vector3(0f, 0f, -2f);
        try
        {
            foreach (int samples in new[] { 0, 16, 32, 64 })
            {
                material.SetInt("_Samples", samples);
                Texture2D image = Render(cam, 128);
                try
                {
                    foreach (int x in new[] { 21, 51, 92, 113 })
                    foreach (int y in new[] { 18, 46, 79, 107 })
                    {
                        Vector2 p = GaussianField.SamplePoint(new Vector2((x + 0.5f) / 128f, (y + 0.5f) / 128f), samples);
                        float expected = (GaussianField.Influence(p, new Vector2(0.3f, 0.62f), 0.16f) + GaussianField.Influence(p, new Vector2(0.7f, 0.62f), 0.16f) + GaussianField.Influence(p, new Vector2(0.5f, 0.22f), 0.16f)) / 3f;
                        Assert.That(image.GetPixel(x, y).r, Is.EqualTo(expected).Within(0.002f), $"Field mismatch at {x}, {y}; samples={samples}");
                    }
                }
                finally { Object.DestroyImmediate(image); }
            }
        }
        finally { Object.DestroyImmediate(quad); Object.DestroyImmediate(material); Object.DestroyImmediate(cam.gameObject); }
    }

    [Test]
    public void IsosurfaceHasVaryingDepthAndChangesWithViewpoint()
    {
        EditorSceneManager.OpenScene(Root + "Scenes/02_Metaballs_3D.unity");
        Metaball3DDemo demo = Object.FindFirstObjectByType<Metaball3DDemo>();
        typeof(Metaball3DDemo).GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(demo, null);
        demo.showCenters = false; demo.Draw();
        demo.output.gameObject.layer = 31;
        Camera cam = TestCamera(); cam.fieldOfView = demo.desktopCamera.fieldOfView;
        cam.transform.SetPositionAndRotation(demo.desktopCamera.transform.position, demo.desktopCamera.transform.rotation);
        RenderTexture target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGBFloat);
        Material probe = new Material(Shader.Find("Hidden/IMDM327/DepthProbe"));
        Texture2D before = null, depth = null, otherView = null;
        try
        {
            Assert.That(ShaderUtil.ShaderHasError(demo.output.sharedMaterial.shader), Is.False);
            cam.targetTexture = target; cam.Render(); before = Read(target);
            probe.SetTexture("_Depth", target, RenderTextureSubElement.Depth);
            RenderTexture depthTarget = new RenderTexture(256, 256, 0, RenderTextureFormat.ARGBFloat);
            try { Graphics.Blit(target, depthTarget, probe); depth = Read(depthTarget); }
            finally { Object.DestroyImmediate(depthTarget); }
            float minimum = float.MaxValue, maximum = float.MinValue; int pixels = 0;
            foreach (Color pixel in depth.GetPixels())
            {
                if (pixel.r <= 0.000001f || pixel.r >= 0.999999f) continue;
                minimum = Mathf.Min(minimum, pixel.r); maximum = Mathf.Max(maximum, pixel.r); pixels++;
            }
            Assert.That(pixels, Is.GreaterThan(100), "No isosurface depth was rendered");
            Assert.That(maximum - minimum, Is.GreaterThan(0.0001f), "The result has flat depth");
            Color[] a = before.GetPixels();
            cam.transform.position += cam.transform.right * 0.064f; cam.Render(); otherView = Read(target);
            Color[] c = otherView.GetPixels(); float change = 0f;
            for (int i = 0; i < a.Length; i++) change += Mathf.Abs(a[i].r - c[i].r);
            Assert.That(change, Is.GreaterThan(10f), "Changing the viewpoint did not change the surface projection");
        }
        finally
        {
            cam.targetTexture = null; Object.DestroyImmediate(target); Object.DestroyImmediate(probe); Object.DestroyImmediate(cam.gameObject);
            foreach (Texture2D image in new[] { before, depth, otherView }) if (image != null) Object.DestroyImmediate(image);
        }
    }

    [UnityTest]
    public IEnumerator DesktopControlsWork()
    {
        if (!Application.isBatchMode) ClassroomGameView();
        yield return new EnterPlayMode();
        previousSettings = InputSystem.settings;
        InputSystem.settings = Object.Instantiate(previousSettings);
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        Keyboard keyboard = testKeyboard = InputSystem.AddDevice<Keyboard>(); Mouse mouse = testMouse = InputSystem.AddDevice<Mouse>();
        yield return SceneManager.LoadSceneAsync("01_Metaballs_2D");
        yield return Frames(2);
        MetaballDemo demo = Object.FindFirstObjectByType<MetaballDemo>();
        Assert.That(demo.centers.Length, Is.EqualTo(3));
        Assert.That(Object.FindObjectsByType<UnityEngine.UI.Slider>(FindObjectsSortMode.None), Is.Empty);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space)); yield return Frames(2);
        Assert.That(demo.animate, Is.True);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
        demo.animate = false;
        Vector3 target = demo.output.transform.TransformPoint(new Vector3(-0.1f, 0.2f, -0.04f));
        Vector2 start = Camera.main.WorldToScreenPoint(demo.centers[0].position);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = start }); yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = start, buttons = 1 }); yield return null; yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = Camera.main.WorldToScreenPoint(target), buttons = 1 }); yield return null; yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = Camera.main.WorldToScreenPoint(target) }); yield return null;
        Assert.That(Vector2.Distance(demo.Center(0), new Vector2(0.4f, 0.7f)), Is.LessThan(0.005f));
        Assert.That((Vector2)demo.disks[0].localPosition, Is.EqualTo((Vector2)demo.centers[0].localPosition));
        demo.ResetPositions();
        MetaballLessonUI ui = Object.FindFirstObjectByType<MetaballLessonUI>();
        Assert.That(demo.objectPanel.gameObject.activeSelf, Is.True);
        Assert.That(ui.modes.Length, Is.EqualTo(7));
        for (int i = 0; i < 7; i++) { ui.modes[i].onClick.Invoke(); yield return null; Assert.That((int)demo.mode, Is.EqualTo(i)); }
        demo.radius = 0.2f; demo.threshold = 1.2f; demo.edgeWidth = 0.04f; yield return null;
        Assert.That(demo.radius, Is.EqualTo(0.2f).Within(0.001f)); Assert.That(demo.threshold, Is.EqualTo(1.2f).Within(0.001f));
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Q, Key.G)); yield return null; yield return null;
        Assert.That(demo.samples, Is.EqualTo(16)); Assert.That(demo.showGrid, Is.True);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
        demo.samples = 0; demo.showGrid = false; demo.radius = 0.16f; demo.threshold = 0.82f;
        yield return SceneManager.LoadSceneAsync("02_Metaballs_3D"); yield return null; yield return null; yield return null;
        Metaball3DDemo volume = Object.FindFirstObjectByType<Metaball3DDemo>();
        Assert.That(volume.desktopInput, Is.True);
        Quaternion rotation = volume.desktopCamera.transform.rotation;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(400f, 300f), buttons = 2, delta = new Vector2(40f, 15f) });
        yield return null; yield return null;
        Assert.That(Quaternion.Angle(rotation, volume.desktopCamera.transform.rotation), Is.GreaterThan(1f));
        InputSystem.QueueStateEvent(mouse, new MouseState()); yield return null;
        volume.MoveCenter(0, volume.transform.TransformPoint(new Vector3(-0.3f, 0.1f, -0.12f))); yield return null;
        Assert.That(volume.centers[0].localPosition.z, Is.EqualTo(-0.12f).Within(0.001f));
        Metaball3DUI ui3D = Object.FindFirstObjectByType<Metaball3DUI>(); ui3D.modes[2].onClick.Invoke(); volume.threshold = 1.1f; volume.steps = 48; yield return null;
        Assert.That(volume.threshold, Is.EqualTo(1.1f).Within(0.001f)); Assert.That(volume.steps, Is.EqualTo(48));
        Assert.That(volume.mode, Is.EqualTo(Metaball3DDemo.Mode.Normals));
        Assert.That(Object.FindObjectsByType<UnityEngine.UI.Slider>(FindObjectsSortMode.None), Is.Empty);
        volume.ResetPositions(); volume.threshold = 0.82f; volume.steps = 96; yield return null;
        InputSystem.RemoveDevice(keyboard); InputSystem.RemoveDevice(mouse);
        InputSystem.settings = previousSettings;
        yield return new ExitPlayMode();
    }
    [UnityTest]
    public IEnumerator CaptureCourseScreenshots()
    {
        if (Application.isBatchMode) Assert.Ignore("Course screenshots need a Game view.");
        ClassroomGameView();
        yield return new EnterPlayMode();
        previousSettings = InputSystem.settings;
        InputSystem.settings = Object.Instantiate(previousSettings);
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        testMouse = InputSystem.AddDevice<Mouse>();
        InputSystem.QueueStateEvent(testMouse, new MouseState { position = new Vector2(20f, 20f) });

        yield return SceneManager.LoadSceneAsync("01_Metaballs_2D");
        yield return Frames(3);
        MetaballDemo demo = Object.FindFirstObjectByType<MetaballDemo>();
        yield return Capture("01_Metaballs_2D");
        demo.centers[0].localPosition = new Vector3(-0.27f, 0.2f, -0.04f);
        demo.centers[1].localPosition = new Vector3(0.27f, 0.2f, -0.04f);
        demo.centers[2].localPosition = new Vector3(0f, -0.29f, -0.04f);
        yield return Frames(3);
        yield return Capture("01b_Separated_Centers");

        yield return SceneManager.LoadSceneAsync("01_Metaballs_2D");
        yield return Frames(3);
        demo = Object.FindFirstObjectByType<MetaballDemo>();
        string[] stages = { "02a_UV", "02b_Distance", "02c_One_Influence", "02d_Combined_Field", "02e_Threshold", "02f_Smooth_Edge", "01_Metaballs_2D" };
        for (int i = 0; i < stages.Length; i++)
        {
            demo.mode = (MetaballDemo.Mode)i;
            // Widen the transition so the smooth edge is visible in a slide.
            demo.edgeWidth = i == 5 ? 0.1f : 0.03f;
            yield return Frames(3);
            yield return Capture(stages[i]);
        }
        demo.samples = 16; demo.showGrid = false;
        yield return Frames(3);
        yield return Capture("02g_Coarse_Sampling");

        yield return SceneManager.LoadSceneAsync("02_Metaballs_3D");
        yield return Frames(3);
        Metaball3DDemo volume = Object.FindFirstObjectByType<Metaball3DDemo>();
        typeof(Metaball3DDemo).GetField("distance", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(volume, 1.8f);
        yield return Frames(3);
        yield return Capture("02_Metaballs_3D");
        volume.mode = Metaball3DDemo.Mode.Normals;
        yield return Frames(3);
        yield return Capture("03a_Surface_Normals");
        volume.mode = Metaball3DDemo.Mode.Bounds;
        typeof(Metaball3DDemo).GetField("distance", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(volume, 2.7f);
        yield return Frames(3);
        yield return Capture("03b_Ray_Bounds");
        InputSystem.RemoveDevice(testMouse);
        InputSystem.settings = previousSettings;
        yield return new ExitPlayMode();
    }
    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (Application.isPlaying)
        {
            if (testKeyboard != null && testKeyboard.added) InputSystem.RemoveDevice(testKeyboard);
            if (testMouse != null && testMouse.added) InputSystem.RemoveDevice(testMouse);
            if (previousSettings != null) InputSystem.settings = previousSettings;
            yield return new ExitPlayMode();
        }
    }
    static IEnumerator Frames(int count)
    {
        int target = Time.frameCount + count;
        double deadline = EditorApplication.timeSinceStartup + 5;
        while (Time.frameCount < target)
        {
            Assert.That(EditorApplication.timeSinceStartup, Is.LessThan(deadline), "Play mode frames did not advance");
            yield return null;
        }
    }
    static IEnumerator Capture(string name)
    {
        // Batch mode has no Game-view framebuffer; rendering has separate GPU tests.
        if (Application.isBatchMode) yield break;
        string directory = Path.GetFullPath("Docs/Week06/screenshots"); Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name + ".png");
        if (File.Exists(path)) File.Delete(path);
        ScreenCapture.CaptureScreenshot(path);
        for (int i = 0; i < 20; i++) { yield return null; if (File.Exists(path)) break; }
        Assert.That(File.Exists(path), Is.True, "Game-view screenshot was not saved");
    }
    static void ClassroomGameView()
    {
        Assembly editor = typeof(Editor).Assembly;
        Type sizesType = editor.GetType("UnityEditor.GameViewSizes");
        object sizes = sizesType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy).GetValue(null);
        object group = sizesType.GetMethod("GetGroup").Invoke(sizes, new object[] { 0 });
        Type sizeType = editor.GetType("UnityEditor.GameViewSize"), modeType = editor.GetType("UnityEditor.GameViewSizeType");
        object size = Activator.CreateInstance(sizeType, new object[] { Enum.Parse(modeType, "FixedResolution"), 1920, 1080, "Week06 Classroom" });
        group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { size });
        int count = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null);
        Type viewType = editor.GetType("UnityEditor.GameView"); EditorWindow view = EditorWindow.GetWindow(viewType);
        viewType.GetProperty("selectedSizeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SetValue(view, count - 1);
        view.Focus();
    }
    static Camera TestCamera()
    {
        Camera cam = new GameObject("Shader test camera").AddComponent<Camera>();
        cam.cullingMask = 1 << 31; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
        cam.allowHDR = cam.allowMSAA = false; cam.aspect = 1f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 50f;
        return cam;
    }
    static Texture2D Render(Camera cam, int size)
    {
        RenderTexture target = new RenderTexture(size, size, 24, RenderTextureFormat.ARGBFloat);
        cam.targetTexture = target;
        try { cam.Render(); return Read(target); }
        finally { cam.targetTexture = null; Object.DestroyImmediate(target); }
    }
    static Texture2D Read(RenderTexture target)
    {
        RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
        Texture2D image = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
        image.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0); image.Apply(); RenderTexture.active = previous;
        return image;
    }
}

