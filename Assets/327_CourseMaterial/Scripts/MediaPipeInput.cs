using System.Collections;
using System.IO;
using Mediapipe;
using Mediapipe.Unity;
using Mediapipe.Unity.Sample;
using Mediapipe.Unity.Sample.Holistic;
using UnityEngine;

[RequireComponent(typeof(HolisticTrackingGraph))]
public class MediaPipeInput : MonoBehaviour
{
    public HolisticTrackingGraph graphRunner;
    public int cameraIndex;
    public bool showCamera = true;
    public bool mirrorX = true;
    WebCamSource webcam;
    Mediapipe.Unity.Experimental.TextureFramePool frames;
    bool logging;

    void OnEnable()
    {
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        yield return null;
        if (graphRunner == null) graphRunner = GetComponent<HolisticTrackingGraph>();
        if (WebCamTexture.devices.Length == 0)
        {
            Debug.LogWarning("Connect a webcam to use the gesture sample.");
            yield break;
        }
        Glog.Initialize("IMDM327");
        logging = true;
        Mediapipe.Logger.MinLogLevel = Mediapipe.Logger.LogLevel.Warn;
        Glog.Minloglevel = 2;

        string folder = Path.Combine(Application.persistentDataPath, "MediaPipe");
        Directory.CreateDirectory(folder);
        foreach (string file in Directory.GetFiles(Path.Combine(Application.streamingAssetsPath, "MediaPipe")))
        {
            if (Path.GetExtension(file) == ".meta") continue;
            string destination = Path.Combine(folder, Path.GetFileName(file));
            if (!File.Exists(destination)) File.Copy(file, destination);
        }
        AssetLoader.Provide(new StreamingAssetsResourceManager("MediaPipe"));
        webcam = new WebCamSource(1280, new[] { new ImageSource.ResolutionStruct(1280, 720, 30) });
        webcam.SelectSource(Mathf.Clamp(cameraIndex, 0, WebCamTexture.devices.Length - 1));
        yield return webcam.Play();
        if (!webcam.isPrepared) yield break;

        var initialization = graphRunner.WaitForInit(RunningMode.Async);
        yield return initialization;
        if (initialization.isError)
        {
            Debug.LogError(initialization.error);
            yield break;
        }
        frames = new Mediapipe.Unity.Experimental.TextureFramePool(webcam.textureWidth, webcam.textureHeight, TextureFormat.RGBA32, 4);
        graphRunner.StartRun(webcam);
        while (isActiveAndEnabled)
        {
            if (!frames.TryGetTextureFrame(out var frame))
            {
                yield return null;
                continue;
            }
            var request = frame.ReadTextureAsync(webcam.GetCurrentTexture(), false, webcam.isVerticallyFlipped);
            yield return new WaitUntil(() => request.done);
            if (request.hasError)
            {
                frame.Release();
                yield return null;
                continue;
            }
            graphRunner.AddTextureFrameToInputStream(frame);
            yield return null;
        }
    }

    void OnGUI()
    {
        if (!showCamera || webcam == null || !webcam.isPrepared) return;
        float width = Mathf.Min(240f, UnityEngine.Screen.width * 0.25f);
        UnityEngine.Rect rect = new UnityEngine.Rect(12f, 12f, width, width * webcam.textureHeight / webcam.textureWidth);
        Matrix4x4 previous = GUI.matrix;
        Vector2 scale = Vector2.one;
        if (mirrorX) scale.x = -1f;
        if (webcam.isVerticallyFlipped) scale.y = -1f;
        GUIUtility.ScaleAroundPivot(scale, rect.center);
        GUI.DrawTexture(rect, webcam.GetCurrentTexture(), UnityEngine.ScaleMode.StretchToFill);
        GUI.matrix = previous;
    }

    void OnDisable()
    {
        StopAllCoroutines();
        MediaPipeBodyTracker tracker = GetComponent<MediaPipeBodyTracker>();
        if (tracker != null) tracker.StopTracking();
        if (graphRunner != null) graphRunner.Stop();
        if (webcam != null) webcam.Stop();
        if (frames != null) frames.Dispose();
        frames = null;
        webcam = null;
        if (logging) Glog.Shutdown();
        logging = false;
    }
}
