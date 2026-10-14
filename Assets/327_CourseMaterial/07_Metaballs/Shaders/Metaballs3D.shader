Shader "IMDM327/Metaballs3D"
{
    Properties
    {
        _Color ("Color", Color) = (0.82, 0.85, 0.89, 1)
        _Radius ("Radius", Float) = 0.16
        _Threshold ("Threshold", Float) = 0.82
        _Steps ("Ray steps", Int) = 96
        _Mode ("Mode", Int) = 3
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color, _Center0, _Center1, _Center2, _BoundsMin, _BoundsMax;
                float4x4 _WorldToField, _FieldToWorld;
                float _Radius, _Threshold;
                int _Steps, _Mode;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            struct Output
            {
                half4 color : SV_Target;
                float depth : SV_Depth;
            };
            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }
            float Influence(float3 p, float3 center)
            {
                float3 d = p - center;
                return exp(-dot(d, d) / (2.0 * _Radius * _Radius));
            }
            float Field(float3 p)
            {
                return Influence(p, _Center0.xyz) + Influence(p, _Center1.xyz) + Influence(p, _Center2.xyz);
            }
            float3 Gradient(float3 p)
            {
                float3 gradient = -(p - _Center0.xyz) * Influence(p, _Center0.xyz)
                                - (p - _Center1.xyz) * Influence(p, _Center1.xyz)
                                - (p - _Center2.xyz) * Influence(p, _Center2.xyz);
                return gradient / (_Radius * _Radius);
            }
            Output frag(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 camera = GetCameraPositionWS();
                float3 origin = mul(_WorldToField, float4(camera, 1.0)).xyz;
                float3 direction = normalize(mul((float3x3)_WorldToField, input.positionWS - camera));

                // Find the ray's entry and exit distances through the field bounds.
                float3 inverseRay = (step(0.0, direction) * 2.0 - 1.0) / max(abs(direction), 0.000001);
                float3 a = (_BoundsMin.xyz - origin) * inverseRay;
                float3 b = (_BoundsMax.xyz - origin) * inverseRay;
                float3 near = min(a, b), far = max(a, b);
                float start = max(max(near.x, max(near.y, near.z)), 0.0);
                float end = min(far.x, min(far.y, far.z));
                if (end <= start) discard;

                float hit = start;
                int usedSteps = 0;
                if (_Mode != 0)
                {
                    // Gaussian density is NOT an SDF: use fixed steps to bracket a crossing.
                    float previous = Field(origin + direction * start) - _Threshold;
                    float previousT = start;
                    bool found = false;
                    [loop] for (int i = 1; i <= 128; i++)
                    {
                        if (i > _Steps) break;
                        float t = lerp(start, end, (float)i / _Steps);
                        float value = Field(origin + direction * t) - _Threshold;
                        if ((previous < 0.0 && value >= 0.0) || (previous >= 0.0 && value < 0.0))
                        {
                            float low = previousT, high = t;
                            [unroll] for (int j = 0; j < 6; j++)
                            {
                                float middle = (low + high) * 0.5;
                                float midValue = Field(origin + direction * middle) - _Threshold;
                                if ((previous < 0.0) == (midValue < 0.0)) low = middle;
                                else high = middle;
                            }
                            hit = (low + high) * 0.5; usedSteps = i; found = true; break;
                        }
                        previous = value; previousT = t;
                    }
                    if (!found) discard;
                }
                float3 p = origin + direction * hit;
                float3 gradient = -Gradient(p);
                float3 normal = normalize(mul(transpose((float3x3)_WorldToField), gradient) + float3(0.0, 0.000001, 0.0));
                float3 world = mul(_FieldToWorld, float4(p, 1.0)).xyz;
                float3 light = normalize(float3(-0.4, 0.8, -0.6));
                float diffuse = saturate(dot(normal, light));
                float3 halfway = normalize(light + normalize(camera - world));
                float highlight = pow(saturate(dot(normal, halfway)), 24.0) * 0.15;
                float3 color = _Color.rgb * (0.25 + diffuse * 0.75) + highlight;
                if (_Mode == 0) color = float3(0.14, 0.18, 0.22);
                if (_Mode == 1) color = ((float)usedSteps / _Steps).xxx;
                if (_Mode == 2) color = normal * 0.5 + 0.5;

                float4 clipPosition = TransformWorldToHClip(world);
                Output output;
                output.color = half4(color, 1.0);
                // Write the surface depth, not the back of the proxy cube.
                output.depth = clipPosition.z / clipPosition.w;
                #if !UNITY_REVERSED_Z
                output.depth = (output.depth - UNITY_NEAR_CLIP_VALUE) / (1.0 - UNITY_NEAR_CLIP_VALUE);
                #endif
                return output;
            }
            ENDHLSL
        }
    }
}
