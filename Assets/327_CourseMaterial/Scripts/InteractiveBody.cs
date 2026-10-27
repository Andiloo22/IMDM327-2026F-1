
using System.Collections.Generic;
using UnityEngine;

public class InteractiveBody : MonoBehaviour
{
    private float[] timer = new float[2];
    private float waitTime = 2f;
    private int[] spawnIndex = new int[2];

    public float G = 1f;
    private GameObject[] body;
    private BodyProperty[] bp;
    private const int numberOfSphere = 120;

    public float fastforwardConst = 1f;
    public float radius = 3.5f;

    private TrailRenderer[] trailRenderer;
    private Transform[] ghostVisuals;
    private MediaPipeBodyTracker mp;
    private Camera cam;

    public float maxVelocity = 6f;
    public float closeDistance = 0.16f;
    public Shader trailShader;

    [Header("Hand Interaction")]
    public float interactiveMass = 50f;
    public float fistDeleteRadius = 0.75f;

    [Header("Ghost Size")]
    public float minGhostSize = 0.65f;
    public float maxGhostSize = 1.25f;

    [Header("Ghost Colors")]
    public Color darkGreen = new Color(0.02f, 0.3f, 0.08f);
    public Color brightGreen = new Color(0.2f, 1f, 0.35f);
    public Color ghostFaceColor = new Color(0.025f, 0.04f, 0.035f);

    [Header("Trail Settings")]
    [Range(0.01f, 0.5f)]
    public float trailWidthRatio = 0.12f;
    [Range(0.001f, 0.1f)]
    public float trailEndWidthRatio = 0.015f;
    public float trailDuration = 3f;

    [Header("Ghost Explosion")]
    public int explosionParticleCount = 45;
    public float explosionDuration = 2.5f;
    public float explosionMinSpeed = 0.6f;
    public float explosionMaxSpeed = 2.5f;
    public float explosionMinSize = 0.035f;
    public float explosionMaxSize = 0.12f;

    private const float GhostBaseWidth = 0.9f;

    private Vector3[] leftLandmarkBuffer = new Vector3[21];
    private Vector3[] rightLandmarkBuffer = new Vector3[21];

    private Mesh sharedGhostMesh;
    private Material sharedFaceMaterial;

    private struct BodyProperty
    {
        public float mass;
        public Vector3 velocity;
        public Vector3 acceleration;
    }

    private void Start()
    {
        mp = FindObjectOfType<MediaPipeBodyTracker>();
        cam = Camera.main;

        bp = new BodyProperty[numberOfSphere];
        body = new GameObject[numberOfSphere];
        trailRenderer = new TrailRenderer[numberOfSphere];
        ghostVisuals = new Transform[numberOfSphere];

        spawnIndex[0] = 0;
        spawnIndex[1] = numberOfSphere / 2;

        sharedGhostMesh = CreateGhostMesh();
        sharedFaceMaterial = CreateColoredMaterial(ghostFaceColor);

        if (mp == null)
        {
            Debug.LogWarning(
                "InteractiveBody could not find MediaPipeBodyTracker."
            );
        }

        if (cam == null)
        {
            Debug.LogWarning(
                "InteractiveBody could not find the Main Camera."
            );
        }
    }

    private void FixedUpdate()
    {
        if (mp == null)
            mp = FindObjectOfType<MediaPipeBodyTracker>();

        if (cam == null)
            cam = Camera.main;

        // Reset acceleration for active ghosts.
        for (int i = 0; i < numberOfSphere; i++)
        {
            if (body[i] != null)
                bp[i].acceleration = Vector3.zero;
        }

        // Spawn ghosts from the center of each palm.
        HandleSpawning();

        // Delete ghosts near a closed fist.
        HandleFistDeletion();

        // Gravity between ghosts.
        ApplyBodyGravity();

        // Add occasional small random forces.
        ApplyRandomMovement();

        // Pull ghosts toward an open, spread hand.
        ApplyPalmAttraction();

        // Update position, direction, speed, and trail width.
        UpdateGhostMovement();
    }

