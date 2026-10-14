Shader "IMDM327/Metaballs2D"
{
    Properties
    {
        _Color ("Color", Color) = (0.88, 0.91, 0.94, 1)
        _Radius ("Radius", Range(0.04, 0.3)) = 0.16
        _Threshold ("Threshold", Range(0.2, 1.8)) = 0.82
        _EdgeWidth ("Edge width", Range(0.001, 0.1)) = 0.03
        _Center0 ("Center A", Vector) = (0.3, 0.62, 0, 0)
        _Center1 ("Center B", Vector) = (0.7, 0.62, 0, 0)
        _Center2 ("Center C", Vector) = (0.5, 0.22, 0, 0)
        _Mode ("Mode", Int) = 6
        _Grid ("Grid", Int) = 0
        _Samples ("Field samples per axis (0 = continuous)", Int) = 0
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

            CBUFFER_START(UnityPerMaterial)
                float4 _Color, _Center0, _Center1, _Center2;
                float _Radius, _Threshold, _EdgeWidth;
                int _Mode, _Grid, _Samples;
            CBUFFER_END

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
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            // A center's influence becomes weaker as the distance increases.
            float Influence(float2 p, float2 center)
            {
                float2 d = p - center;
                return exp(-dot(d, d) / (2.0 * _Radius * _Radius));
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 p = input.uv;
                if (_Samples > 0) p = (floor(p * _Samples) + 0.5) / _Samples;

                float a = Influence(p, _Center0.xy);
                float field = a + Influence(p, _Center1.xy) + Influence(p, _Center2.xy);
                float mask = smoothstep(_Threshold - _EdgeWidth, _Threshold + _EdgeWidth, field);

                float3 color;
                if (_Mode == 0) color = float3(p.x, p.y, 0.0);          // UV
                else if (_Mode == 1) color = distance(p, _Center0.xy).xxx;
                else if (_Mode == 2) color = a.xxx;                    // One influence
                else if (_Mode == 3) color = (field / 3.0).xxx;        // Sum, displayed on a 0..3 scale
                else if (_Mode == 4) color = step(_Threshold, field).xxx;
                else if (_Mode == 5) color = mask.xxx;
                else color = lerp(float3(0.035, 0.04, 0.05), _Color.rgb, mask);

                if (_Grid == 1)
                {
                    float2 gridDistance = abs(frac(input.uv * 10.0) - 0.5);
                    float grid = step(0.49, max(gridDistance.x, gridDistance.y));
                    color = lerp(color, float3(0.25, 0.28, 0.3), grid * 0.6);
                }
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
