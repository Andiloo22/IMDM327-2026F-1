Shader "IMDM327/Basics/Color"
{
    Properties
    {
        _Color ("Color", Color) = (0.1, 0.8, 0.9, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // C# and the Material Inspector can change these properties.
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            // The mesh gives us a position and UV coordinates.
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                // Move the vertex from object space to the screen.
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // Pass UV coordinates to the fragment shader.
                output.uv = input.uv;
                return output;
            }

            // This runs for each fragment drawn on the screen.
            half4 frag(Varyings input) : SV_Target
            {
                // Every fragment receives the same color.
                return _Color;
            }
            ENDHLSL
        }
    }
}
