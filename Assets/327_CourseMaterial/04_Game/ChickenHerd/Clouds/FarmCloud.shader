Shader "IMDM327/Farm Cloud"
{
    Properties
    {
        _Noise ("Volume noise", 3D) = "white" {}
        _Coverage ("Coverage threshold", Range(0.4, 0.85)) = 0.58
        _Density ("Density", Range(0.1, 3)) = 1.2
        _Wind ("Wind", Vector) = (0.012, 0, 0.004, 0)
        _CloudColor ("Cloud color", Color) = (1, 0.97, 0.9, 1)
        _ShadowColor ("Shadow color", Color) = (0.55, 0.65, 0.72, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Front
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE3D(_Noise);
            SAMPLER(sampler_Noise);
            CBUFFER_START(UnityPerMaterial)
                float4 _Wind, _CloudColor, _ShadowColor;
                float _Coverage, _Density;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            float Density(float3 p)
            {
                float shape = saturate(1.0 - dot(p * 2.0, p * 2.0));
                float3 uv = p + 0.5 + TransformObjectToWorld(float3(0, 0, 0)) * 0.017 + _Time.y * _Wind.xyz;
                float2 noise = SAMPLE_TEXTURE3D_LOD(_Noise, sampler_Noise, uv, 0).rg;
                float density = saturate((noise.r - _Coverage) / (1.0 - _Coverage));
                return saturate(density - (1.0 - noise.g) * 0.12) * shape * _Density;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 origin = TransformWorldToObject(_WorldSpaceCameraPos);
                float3 directionWS = normalize(input.positionWS - _WorldSpaceCameraPos);
                float3 direction = TransformWorldToObjectDir(directionWS, false);
                float3 safeDirection = direction + (abs(direction) < 0.00001) * 0.00001;
                float3 a = (-0.5 - origin) / safeDirection;
                float3 b = (0.5 - origin) / safeDirection;
                float3 nearBox = min(a, b), farBox = max(a, b);
                float nearDistance = max(0.0, max(nearBox.x, max(nearBox.y, nearBox.z)));
                float farDistance = min(farBox.x, min(farBox.y, farBox.z));

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float depth = SampleSceneDepth(screenUV);
                #if !UNITY_REVERSED_Z
                    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, depth);
                #endif
                float3 opaquePosition = ComputeWorldSpacePosition(screenUV, depth, UNITY_MATRIX_I_VP);
                farDistance = min(farDistance, length(opaquePosition - _WorldSpaceCameraPos));
                if (farDistance <= nearDistance) return 0;

                float stepLength = (farDistance - nearDistance) / 40.0;
                float transmission = 1.0;
                float3 color = 0;
                float3 sunlight = TransformWorldToObjectDir(normalize(float3(-0.5, 1.0, -0.3)), false);
                // Accumulate density through the volume, from front to back.
                for (int i = 0; i < 40; i++)
                {
                    float distance = nearDistance + (i + 0.5) * stepLength;
                    float3 p = origin + direction * distance;
                    float density = Density(p);
                    float alpha = 1.0 - exp(-density * stepLength);
                    float light = exp(-Density(p + sunlight * 1.5) * 2.0);
                    float3 sampleColor = lerp(_ShadowColor.rgb, _CloudColor.rgb, light);
                    color += transmission * alpha * sampleColor;
                    transmission *= 1.0 - alpha;
                    if (transmission < 0.02) break;
                }
                return half4(color, 1.0 - transmission);
            }
            ENDHLSL
        }
    }
}
