using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.ARFoundation;
using UnityEngine.UI;
using TMPro;
using Object = UnityEngine.Object;

public static class Week06DemoGenerator
{
    public const string Root = "Assets/327_CourseMaterial/07_Metaballs/";
    public static readonly string[] SceneNames = { "01_Metaballs_2D", "02_Metaballs_3D", "03_Metaballs_MR" };
    static Material dark, disk, outline, handle, border;
    static Mesh diskMesh, markerMesh;
    static Color textColor = new Color(0.88f, 0.9f, 0.93f);

    [MenuItem("Tools/IMDM327/Generate Week06 Metaballs Demo")]
    public static void Generate()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
        { Debug.LogWarning("Save the current scene before generating the Week06 scenes."); return; }
        Directory.CreateDirectory(Root + "Scenes"); Directory.CreateDirectory(Root + "Materials");
        AssetDatabase.Refresh();
        dark = Solid("Panel", new Color(0.035f, 0.04f, 0.05f));
        disk = Solid("Disks", new Color(0.88f, 0.91f, 0.94f));
        outline = Solid("Disk boundary", new Color(0.08f, 0.09f, 0.11f));
        border = Solid("Border", new Color(0.2f, 0.23f, 0.26f));
        handle = Material("Handles", "IMDM327/CenterHandle");
        diskMesh = Disk(); markerMesh = Marker();
        Create2D(); Create3D(); CreateMR();
        var scenes = EditorBuildSettings.scenes.ToList();
        foreach (string name in SceneNames)
        {
            string path = Root + "Scenes/" + name + ".unity";
            if (!scenes.Any(s => s.path == path)) scenes.Add(new EditorBuildSettingsScene(path, true));
        }
        EditorBuildSettings.scenes = scenes.ToArray();
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(Root + "Scenes/" + SceneNames[0] + ".unity");
        Debug.Log("Week06: generated three metaball scenes. Open scene 01 and press Play.");
    }

    static void NewScene(bool orthographic)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject camera = new GameObject("Main Camera"); camera.tag = "MainCamera";
        Camera cam = camera.AddComponent<Camera>(); cam.orthographic = orthographic;
        cam.orthographicSize = 4.2f; cam.fieldOfView = 42f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 50f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.018f, 0.022f, 0.029f);
        camera.transform.position = new Vector3(0f, 0f, -10f); camera.AddComponent<AudioListener>();
        GameObject events = new GameObject("EventSystem"); events.AddComponent<EventSystem>();
        events.AddComponent<InputSystemUIInputModule>();
    }

    static void Create2D()
    {
        NewScene(true);
        Material field = Material("Metaballs 2D", "IMDM327/Metaballs2D");
        GameObject left = Quad("Object panel", dark, new Vector3(-3.3f, 0f, 0f), Vector3.one * 5.8f);
        GameObject right = Quad("Shader quad", field, new Vector3(3.3f, 0f, 0f), Vector3.one * 5.8f);
        Frame(left.transform); Frame(right.transform);
        MetaballDemo demo = right.AddComponent<MetaballDemo>(); demo.output = right.GetComponent<Renderer>();
        demo.objectPanel = left.transform; demo.mode = MetaballDemo.Mode.Metaballs;
        demo.centers = new Transform[3]; demo.disks = new Transform[3]; demo.leftMarkers = new Transform[3];
        Vector2[] positions = { new Vector2(0.3f, 0.62f), new Vector2(0.7f, 0.62f), new Vector2(0.5f, 0.22f) };
        MaterialPropertyBlock properties = new MaterialPropertyBlock();
        for (int i = 0; i < 3; i++)
        {
            Vector3 p = new Vector3(positions[i].x - 0.5f, positions[i].y - 0.5f, -0.04f);
            demo.centers[i] = Shape("Center " + (char)('A' + i), markerMesh, new[] { handle }, right.transform, p, Vector3.one * 0.018f).transform;
            demo.disks[i] = Shape("Disk " + (char)('A' + i), diskMesh, new[] { disk, outline }, left.transform, p, Vector3.one * 0.2016f).transform;
            demo.leftMarkers[i] = Shape("Center marker", markerMesh, new[] { handle }, left.transform, p + Vector3.back * 0.02f, Vector3.one * 0.018f).transform;
            properties.SetVector("_Center" + i, new Vector4(positions[i].x, positions[i].y, 0f, 0f));
        }
        properties.SetFloat("_Radius", demo.radius); properties.SetFloat("_Threshold", demo.threshold);
        properties.SetFloat("_EdgeWidth", demo.edgeWidth); properties.SetInt("_Mode", (int)demo.mode);
        demo.output.SetPropertyBlock(properties);
        Canvas canvas = Canvas(); MetaballLessonUI ui = canvas.gameObject.AddComponent<MetaballLessonUI>(); ui.demo = demo;
        Text(canvas.transform, "Objects", new Vector2(0.27f, 1f), new Vector2(0f, -142f), new Vector2(300f, 25f), 20);
        Text(canvas.transform, "Field", new Vector2(0.73f, 1f), new Vector2(0f, -142f), new Vector2(300f, 25f), 20);
        ui.modes = new Button[7];
        string[] labels = { "UV", "Distance", "Influence", "Field", "Threshold", "Smooth", "Metaballs" };
        for (int i = 0; i < ui.modes.Length; i++)
        {
            ui.modes[i] = Button(canvas.transform, labels[i], new Vector2(0.5f, 1f), new Vector2((i - 3) * 158f, -50f), new Vector2(148f, 30f));
            UnityEventTools.AddIntPersistentListener(ui.modes[i].onClick, ui.SetMode, i);
        }
        AddComparison(demo.output);
        Save(0);
    }

    static void Create3D()
    {
        NewScene(false);
        Metaball3DDemo demo = CreateVolume(Camera.main);
        GameObject volume = demo.gameObject;
        demo.desktopCamera.transform.position = volume.transform.position + Quaternion.Euler(15f, 12f, 0f) * Vector3.back * 2.5f;
        demo.desktopCamera.transform.LookAt(volume.transform.position);
        Canvas canvas = Canvas(); Metaball3DUI ui = canvas.gameObject.AddComponent<Metaball3DUI>(); ui.demo = demo;
        string[] labels = { "Bounds", "Steps", "Normals", "Surface" };
        ui.modes = new Button[4];
        for (int i = 0; i < 4; i++)
        {
            ui.modes[i] = Button(canvas.transform, labels[i], new Vector2(0.5f, 1f), new Vector2((i - 1.5f) * 158f, -50f), new Vector2(148f, 30f));
            UnityEventTools.AddIntPersistentListener(ui.modes[i].onClick, ui.SetMode, i);
        }
        Save(1);
    }

    static Metaball3DDemo CreateVolume(Camera camera, bool showFloor = true)
    {
        GameObject volume = new GameObject("Gaussian field"); volume.transform.position = new Vector3(0f, 1.35f, 0.9f);
        volume.transform.localScale = Vector3.one * 1.2f;
        Metaball3DDemo demo = volume.AddComponent<Metaball3DDemo>(); demo.desktopCamera = camera;
        GameObject proxy = GameObject.CreatePrimitive(PrimitiveType.Cube); proxy.name = "Ray bounds"; Object.DestroyImmediate(proxy.GetComponent<Collider>());
        proxy.transform.SetParent(volume.transform, false); demo.output = proxy.GetComponent<Renderer>();
        demo.output.sharedMaterial = Material("Metaballs 3D", "IMDM327/Metaballs3D");
        demo.centers = new Transform[3];
        Vector3[] positions = { new Vector3(-0.17f, 0.05f, 0f), new Vector3(0.17f, 0.05f, 0f), new Vector3(0f, -0.18f, 0.08f) };
        for (int i = 0; i < 3; i++)
        {
            GameObject center = GameObject.CreatePrimitive(PrimitiveType.Sphere); center.name = "Center " + (char)('A' + i);
            Object.DestroyImmediate(center.GetComponent<Collider>()); center.transform.SetParent(volume.transform, false);
            center.transform.localPosition = positions[i]; center.transform.localScale = Vector3.one * 0.025f;
            center.GetComponent<Renderer>().sharedMaterial = handle; demo.centers[i] = center.transform;
        }
        if (showFloor)
        {
            Material floor = Solid("3D floor", new Color(0.055f, 0.065f, 0.075f));
            GameObject ground = Quad("Reference plane", floor, new Vector3(0f, 0.72f, 0.9f), new Vector3(2.4f, 2.4f, 1f));
            ground.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            for (int i = -5; i <= 5; i++)
            {
                GameObject x = Quad("Grid", border, new Vector3(i * 0.2f, 0.722f, 0.9f), new Vector3(0.002f, 2.2f, 1f));
                GameObject z = Quad("Grid", border, new Vector3(0f, 0.723f, 0.9f + i * 0.2f), new Vector3(2.2f, 0.002f, 1f));
                x.transform.rotation = z.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }
        }
        // Initialize the material once so the saved scene also renders outside Play mode.
        typeof(Metaball3DDemo).GetField("properties", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(demo, new MaterialPropertyBlock());
        demo.Draw();
        AddComparison(demo.output, demo, camera);
        return demo;
    }

    [MenuItem("Tools/IMDM327/Generate MR Metaballs Scene")]
    public static void GenerateMR()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
        { Debug.LogWarning("Save the current scene first."); return; }
        handle = Material("Handles", "IMDM327/CenterHandle");
        CreateMR();
        var scenes = EditorBuildSettings.scenes.ToList();
        string path = Root + "Scenes/" + SceneNames[2] + ".unity";
        if (!scenes.Any(s => s.path == path)) scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        AssetDatabase.SaveAssets();
    }

    static void CreateMR()
    {
        string path = Root + "Scenes/" + SceneNames[2] + ".unity";
        if (!File.Exists(path)) AssetDatabase.CopyAsset("Assets/Scenes/SampleScene.unity", path);
        var scene = EditorSceneManager.OpenScene(path);
        foreach (var root in scene.GetRootGameObjects())
            if (root.name != "MR Interaction Setup" && root.name != "Permissions Manager" && root.name != "Lighting")
                Object.DestroyImmediate(root);
        GameObject setup = scene.GetRootGameObjects().First(obj => obj.name == "MR Interaction Setup");
        if (PrefabUtility.IsPartOfPrefabInstance(setup))
            PrefabUtility.UnpackPrefabInstance(setup, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        foreach (Transform child in setup.GetComponentsInChildren<Transform>(true))
            if (child != null && (child.name == "Goal Manager" || child.name == "Object Spawner" || child.name == "Contact Spawn Trigger"))
                Object.DestroyImmediate(child.gameObject);
        XROrigin origin = setup.GetComponentInChildren<XROrigin>(true);
        foreach (Behaviour component in origin.GetComponents<Behaviour>())
            if (component is ARPlaneManager || component is ARBoundingBoxManager || component is ARFaceManager ||
                component.GetType().Name == "ARFeatureController" || component.GetType().Name == "OcclusionManager")
                component.enabled = false;
        Camera camera = origin.Camera;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
        camera.nearClipPlane = 0.03f; camera.farClipPlane = 30f;
        RenderSettings.skybox = null;
        Metaball3DDemo demo = CreateVolume(camera, false);
        demo.desktopInput = false; demo.centerSize = 0.05f; demo.steps = 48;
        var mr = demo.gameObject.AddComponent<MetaballMR>(); mr.demo = demo; mr.head = camera.transform;
        mr.handles = new XRGrabInteractable[3];
        XRInteractionManager manager = setup.GetComponentInChildren<XRInteractionManager>();
        for (int i = 0; i < 3; i++)
        {
            GameObject center = demo.centers[i].gameObject;
            center.AddComponent<SphereCollider>().radius = 0.8f;
            var body = center.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
            var grab = center.AddComponent<XRGrabInteractable>(); grab.interactionManager = manager;
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.unparentTransformOnGrab = false; grab.retainTransformParent = true;
            grab.trackRotation = false; grab.trackScale = false; grab.throwOnDetach = false;
            grab.useDynamicAttach = true; grab.matchAttachPosition = true; grab.matchAttachRotation = false;
            grab.attachEaseInTime = 0f;
            mr.handles[i] = grab;
        }
        mr.Place(); demo.Draw();
        EditorSceneManager.MarkSceneDirty(scene); Save(2);
    }
    public static void AddComparison(Renderer output, Metaball3DDemo volume = null, Camera camera = null)
    {
        var comparison = output.GetComponentInParent<MetaballShaderComparison>();
        if (comparison == null)
            comparison = (volume == null ? output.gameObject : volume.gameObject).AddComponent<MetaballShaderComparison>();
        comparison.shaderGraph = false;
        comparison.output = output; comparison.code = output.sharedMaterial;
        comparison.graph = AssetDatabase.LoadAssetAtPath<Material>(Root + "Materials/Metaballs " + (volume == null ? "2D" : "3D") + " Graph.mat");
        comparison.volume = volume; comparison.camera = camera;
    }

    static Material Material(string name, string shader)
    {
        string path = Root + "Materials/" + name + ".mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(mat, path); }
        return mat;
    }
    static Material Solid(string name, Color color)
    {
        Material mat = Material(name, "Universal Render Pipeline/Unlit"); mat.SetColor("_BaseColor", color); EditorUtility.SetDirty(mat); return mat;
    }
    static GameObject Quad(string name, Material material, Vector3 position, Vector3 scale)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Quad); obj.name = name;
        Object.DestroyImmediate(obj.GetComponent<Collider>()); obj.GetComponent<Renderer>().sharedMaterial = material;
        obj.transform.position = position; obj.transform.localScale = scale; return obj;
    }
    static GameObject Shape(string name, Mesh mesh, Material[] materials, Transform parent, Vector3 position, Vector3 scale)
    {
        GameObject obj = new GameObject(name); obj.transform.SetParent(parent, false);
        obj.transform.localPosition = position; obj.transform.localScale = scale;
        obj.AddComponent<MeshFilter>().sharedMesh = mesh; obj.AddComponent<MeshRenderer>().sharedMaterials = materials; return obj;
    }
    static void Frame(Transform panel)
    {
        for (int i = 0; i < 4; i++)
        {
            GameObject line = Quad("Frame", border, Vector3.zero, Vector3.one); line.transform.SetParent(panel, false);
            line.transform.localPosition = new Vector3(i < 2 ? (i == 0 ? -0.502f : 0.502f) : 0f, i >= 2 ? (i == 2 ? -0.502f : 0.502f) : 0f, -0.01f);
            line.transform.localScale = i < 2 ? new Vector3(0.002f, 1.006f, 1f) : new Vector3(1.006f, 0.002f, 1f);
        }
    }
    static Mesh Disk()
    {
        string path = Root + "Materials/Disk.asset"; Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh != null) return mesh;
        mesh = new Mesh(); Vector3[] vertices = new Vector3[129]; int[] fill = new int[64 * 3], ring = new int[64 * 6];
        for (int i = 0; i < 64; i++)
        {
            Vector3 p = new Vector3(Mathf.Cos(i * Mathf.PI / 32f), Mathf.Sin(i * Mathf.PI / 32f), 0f);
            vertices[1 + i] = p * 0.48f; vertices[65 + i] = p * 0.5f;
            int next = (i + 1) % 64;
            fill[i * 3] = 0; fill[i * 3 + 1] = 1 + next; fill[i * 3 + 2] = 1 + i;
            int[] indices = { 1 + i, 65 + next, 65 + i, 1 + i, 1 + next, 65 + next };
            Array.Copy(indices, 0, ring, i * 6, 6);
        }
        mesh.vertices = vertices; mesh.subMeshCount = 2; mesh.SetTriangles(fill, 0); mesh.SetTriangles(ring, 1); mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path); return mesh;
    }
    static Mesh Marker()
    {
        string path = Root + "Materials/Marker.asset"; Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh != null) return mesh;
        mesh = new Mesh(); mesh.vertices = new[] { new Vector3(-0.5f, -0.08f, 0f), new Vector3(-0.5f, 0.08f, 0f), new Vector3(0.5f, 0.08f, 0f), new Vector3(0.5f, -0.08f, 0f),
            new Vector3(-0.08f, -0.5f, 0f), new Vector3(-0.08f, 0.5f, 0f), new Vector3(0.08f, 0.5f, 0f), new Vector3(0.08f, -0.5f, 0f) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 }; mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh, path); return mesh;
    }
    static Canvas Canvas()
    {
        GameObject obj = new GameObject("Modes", typeof(RectTransform)); Canvas canvas = obj.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = obj.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = 0.5f; obj.AddComponent<GraphicRaycaster>(); return canvas;
    }
    static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = anchor; rect.pivot = Vector2.one * 0.5f; rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
    }
    static TMP_Text Text(Transform parent, string value, Vector2 anchor, Vector2 position, Vector2 size, float fontSize)
    {
        TMP_Text text = Rect("Caption", parent, anchor, position, size).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset; text.text = value; text.fontSize = fontSize; text.color = textColor;
        text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false; return text;
    }
    static Button Button(Transform parent, string label, Vector2 anchor, Vector2 position, Vector2 size)
    {
        GameObject obj = Rect(label, parent, anchor, position, size).gameObject; Image image = obj.AddComponent<Image>(); image.color = new Color(0.11f, 0.13f, 0.16f);
        Button button = obj.AddComponent<Button>(); button.targetGraphic = image;
        ColorBlock colors = button.colors; colors.highlightedColor = new Color(0.7f, 0.75f, 0.8f); colors.disabledColor = new Color(0.5f, 0.35f, 0.2f); button.colors = colors;
        Text(obj.transform, label, Vector2.one * 0.5f, Vector2.zero, size, 16f); return button;
    }
    static void Save(int scene)
    {
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), Root + "Scenes/" + SceneNames[scene] + ".unity");
    }
}

