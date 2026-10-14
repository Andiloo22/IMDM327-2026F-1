#ifndef METABALLS_RAYMARCH_INCLUDED
#define METABALLS_RAYMARCH_INCLUDED

float Gaussian3D(float3 p, float3 center, float radius)
{
    float3 d = p - center;
    return exp(-dot(d, d) / (2.0 * radius * radius));
}

float Field3D(float3 p, float3 a, float3 b, float3 c, float radius)
{
    return Gaussian3D(p, a, radius) + Gaussian3D(p, b, radius) + Gaussian3D(p, c, radius);
}

void Raymarch_float(float3 ViewDirection, float3 A, float3 B, float3 C,
    float Radius, float Threshold, float Steps, float Mode,
    float3 BoundsMin, float3 BoundsMax, float4x4 WorldToField, float4x4 FieldToWorld,
    out float3 World, out float3 Normal, out float StepFraction, out float Depth, out float Hit)
{
    World = Normal = 0.0;
    StepFraction = Hit = 0.0;
    Depth = 0.0;
    #if !defined(SHADERGRAPH_PREVIEW)
    Depth = UNITY_RAW_FAR_CLIP_VALUE;
    float3 camera = GetCameraPositionWS();
    float3 origin = mul(WorldToField, float4(camera, 1.0)).xyz;
    float3 direction = normalize(mul((float3x3)WorldToField, -ViewDirection));

    float3 inverseRay = (step(0.0, direction) * 2.0 - 1.0) / max(abs(direction), 0.000001);
    float3 a = (BoundsMin - origin) * inverseRay;
    float3 b = (BoundsMax - origin) * inverseRay;
    float3 near = min(a, b), far = max(a, b);
    float start = max(max(near.x, max(near.y, near.z)), 0.0);
    float end = min(far.x, min(far.y, far.z));
    if (end <= start) return;

    float hit = start;
    int usedSteps = 0;
    int stepCount = (int)Steps;
    if ((int)Mode != 0)
    {
        // Gaussian density is not an SDF. Find a crossing with fixed steps.
        float previous = Field3D(origin + direction * start, A, B, C, Radius) - Threshold;
        float previousT = start;
        bool found = false;
        [loop] for (int i = 1; i <= 128; i++)
        {
            if (i > stepCount) break;
            float t = lerp(start, end, (float)i / stepCount);
            float value = Field3D(origin + direction * t, A, B, C, Radius) - Threshold;
            if ((previous < 0.0 && value >= 0.0) || (previous >= 0.0 && value < 0.0))
            {
                float low = previousT, high = t;
                [loop] for (int j = 0; j < 6; j++)
                {
                    float middle = (low + high) * 0.5;
                    float midValue = Field3D(origin + direction * middle, A, B, C, Radius) - Threshold;
                    if ((previous < 0.0) == (midValue < 0.0)) low = middle;
                    else high = middle;
                }
                hit = (low + high) * 0.5; usedSteps = i; found = true; break;
            }
            previous = value; previousT = t;
        }
        if (!found) return;
    }

    Hit = 1.0;
    float3 p = origin + direction * hit;
    float3 gradient = -(p - A) * Gaussian3D(p, A, Radius)
                    - (p - B) * Gaussian3D(p, B, Radius)
                    - (p - C) * Gaussian3D(p, C, Radius);
    gradient /= Radius * Radius;
    Normal = normalize(mul(transpose((float3x3)WorldToField), -gradient) + float3(0.0, 0.000001, 0.0));
    World = mul(FieldToWorld, float4(p, 1.0)).xyz;
    StepFraction = (float)usedSteps / stepCount;
    float4 clipPosition = TransformWorldToHClip(World);
    Depth = clipPosition.z / clipPosition.w;
    #if !UNITY_REVERSED_Z
    Depth = (Depth - UNITY_NEAR_CLIP_VALUE) / (1.0 - UNITY_NEAR_CLIP_VALUE);
    #endif
    #endif
}

// Use the hit point, rather than the fullscreen triangle, for the highlight.
void SurfaceView_float(float3 World, out float3 Direction)
{
    Direction = float3(0.0, 0.0, 1.0);
    #if !defined(SHADERGRAPH_PREVIEW)
    Direction = normalize(GetCameraPositionWS() - World);
    #endif
}
#endif
