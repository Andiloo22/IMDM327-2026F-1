Shader "Hidden/IMDM327/DepthProbe"
{
    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_Depth); SAMPLER(sampler_Depth);
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes input)
            {
                Varyings output; output.positionCS = TransformObjectToHClip(input.positionOS.xyz); output.uv = input.uv; return output;
            }
            float4 frag(Varyings input) : SV_Target
            {
                float depth = SAMPLE_TEXTURE2D(_Depth, sampler_Depth, input.uv).r;
                return float4(depth, depth, depth, 1.0);
            }
            ENDHLSL
        }
    }
}