    private void HandleSpawning()
    {
        if (mp == null || cam == null)
            return;

        for (int hand = 0; hand < 2; hand++)
        {
            bool tracked = hand == 0
                ? mp.LeftHandTracked
                : mp.RightHandTracked;

            bool open = hand == 0
                ? mp.LeftHandOpen
                : mp.RightHandOpen;

            if (!tracked || !open)
            {
                // Do not accumulate spawn timer while the hand is closed.
                continue;
            }

            if (timer[hand] > 0f)
                timer[hand] -= Time.fixedDeltaTime;

            if (timer[hand] > 0f)
                continue;

            int index = spawnIndex[hand];

            int limit = hand == 0
                ? numberOfSphere / 2
                : numberOfSphere;

            if (index < limit)
            {
                Vector3 palmPosition;

                if (TryGetPalmWorldPosition(hand, out palmPosition))
                {
                    SpawnBody(index, palmPosition);
                    spawnIndex[hand]++;
                }
            }

            timer[hand] = waitTime;
        }
    }

    private void HandleFistDeletion()
    {
        if (mp == null || cam == null)
            return;

        for (int hand = 0; hand < 2; hand++)
        {
            bool tracked = hand == 0
                ? mp.LeftHandTracked
                : mp.RightHandTracked;

            bool fist = hand == 0
                ? mp.LeftHandFist
                : mp.RightHandFist;

            if (!tracked || !fist)
                continue;

            Vector3 palmPosition;

            if (!TryGetPalmWorldPosition(hand, out palmPosition))
                continue;

            for (int i = 0; i < numberOfSphere; i++)
            {
                if (body[i] == null)
                    continue;

                float distance = Vector3.Distance(
                    body[i].transform.position,
                    palmPosition
                );

                if (distance <= fistDeleteRadius)
                    DeleteBody(i);
            }
        }
    }

    private void ApplyBodyGravity()
    {
        for (int i = 0; i < numberOfSphere; i++)
        {
            if (body[i] == null)
                continue;

            for (int j = i + 1; j < numberOfSphere; j++)
            {
                if (body[j] == null)
                    continue;

                Vector3 distance =
                    body[j].transform.position -
                    body[i].transform.position;

                Vector3 gravity = CalculateGravity(
                    distance,
                    bp[i].mass,
                    bp[j].mass
                );

                if (distance.sqrMagnitude >
                    closeDistance * closeDistance)
                {
                    bp[i].acceleration += gravity / bp[i].mass;
                    bp[j].acceleration -= gravity / bp[j].mass;
                }
                else
                {
                    bp[i].acceleration -= 3f * gravity / bp[i].mass;
                    bp[j].acceleration += 3f * gravity / bp[j].mass;
                }
            }
        }
    }

    private void ApplyRandomMovement()
    {
        for (int i = 0; i < numberOfSphere; i++)
        {
            if (body[i] == null)
                continue;

            if (Random.Range(0f, 1.05f) > 1f)
            {
                bp[i].acceleration += new Vector3(
                    Random.Range(-1f, 1f),
                    Random.Range(-1f, 1f),
                    Random.Range(-1f, 1f)
                );
            }
        }
    }

    private void ApplyPalmAttraction()
    {
        if (mp == null || cam == null)
            return;

        for (int hand = 0; hand < 2; hand++)
        {
            bool tracked = hand == 0
                ? mp.LeftHandTracked
                : mp.RightHandTracked;

            bool openApart = hand == 0
                ? mp.LeftHandOpenApart
                : mp.RightHandOpenApart;

            if (!tracked || !openApart)
                continue;

            Vector3 palmPosition;

            if (!TryGetPalmWorldPosition(hand, out palmPosition))
                continue;

            for (int i = 0; i < numberOfSphere; i++)
            {
                if (body[i] == null)
                    continue;

                Vector3 distance =
                    palmPosition - body[i].transform.position;

                bp[i].acceleration += CalculateGravity(
                    distance,
                    bp[i].mass,
                    interactiveMass
                ) / bp[i].mass;
            }
        }
    }

