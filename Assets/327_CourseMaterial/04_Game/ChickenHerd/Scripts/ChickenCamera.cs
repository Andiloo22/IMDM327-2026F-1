using UnityEngine;
using UnityEngine.Rendering;

public class ChickenCamera : MonoBehaviour
{
    public float fieldOfView = 60f;
    public Transform player;
    public Vector3 offset = new Vector3(0f, 4.5f, -7f);
    public RenderPipelineAsset pipeline;
    RenderPipelineAsset previousPipeline;
    Camera cam;

    void Start()
    {
        previousPipeline = QualitySettings.renderPipeline;
        if (pipeline != null) QualitySettings.renderPipeline = pipeline;
        cam = GetComponent<Camera>();
        cam.ResetAspect();
        transform.position = player.position + offset;
    }

    void LateUpdate()
    {
        cam.fieldOfView = fieldOfView;
        transform.position = Vector3.Lerp(transform.position, player.position + offset, 6f * Time.deltaTime);
        transform.LookAt(player.position + new Vector3(0f, 1f, 4f));
    }

    void OnDisable()
    {
        if (pipeline != null && QualitySettings.renderPipeline == pipeline)
            QualitySettings.renderPipeline = previousPipeline;
    }
}
