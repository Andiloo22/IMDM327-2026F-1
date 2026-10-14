Shader "IMDM327/Basics/Circle"
{
    Properties
    {
        _Color ("Color", Color) = (0.1, 0.8, 0.9, 1)
        _Center ("Center", Vector) = (0.5, 0.5, 0, 0)
        _Radius ("Radius", Range(0.05, 0.45)) = 0.28
        _EdgeWidth ("Edge Width", Range(0.001, 0.1)) = 0.01
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
                float4 _Color, _Center;
                float _Radius, _EdgeWidth;
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
                // Each fragment measures its distance from the center.
                float d = distance(input.uv, _Center.xy);
                // Inside is 1, outside is 0. Smoothstep softens the edge. 
                // https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-smoothstep
                float mask = 1 - smoothstep(_Radius - _EdgeWidth, _Radius + _EdgeWidth, d);
                return half4(_Color.rgb * mask, 1);
            }
            ENDHLSL
        }
    }
}
