using UnityEngine;

public static class GaussianField
{
    public static float Influence(Vector2 p, Vector2 center, float radius)
    {
        Vector2 d = p - center;
        return Mathf.Exp(-Vector2.Dot(d, d) / (2f * radius * radius));
    }

    public static Vector2 SamplePoint(Vector2 p, int samples)
    {
        if (samples <= 0) return p;
        return new Vector2((Mathf.Floor(p.x * samples) + 0.5f) / samples,
            (Mathf.Floor(p.y * samples) + 0.5f) / samples);
    }

    public static float Influence(Vector3 p, Vector3 center, float radius)
    {
        Vector3 d = p - center;
        return Mathf.Exp(-Vector3.Dot(d, d) / (2f * radius * radius));
    }

    public static Vector3 Gradient(Vector3 p, Vector3 center, float radius)
    {
        return -(p - center) / (radius * radius) * Influence(p, center, radius);
    }
}
