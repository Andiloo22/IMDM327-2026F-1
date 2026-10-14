using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace IMDM327.EDMBoids
{
    public class EDMAfterimage : ScriptableRendererFeature
    {
        public Shader shader;
        Material material;
        AfterimagePass pass;

        public override void Create()
        {
            pass?.Dispose();
            CoreUtils.Destroy(material);
            if (shader == null) return;
            material = CoreUtils.CreateEngineMaterial(shader);
            pass = new AfterimagePass(material);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (pass == null || renderingData.cameraData.cameraType != CameraType.Game) return;
            EDMReactiveCamera camera = renderingData.cameraData.camera.GetComponent<EDMReactiveCamera>();
            if (camera == null || !camera.isActiveAndEnabled) return;
            if (!camera.afterimage)
            {
                pass.Dispose();
                return;
            }
            pass.seconds = camera.afterimageSeconds;
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            pass?.Dispose();
            CoreUtils.Destroy(material);
        }

        class AfterimagePass : ScriptableRenderPass
        {
            public float seconds;
            Material material;
            RTHandle history;
            bool ready;

            class PassData
            {
                public TextureHandle source;
                public Material material;
            }

            public AfterimagePass(Material value)
            {
                material = value;
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
                ConfigureInput(ScriptableRenderPassInput.Color);
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                UniversalCameraData camera = frameData.Get<UniversalCameraData>();
                if (resources.isActiveTargetBackBuffer) return;

                RenderTextureDescriptor desc = camera.cameraTargetDescriptor;
                desc.depthBufferBits = 0;
                desc.msaaSamples = 1;
                desc.bindMS = false;
                desc.useMipMap = false;
                desc.autoGenerateMips = false;
                if (RenderingUtils.ReAllocateHandleIfNeeded(ref history, desc, FilterMode.Bilinear,
                    TextureWrapMode.Clamp, name: "Afterimage history")) ready = false;

                TextureHandle source = resources.activeColorTexture;
                TextureHandle previous = graph.ImportTexture(history);
                TextureDesc outputDesc = graph.GetTextureDesc(source);
                outputDesc.name = "Afterimage";
                outputDesc.clearBuffer = false;
                TextureHandle output = graph.CreateTexture(outputDesc);
                float decay = 0f;
                if (ready) decay = Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(seconds, 0.05f));
                material.SetFloat("_Decay", decay);
                material.SetTexture("_History", history.rt);

                // Draw this frame over the fading previous frame.
                using (var builder = graph.AddRasterRenderPass<PassData>("Afterimage", out var data))
                {
                    data.source = source;
                    data.material = material;
                    builder.UseTexture(source, AccessFlags.Read);
                    builder.UseTexture(previous, AccessFlags.Read);
                    builder.SetRenderAttachment(output, 0, AccessFlags.Write);
                    builder.SetRenderFunc<PassData>(Draw);
                }
                graph.AddBlitPass(output, previous, Vector2.one, Vector2.zero, passName: "Save afterimage");
                resources.cameraColor = output;
                ready = true;
            }

            static void Draw(PassData data, RasterGraphContext context)
            {
                Blitter.BlitTexture(context.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.material, 0);
            }

            public void Dispose()
            {
                history?.Release();
                history = null;
                ready = false;
            }
        }
    }
}
