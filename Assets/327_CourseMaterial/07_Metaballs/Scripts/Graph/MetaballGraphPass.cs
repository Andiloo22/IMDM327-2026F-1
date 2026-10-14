using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class MetaballGraphPass : ScriptableRendererFeature
{
    GraphPass pass;

    public override void Create()
    {
        pass = new GraphPass();
        pass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        foreach (MetaballShaderComparison demo in Object.FindObjectsByType<MetaballShaderComparison>(FindObjectsSortMode.None))
        {
            if (!demo.isActiveAndEnabled || !demo.shaderGraph || demo.volume == null || demo.camera != renderingData.cameraData.camera) continue;
            if ((demo.camera.cullingMask & (1 << demo.output.gameObject.layer)) == 0) continue;

            Metaball3DDemo volume = demo.volume;
            Material material = demo.graph;
            for (int i = 0; i < 3; i++) material.SetVector("_Center" + i, volume.centers[i].localPosition);
            material.SetVector("_BoundsMin", volume.boundsMin);
            material.SetVector("_BoundsMax", volume.boundsMax);
            material.SetFloat("_Radius", volume.radius);
            material.SetFloat("_Threshold", volume.threshold);
            material.SetFloat("_Steps", volume.steps);
            material.SetFloat("_Mode", (int)volume.mode);
            material.SetMatrix("_WorldToField", volume.transform.worldToLocalMatrix);
            material.SetMatrix("_FieldToWorld", volume.transform.localToWorldMatrix);
            pass.material = material;
            renderer.EnqueuePass(pass);
        }
    }

    class GraphPass : ScriptableRenderPass
    {
        public Material material;
        class Data { public Material material; public int pass; }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            using (var builder = renderGraph.AddRasterRenderPass<Data>("Metaballs Graph", out var data))
            {
                data.material = material;
                data.pass = material.FindPass("DrawProcedural");
                // Keep the camera's existing color and depth for overlaps.
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((Data d, RasterGraphContext context) =>
                    context.cmd.DrawProcedural(Matrix4x4.identity, d.material, d.pass, MeshTopology.Triangles, 3));
            }
        }
    }
}