    // Gets the center of the palm from the actual landmark array.
    // This deliberately does not use LeftHandPosition/RightHandPosition,
    // because older tracker versions stored the wrist at landmark 0.
    private bool TryGetPalmWorldPosition(int hand, out Vector3 worldPosition)
    {
        worldPosition = Vector3.zero;

        if (mp == null || cam == null)
            return false;

        if (hand == 0)
            mp.GetLeftHandLandmarks(leftLandmarkBuffer);
        else
            mp.GetRightHandLandmarks(rightLandmarkBuffer);

        Vector3[] landmarks = hand == 0
            ? leftLandmarkBuffer
            : rightLandmarkBuffer;

        // MediaPipe hand landmarks:
        // 0 = wrist, 5 = index MCP, 9 = middle MCP,
        // 13 = ring MCP, 17 = pinky MCP.
        Vector3 palmCenter = (
            landmarks[0] +
            landmarks[5] +
            landmarks[9] +
            landmarks[13] +
            landmarks[17]
        ) / 5f;

        // Convert the normalized camera position to the world,
        // retaining the original project's mirrored-camera mapping.
        worldPosition = cam.ViewportToWorldPoint(
            new Vector3(
                1f - palmCenter.x,
                1f - palmCenter.y,
                -cam.transform.position.z
            )
        );

        return true;
    }

