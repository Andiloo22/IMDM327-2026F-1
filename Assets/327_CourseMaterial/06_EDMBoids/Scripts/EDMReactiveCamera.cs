using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace IMDM327.EDMBoids
{
    [RequireComponent(typeof(Camera))]
    public class EDMReactiveCamera : MonoBehaviour
    {
        public EDMBoidsController boids;
        public UniversalRenderPipelineAsset lightRenderPipeline;
        public bool autoOrbit = true;
        public bool afterimage = true;
        [Range(0.05f, 2f)] public float afterimageSeconds = 0.35f;
        public float orbitDegreesPerSecond = 0.35f, radius = 12.8f, height = 0.7f;
        float angle = 180f;
        VolumeProfile profile;
        RenderPipelineAsset previousPipeline;
        UniversalRenderPipelineAsset lightPipeline;

        void Start()
        {
            // Restore this pipeline when the scene ends.
            previousPipeline = QualitySettings.renderPipeline;
            UniversalRenderPipelineAsset urp = lightRenderPipeline != null ? lightRenderPipeline : GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
            {
                lightPipeline = Instantiate(urp);
                lightPipeline.supportsHDR = true;
                QualitySettings.renderPipeline = lightPipeline;
            }
            if (boids == null) boids = FindFirstObjectByType<EDMBoidsController>();
            Camera cam = GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.0015f, 0.002f, 0.006f);
            cam.fieldOfView = 48f;
            cam.allowHDR = true;
            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            GameObject obj = new GameObject("Light field grading");
            obj.transform.SetParent(transform, false);
            Volume volume = obj.AddComponent<Volume>();
            volume.isGlobal = true;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile = profile;
            Bloom bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.8f);
            bloom.intensity.Override(0.65f);
            bloom.scatter.Override(0.72f);
            bloom.highQualityFiltering.Override(true);
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            Vignette vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.18f);
            vignette.smoothness.Override(0.7f);
            Place();
        }

        void LateUpdate()
        {
            if (autoOrbit) angle += orbitDegreesPerSecond * Time.deltaTime;
            Place();
        }

        void Place()
        {
            Vector3 target = boids != null ? boids.center : Vector3.zero;
            float a = angle * Mathf.Deg2Rad;
            transform.position = target + new Vector3(Mathf.Sin(a) * radius, height, Mathf.Cos(a) * radius);
            transform.LookAt(target);
        }

        void OnDestroy()
        {
            if (QualitySettings.renderPipeline == lightPipeline) QualitySettings.renderPipeline = previousPipeline;
            if (lightPipeline != null) Destroy(lightPipeline);
            if (profile != null)
            {
                foreach (var c in profile.components) Destroy(c);
                Destroy(profile);
            }
        }
    }
}
