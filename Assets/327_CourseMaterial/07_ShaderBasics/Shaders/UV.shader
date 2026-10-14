Shader "IMDM327/Basics/UV"
{
    Properties
    {
        _Tiling ("Tiling", Vector) = (1, 1, 0, 0)
        _Offset ("Offset", Vector) = (0, 0, 0, 0)
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
                float4 _Tiling, _Offset;
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
                // U becomes red. V becomes green. Both normally go from 0 to 1.
                float2 uv = input.uv * _Tiling.xy + _Offset.xy;
                return half4(uv.x, uv.y, 0, 1);
            }
            ENDHLSL
        }
    }
}
