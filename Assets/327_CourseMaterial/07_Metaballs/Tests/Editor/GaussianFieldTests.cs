using NUnit.Framework;
using UnityEngine;

public class GaussianFieldTests
{
    [Test]
    public void CenterHasUnitInfluence()
    {
        Vector2 center = new Vector2(0.3f, 0.62f);
        Assert.That(GaussianField.Influence(center, center, 0.16f), Is.EqualTo(1f));
    }
    [Test]
    public void InfluenceIsSymmetricAndDecreasesWithDistance()
    {
        float near = GaussianField.Influence(new Vector2(0.1f, 0f), Vector2.zero, 0.16f);
        float far = GaussianField.Influence(new Vector2(0.2f, 0f), Vector2.zero, 0.16f);
        Assert.That(near, Is.GreaterThan(far));
        Assert.That(near, Is.EqualTo(GaussianField.Influence(new Vector2(-0.1f, 0f), Vector2.zero, 0.16f)));
    }
    [Test]
    public void AdditionDoesNotDependOnCenterOrder()
    {
        Vector2 p = new Vector2(0.4f, 0.5f);
        float a = GaussianField.Influence(p, new Vector2(0.3f, 0.62f), 0.16f);
        float b = GaussianField.Influence(p, new Vector2(0.7f, 0.62f), 0.16f);
        float c = GaussianField.Influence(p, new Vector2(0.5f, 0.22f), 0.16f);
        Assert.That(a + b + c, Is.EqualTo(c + a + b).Within(0.000001f));
        Assert.That(a + b + c, Is.GreaterThan(a));
    }
    [Test]
    public void IncreasingThresholdCannotExpandTheRegion()
    {
        int low = 0, high = 0;
        for (int x = 0; x <= 40; x++)
        for (int y = 0; y <= 40; y++)
        {
            Vector2 p = new Vector2(x / 40f, y / 40f);
            float field = GaussianField.Influence(p, new Vector2(0.4f, 0.5f), 0.16f) + GaussianField.Influence(p, new Vector2(0.6f, 0.5f), 0.16f);
            if (field >= 0.5f) low++;
            if (field >= 1.2f) high++;
        }
        Assert.That(high, Is.LessThan(low));
    }
    [Test]
    public void SmoothEdgeStaysBetweenZeroAndOne()
    {
        for (int i = -10; i < 40; i++)
        {
            float t = Mathf.InverseLerp(0.82f - 0.03f, 0.82f + 0.03f, i * 0.1f);
            Assert.That(Mathf.SmoothStep(0f, 1f, t), Is.InRange(0f, 1f));
        }
    }
    [Test]
    public void AnalyticGradientMatchesFiniteDifferenceAndPointsOutward()
    {
        Vector3 p = new Vector3(0.12f, 0.07f, -0.03f);
        Vector3 gradient = GaussianField.Gradient(p, Vector3.zero, 0.16f);
        float epsilon = 0.0001f;
        float dx = (GaussianField.Influence(p + Vector3.right * epsilon, Vector3.zero, 0.16f) - GaussianField.Influence(p - Vector3.right * epsilon, Vector3.zero, 0.16f)) / (epsilon * 2f);
        Assert.That(gradient.x, Is.EqualTo(dx).Within(0.002f));
        Vector3 normal = (-gradient).normalized;
        Assert.That(Vector3.Dot(normal, p.normalized), Is.GreaterThan(0.999f));
        Assert.That(float.IsNaN(normal.x) || float.IsInfinity(normal.x), Is.False);
        Assert.That(GaussianField.Gradient(Vector3.zero, Vector3.zero, 0.16f), Is.EqualTo(Vector3.zero));
    }
    [Test]
    public void CoarseSamplingUsesCellCenters()
    {
        Assert.That(GaussianField.SamplePoint(new Vector2(0.3f, 0.6f), 16), Is.EqualTo(new Vector2(4.5f / 16f, 9.5f / 16f)));
        Assert.That(GaussianField.SamplePoint(new Vector2(0.3f, 0.6f), 0), Is.EqualTo(new Vector2(0.3f, 0.6f)));
    }
}
