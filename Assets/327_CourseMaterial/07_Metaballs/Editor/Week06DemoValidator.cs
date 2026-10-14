using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using System.IO;
using System.Linq;
using UnityEditor.Build.Reporting;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor.Rendering;
using UnityEditor.Build;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

public static class Week06DemoValidator
{
    public static bool BuildingMR;
    [MenuItem("Tools/IMDM327/Validate Week06 Metaballs Demo")]
    public static void Run()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
        { Debug.LogWarning("Save the current scene before running the Week06 tests."); return; }
        TestRunnerApi runner = ScriptableObject.CreateInstance<TestRunnerApi>();
        runner.Execute(new ExecutionSettings(new Filter {
            testMode = TestMode.EditMode,
            assemblyNames = new[] { "Week06.Metaballs.Tests" }
        }));
    }

    [MenuItem("Tools/IMDM327/Build Week06 Android Demo")]
    public static void BuildAndroid()
    {
        string path = Path.GetFullPath("Builds/Week06/Week06-Metaballs.apk");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string[] scenes = { Week06DemoGenerator.SceneNames[2], Week06DemoGenerator.SceneNames[0], Week06DemoGenerator.SceneNames[1] };
        string settingsPath = Week06DemoGenerator.Root + "Materials/AndroidGlobalSettings.asset";
        var settings = AssetDatabase.LoadAssetAtPath<RenderPipelineGlobalSettings>(settingsPath);
        if (settings == null)
        {
            var type = typeof(UniversalRenderPipelineAsset).Assembly.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineGlobalSettings");
            settings = RenderPipelineGlobalSettingsUtils.Create(type, settingsPath);
        }
        var previous = GraphicsSettings.GetSettingsForRenderPipeline<UniversalRenderPipeline>();
        var previousGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
        EditorUserBuildSettings.selectedBuildTargetGroup = BuildTargetGroup.Android;
        var xr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        var metaCore = xr.GetFeatures<OpenXRFeature>().FirstOrDefault(feature => feature.GetType().FullName == "Meta.XR.MetaXRFeature");
        bool metaCoreEnabled = metaCore != null && metaCore.enabled;
        // This lesson uses AR Foundation, so it needs only Unity's OpenXR loader.
        if (metaCore != null) metaCore.enabled = false;
        BuildReport report;
        EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset(typeof(UniversalRenderPipeline), settings);
        try
        {
            BuildingMR = true;
            report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = scenes.Select(name => Week06DemoGenerator.Root + "Scenes/" + name + ".unity").ToArray(),
                locationPathName = path, target = BuildTarget.Android, options = BuildOptions.Development
            });
        }
        finally
        {
            BuildingMR = false;
            EditorUserBuildSettings.selectedBuildTargetGroup = previousGroup;
            if (metaCore != null) metaCore.enabled = metaCoreEnabled;
            EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset(typeof(UniversalRenderPipeline), previous);
        }
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "IMDM327-week06-android.txt"),
            report.summary.result + ": " + report.summary.totalErrors + " errors. " + report.summary.outputPath);
        if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}

public class Week06MRPlugins : IPreprocessBuildWithReport
{
    public int callbackOrder => int.MaxValue;
    public void OnPreprocessBuild(BuildReport report)
    {
        if (!Week06DemoValidator.BuildingMR || report.summary.platform != BuildTarget.Android) return;
        foreach (var importer in PluginImporter.GetAllImporters())
            if (importer.assetPath.Contains("com.meta.xr.sdk.core") && importer.assetPath.EndsWith("OVRPlugin.aar"))
                importer.SetIncludeInBuildDelegate(path => !Week06DemoValidator.BuildingMR);
    }
}