    private void SpawnBody(int index, Vector3 spawnPosition)
    {
        // The root stays at scale one, so the trail uses world units.
        GameObject ghostRoot = new GameObject("CartoonGhost");
        ghostRoot.transform.position = spawnPosition;

        body[index] = ghostRoot;

        float size = Random.Range(minGhostSize, maxGhostSize);

        Color ghostColor = Random.ColorHSV(
            0.25f, 0.40f,
            0.65f, 1f,
            0.75f, 1f
        );

        // All visual elements are scaled together beneath this transform.
        GameObject visualObject = new GameObject("GhostVisual");
        visualObject.transform.SetParent(ghostRoot.transform, false);
        visualObject.transform.localScale = Vector3.one * size;
        ghostVisuals[index] = visualObject.transform;

        // Body mesh: a rounded sheet with exactly three bottom scallops.
        GameObject meshObject = new GameObject("GhostSheet");
        meshObject.transform.SetParent(visualObject.transform, false);

        MeshFilter meshFilter = meshObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = meshObject.AddComponent<MeshRenderer>();

        meshFilter.sharedMesh = sharedGhostMesh;
        meshRenderer.material = CreateColoredMaterial(ghostColor);

        // The visible front surface is on the negative-Z side,
        // toward the standard Unity camera positioned at negative Z.
        CreateFaceFeature(
            visualObject.transform,
            "LeftEye",
            new Vector3(-0.16f, 0.13f, -0.115f),
            new Vector3(0.075f, 0.105f, 0.035f)
        );

        CreateFaceFeature(
            visualObject.transform,
            "RightEye",
            new Vector3(0.16f, 0.13f, -0.115f),
            new Vector3(0.075f, 0.105f, 0.035f)
        );

        CreateFaceFeature(
            visualObject.transform,
            "Mouth",
            new Vector3(0f, -0.06f, -0.12f),
            new Vector3(0.045f, 0.065f, 0.03f)
        );

        bp[index].velocity = new Vector3(
            Mathf.Cos(index * 2f * Mathf.PI / numberOfSphere),
            -Mathf.Sin(index * 2f * Mathf.PI / numberOfSphere),
            0f
        ) * maxVelocity * 0.3f;

        bp[index].mass = Random.Range(0.01f, 0.05f);

        // TrailRenderer lives on the unscaled root. Its width is based
        // on the actual width of the ghost mesh times the visual scale.
        TrailRenderer trail = ghostRoot.AddComponent<TrailRenderer>();
        trailRenderer[index] = trail;

        trail.time = trailDuration;
        trail.minVertexDistance = 0.02f;
        trail.startWidth = GetGhostWorldWidth(index) * trailWidthRatio;
        trail.endWidth = GetGhostWorldWidth(index) * trailEndWidthRatio;

        Shader actualTrailShader = trailShader != null
            ? trailShader
            : Shader.Find("Sprites/Default");

        if (actualTrailShader != null)
        {
            trail.material = new Material(actualTrailShader);
        }

        Color brighter = Color.Lerp(ghostColor, Color.white, 0.4f);

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(brighter, 0f),
                new GradientColorKey(ghostColor, 0.4f),
                new GradientColorKey(darkGreen, 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.9f, 0f),
                new GradientAlphaKey(0.65f, 0.45f),
                new GradientAlphaKey(0f, 1f)
            }
        );

        trail.colorGradient = gradient;
    }

    // Width of the ghost mesh in world units:
    // mesh width is 0.9 units, multiplied by the visual's scale.
    private float GetGhostWorldWidth(int index)
    {
        if (ghostVisuals == null || ghostVisuals[index] == null)
            return 0f;

        return GhostBaseWidth *
            Mathf.Abs(ghostVisuals[index].localScale.x);
    }

    private Mesh CreateGhostMesh()
    {
        // Create a 2D sheet-like outline and give it thickness.
        // Front = negative Z; back = positive Z.
        List<Vector2> outline = new List<Vector2>();

        const int topSegments = 24;
        const int bottomSegments = 48;

        const float halfWidth = 0.45f;
        const float archCenterY = 0.08f;
        const float archHeight = 0.55f;
        const float hemY = -0.33f;
        const float scallopDepth = 0.13f;
        const float frontZ = -0.08f;
        const float backZ = 0.04f;

        // Rounded dome from the left side across to the right side.
        for (int i = 0; i <= topSegments; i++)
        {
            float angle = Mathf.PI -
                Mathf.PI * i / topSegments;

            float x = halfWidth * Mathf.Cos(angle);
            float y = archCenterY +
                archHeight * Mathf.Sin(angle);

            outline.Add(new Vector2(x, y));
        }

        // Right edge of the sheet.
        outline.Add(new Vector2(halfWidth, hemY));

        // Bottom hem with three scalloped cloth points.
        for (int i = 1; i <= bottomSegments; i++)
        {
            float t = (float)i / bottomSegments;

            float x = Mathf.Lerp(halfWidth, -halfWidth, t);
            float scallop = Mathf.Sin(3f * Mathf.PI * t);
            float y = hemY -
                scallopDepth * scallop * scallop;

            outline.Add(new Vector2(x, y));
        }

        int count = outline.Count;
        int frontCenter = 0;
        int frontStart = 1;
        int backCenter = count + 1;
        int backStart = count + 2;

        Vector3[] vertices = new Vector3[2 * (count + 1)];
        int[] triangles = new int[count * 12];

        // Center points for both sides.
        vertices[frontCenter] = new Vector3(0f, 0f, frontZ);
        vertices[backCenter] = new Vector3(0f, 0f, backZ);

        for (int i = 0; i < count; i++)
        {
            vertices[frontStart + i] =
                new Vector3(outline[i].x, outline[i].y, frontZ);

            vertices[backStart + i] =
                new Vector3(outline[i].x, outline[i].y, backZ);
        }

        int tri = 0;

        for (int i = 0; i < count; i++)
        {
            int next = (i + 1) % count;

            // Front face: clockwise outline gives a -Z-facing surface.
            triangles[tri++] = frontCenter;
            triangles[tri++] = frontStart + i;
            triangles[tri++] = frontStart + next;

            // Back face, with reverse winding.
            triangles[tri++] = backCenter;
            triangles[tri++] = backStart + next;
            triangles[tri++] = backStart + i;

            // Side walls connecting the front and back outlines.
            triangles[tri++] = frontStart + i;
            triangles[tri++] = backStart + i;
            triangles[tri++] = frontStart + next;

            triangles[tri++] = frontStart + next;
            triangles[tri++] = backStart + i;
            triangles[tri++] = backStart + next;
        }

        Mesh mesh = new Mesh();
        mesh.name = "ThreeScallopCartoonGhost";
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    private Material CreateColoredMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");

        if (shader == null)
            shader = Shader.Find("Standard");

        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        Material material = new Material(shader);
        material.color = color;

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);

        // Make the ghost visible even under weak scene lighting.
        if (material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 0.25f);
        }

        // Keep both sides drawable for the thin sheet mesh.
        if (material.HasProperty("_Cull"))
            material.SetFloat("_Cull", 0f);

        return material;
    }

    private void CreateFaceFeature(
        Transform parent,
        string featureName,
        Vector3 localPosition,
        Vector3 localScale
    )
    {
        GameObject feature =
            GameObject.CreatePrimitive(PrimitiveType.Sphere);

        feature.name = featureName;
        feature.transform.SetParent(parent, false);
        feature.transform.localPosition = localPosition;
        feature.transform.localScale = localScale;

        Collider featureCollider = feature.GetComponent<Collider>();

        if (featureCollider != null)
            Destroy(featureCollider);

        Renderer featureRenderer = feature.GetComponent<Renderer>();
        featureRenderer.sharedMaterial = sharedFaceMaterial;
    }

    private void UpdateGhostMovement()
    {
        for (int i = 0; i < numberOfSphere; i++)
        {
            if (body[i] == null)
                continue;

            Vector3 position = body[i].transform.position;

            // Keep ghosts inside the simulation radius.
            if (position.magnitude > radius)
            {
                bp[i].acceleration -=
                    position.normalized *
                    (position.magnitude - radius) * 6f;
            }

            // Keep movement mostly in the camera's XY plane.
            bp[i].acceleration.z -=
                position.z * 3f + bp[i].velocity.z;

            bp[i].acceleration = Vector3.ClampMagnitude(
                bp[i].acceleration,
                20f
            );

            bp[i].velocity +=
                bp[i].acceleration *
                Time.fixedDeltaTime *
                fastforwardConst;

            bp[i].velocity = Vector3.ClampMagnitude(
                bp[i].velocity,
                maxVelocity
            );

            body[i].transform.position +=
                bp[i].velocity *
                Time.fixedDeltaTime *
                fastforwardConst;

            // Rotate the whole ghost in the screen plane so its
            // upper/dome end points along its actual travel direction.
            Vector2 planarVelocity = new Vector2(
                bp[i].velocity.x,
                bp[i].velocity.y
            );

            if (planarVelocity.sqrMagnitude > 0.0001f)
            {
                float angle = Mathf.Atan2(
                    planarVelocity.y,
                    planarVelocity.x
                ) * Mathf.Rad2Deg - 90f;

                body[i].transform.rotation =
                    Quaternion.Euler(0f, 0f, angle);
            }

            // Update the trail width from the ghost visual's real scale.
            if (trailRenderer[i] != null && ghostVisuals[i] != null)
            {
                float worldWidth = GetGhostWorldWidth(i);

                trailRenderer[i].startWidth =
                    worldWidth * trailWidthRatio;

                trailRenderer[i].endWidth =
                    worldWidth * trailEndWidthRatio;
            }
        }
    }

    private void DeleteBody(int index)
    {
        if (body[index] == null)
            return;

        GameObject deletedGhost = body[index];
        Vector3 position = deletedGhost.transform.position;

        Renderer bodyRenderer =
            deletedGhost.GetComponentInChildren<MeshRenderer>();

        Color explosionColor = brightGreen;

        if (bodyRenderer != null)
            explosionColor = bodyRenderer.material.color;

        CreateGhostlyExplosion(position, explosionColor);

        // Stop the trail from adding points but allow its existing
        // points to fade out. Do not disable the TrailRenderer.
        TrailRenderer trail = trailRenderer[index];

        if (trail != null)
        {
            trail.emitting = false;
            trail.autodestruct = false;
        }

        // Hide only the visual subtree so the trail remains visible.
        if (ghostVisuals[index] != null)
        {
            Renderer[] visualRenderers =
                ghostVisuals[index].GetComponentsInChildren<Renderer>(true);

            foreach (Renderer visualRenderer in visualRenderers)
                visualRenderer.enabled = false;
        }

        body[index] = null;
        ghostVisuals[index] = null;
        trailRenderer[index] = null;

        Destroy(deletedGhost, trailDuration + 0.25f);
    }

    private void CreateGhostlyExplosion(Vector3 position, Color baseColor)
    {
        GameObject explosion = new GameObject("GhostlyGreenExplosion");
        explosion.transform.position = position;

        ParticleSystem particles =
            explosion.AddComponent<ParticleSystem>();

        // Stop the new particle system before changing its duration.
        particles.Stop(
            true,
            ParticleSystemStopBehavior.StopEmittingAndClear
        );

        ParticleSystem.MainModule main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = explosionDuration;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(
            0.8f,
            explosionDuration
        );
        main.startSpeed = new ParticleSystem.MinMaxCurve(
            explosionMinSpeed,
            explosionMaxSpeed
        );
        main.startSize = new ParticleSystem.MinMaxCurve(
            explosionMinSize,
            explosionMaxSize
        );
        main.startColor = Color.Lerp(baseColor, brightGreen, 0.6f);
        main.maxParticles = explosionParticleCount + 20;
        main.gravityModifier = -0.15f;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.08f;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime =
            particles.colorOverLifetime;

        colorOverLifetime.enabled = true;

        Gradient fadeGradient = new Gradient();

        fadeGradient.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(
                    Color.Lerp(baseColor, brightGreen, 0.7f), 0.2f),
                new GradientColorKey(darkGreen, 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.85f, 0.2f),
                new GradientAlphaKey(0.35f, 0.7f),
                new GradientAlphaKey(0f, 1f)
            }
        );

        colorOverLifetime.color =
            new ParticleSystem.MinMaxGradient(fadeGradient);

        ParticleSystem.VelocityOverLifetimeModule velocity =
            particles.velocityOverLifetime;

        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.15f, 0.8f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);

        ParticleSystem.NoiseModule noise = particles.noise;
        noise.enabled = true;
        noise.strength = 0.7f;
        noise.frequency = 0.6f;
        noise.scrollSpeed = 0.3f;
        noise.damping = true;

        ParticleSystemRenderer particleRenderer =
            explosion.GetComponent<ParticleSystemRenderer>();

        Shader particleShader =
            Shader.Find("Universal Render Pipeline/Particles/Unlit");

        if (particleShader == null)
            particleShader = Shader.Find("Particles/Standard Unlit");

        if (particleShader == null)
            particleShader = Shader.Find("Sprites/Default");

        if (particleShader != null)
            particleRenderer.material = new Material(particleShader);

        particles.Emit(explosionParticleCount);
        particles.Play();

        Destroy(explosion, explosionDuration + 1f);
    }

    private Vector3 CalculateGravity(
        Vector3 distanceVector,
        float mass1,
        float mass2
    )
    {
        const float epsilon = 0.1f;

        return G * mass1 * mass2 /
            (distanceVector.magnitude + epsilon) *
            distanceVector.normalized;
    }

    private void OnDestroy()
    {
        if (sharedGhostMesh != null)
            Destroy(sharedGhostMesh);

        if (sharedFaceMaterial != null)
            Destroy(sharedFaceMaterial);
    }
}
