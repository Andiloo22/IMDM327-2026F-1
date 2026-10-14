using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace IMDM327.EDMBoids
{
    public class EDMBoidsController : MonoBehaviour
    {
        public FMMidiSynth synth;
        public Shader trailShader;
        [FormerlySerializedAs("count")]
        [Range(16, 480)] public int numberOfBoids = 360;
        public Vector3 center = Vector3.zero;
        public float fieldRadius = 5.8f;
        [FormerlySerializedAs("maxSpeed")]
        public float maxVelocity = 2.6f;
        public float groupSpread = 3.4f;
        public float groupRadius = 1.25f;
        public float circulation = 3.8f;
        public float G = 0.65f;
        public float closeDistance = 0.16f; // Squared distance.
        public float noteMass = 0.9f;
        public float neighborRadius = 1.1f;
        public float cohesion = 0.65f;
        public float alignment = 0.9f;
        public float separation = 3f;
        public float turbulence = 0.6f;
        public float notePull = 6f;
        public float notePush = 24f;
        public float attackSeconds = 0.16f;
        public float releaseSeconds = 0.6f;
        public float beatImpulse = 2.8f;
        public float burstSpeed = 6.8f;
        public float returnStrength = 2.4f;
        public float trailSeconds = 4.5f;
        public float glowIntensity = 2.2f;

        struct BodyProperty
        {
            public float mass;
            public Vector3 position, velocity, acceleration;
        }
        BodyProperty[] bp;
        Transform[] body;
        TrailRenderer[] trails;
        readonly Vector3[] groupCenters = new Vector3[4];
        readonly float[] groupEnergy = new float[4];
        readonly float[] groupAttack = new float[4];
        struct ForceSource
        {
            public Vector3 position;
            public float envelope, attack, age, push, pull;
        }
        readonly ForceSource[] sources = new ForceSource[4 * 128];
        readonly int[] activeSources = new int[4 * 128];
        int sourceCount;
        GameObject visuals;
        Material trailMaterial;

        float kickEnvelope;
        readonly Vector3[] centroids = new Vector3[4], directions = new Vector3[4];
        readonly float[] speeds = new float[4], spreads = new float[4];
        readonly int[] groupCounts = new int[4];
        void Start()
        {
            if (synth == null) synth = FindFirstObjectByType<FMMidiSynth>();
            numberOfBoids = Mathf.Clamp(numberOfBoids, 16, 480);
            visuals = new GameObject("EDM Light Bodies");
            visuals.transform.SetParent(transform, false);
            trailMaterial = new Material(trailShader != null ? trailShader : Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            trailMaterial.SetFloat("_Surface", 1f);
            trailMaterial.SetFloat("_Blend", 2f);
            trailMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            trailMaterial.SetFloat("_DstBlend", (float)BlendMode.One);
            trailMaterial.SetFloat("_ZWrite", 0f);
            trailMaterial.SetFloat("_Cull", 0f);
            trailMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            trailMaterial.SetColor("_BaseColor", new Color(glowIntensity, glowIntensity, glowIntensity, 1f));
            trailMaterial.renderQueue = 3000;
            bp = new BodyProperty[numberOfBoids];
            body = new Transform[numberOfBoids];
            trails = new TrailRenderer[numberOfBoids];
            System.Random random = new System.Random(327);
            UpdateGroupCenters(0f);
            // Initial position, mass, and velocity.
            for (int i = 0; i < numberOfBoids; i++)
            {
                GameObject boid = new GameObject("Boid_" + i);
                boid.transform.SetParent(visuals.transform, false);
                body[i] = boid.transform;
                int group = i % 4;
                float angle = (i / 4) * 2.399963f;
                float radius = groupRadius * (0.65f + (float)random.NextDouble() * 0.65f);
                bp[i].position = groupCenters[group] + new Vector3(Mathf.Sin(angle) * radius,
                    Mathf.Cos(angle) * radius * 0.8f, ((float)random.NextDouble() - 0.5f) * 0.7f);
                bp[i].mass = 0.01f + (float)random.NextDouble() * 0.04f;
                bp[i].velocity = new Vector3(Mathf.Cos(angle), -Mathf.Sin(angle), 0f) * maxVelocity * 0.5f;
                body[i].localPosition = bp[i].position;
                trails[i] = boid.AddComponent<TrailRenderer>();
                trails[i].sharedMaterial = trailMaterial;
                trails[i].time = trailSeconds;
                trails[i].minVertexDistance = 0.035f;
                trails[i].startWidth = 0.025f;
                trails[i].endWidth = 0.001f;
                trails[i].numCornerVertices = 2;
                trails[i].numCapVertices = 2;
                trails[i].shadowCastingMode = ShadowCastingMode.Off;
                trails[i].receiveShadows = false;
                SetColor(i, Palette(i) * glowIntensity);
            }
        }

        void FixedUpdate()
        {
            if (bp == null) return;
            float drums = synth != null ? Mathf.Clamp01(synth.GetEnergy(0) * 3f) : 0f;
            float bass = synth != null ? Mathf.Clamp01(synth.GetEnergy(1) * 4f) : 0f;
            float pad = synth != null ? Mathf.Clamp01(synth.GetEnergy(2) * 5f) : 0f;
            float lead = synth != null ? Mathf.Clamp01(synth.GetEnergy(3) * 4f) : 0f;
            float dt = Time.fixedDeltaTime;
            groupEnergy[0] = drums;
            groupEnergy[1] = bass;
            groupEnergy[2] = pad;
            groupEnergy[3] = lead;
            UpdateGroupCenters(Time.time);
            float kick = synth != null ? synth.ConsumeKickStrength() : 0f;
            if (kick > 0f)
            {
                kickEnvelope = kick * kick;
                for (int i = 0; i < bp.Length; i++)
                {
                    Vector3 outward = (bp[i].position - groupCenters[i % 4]).normalized;
                    bp[i].velocity += outward * beatImpulse * kickEnvelope * (0.8f + i % 5 * 0.08f);
                }
            }
            else kickEnvelope *= Mathf.Exp(-dt * 5f);
            UpdateForceSources(dt);
            // 00. Initialize acceleration
            for (int i = 0; i < bp.Length; i++)
            {
                bp[i].acceleration = Vector3.zero;
            }
            // 01. Gravity between bodies
            for (int i = 0; i < bp.Length; i++)
            {
                for (int j = i + 1; j < bp.Length; j++)
                {
                    Vector3 distance = bp[j].position - bp[i].position;
                    Vector3 gravity = CalculateGravity(distance, bp[i].mass, bp[j].mass);
                    if (distance.sqrMagnitude > closeDistance)
                    {
                        float local = Mathf.Clamp01(1f - distance.magnitude / (neighborRadius * 2.5f));
                        gravity *= cohesion * (0.35f + bass * 0.8f) * local * (i % 4 == j % 4 ? 1f : 0.035f);
                        bp[i].acceleration += gravity / bp[i].mass;
                        bp[j].acceleration -= gravity / bp[j].mass;
                    }
                    else
                    {
                        gravity *= separation * 8f * (1f + drums + kickEnvelope);
                        bp[i].acceleration -= gravity / bp[i].mass;
                        bp[j].acceleration += gravity / bp[j].mass;
                    }
                    if (i % 4 == j % 4 && distance.sqrMagnitude < neighborRadius * neighborRadius)
                    {
                        Vector3 heading = (bp[j].velocity - bp[i].velocity) * alignment * (0.02f + pad * 0.08f);
                        bp[i].acceleration += heading;
                        bp[j].acceleration -= heading;
                    }
                }
            }
            // Notes push first, then pull.
            for (int s = 0; s < sourceCount; s++)
            {
                int index = activeSources[s], role = index / 128;
                ForceSource source = sources[index];
                for (int i = 0; i < bp.Length; i++)
                {
                    float affinity = i % 4 == role ? 1f : 0.025f;
                    Vector3 distance = source.position - bp[i].position;
                    Vector3 gravity = G * bp[i].mass * noteMass / (distance.magnitude + 0.8f) * distance.normalized;
                    bp[i].acceleration += gravity / bp[i].mass * affinity * (source.pull * notePull - source.push * notePush);
                }
            }
            // 02. Update velocity and position
            for (int i = 0; i < bp.Length; i++)
            {
                int group = i % 4;
                Vector3 fromCenter = bp[i].position - center;
                Vector3 fromGroup = bp[i].position - groupCenters[group];
                float radiusScale = 1.15f;
                if (group == 0) radiusScale = 0.9f;
                if (group == 2) radiusScale = 1.45f;
                float restRadius = groupRadius * radiusScale
                    * (0.9f + groupEnergy[group] * 0.3f + kickEnvelope * 0.35f)
                    * (0.55f + i / 4 % 11 * 0.065f);
                bp[i].acceleration += fromGroup.normalized * (restRadius - fromGroup.magnitude)
                    * (3f + returnStrength * (1f - kickEnvelope));
                Vector3 tangent = new Vector3(-fromGroup.y, fromGroup.x, 0f).normalized;
                float direction = group % 2 == 0 ? -1f : 1f;
                float flow = 1f;
                if (group == 2) flow = 0.6f;
                if (group == 3) flow = 1.3f;
                flow *= 0.7f + i / 4 % 7 * 0.07f;
                bp[i].acceleration += tangent * direction * circulation * flow * (0.65f + groupEnergy[group]);
                float fold = Mathf.Sin(Mathf.Atan2(fromGroup.y, fromGroup.x) * 3f - Time.time * 0.8f + group);
                bp[i].acceleration += fromGroup.normalized * fold * groupEnergy[group] * 1.3f;
                float phase = Time.time * 0.7f + i * 0.13f;
                bp[i].acceleration += new Vector3(Mathf.Sin(phase), Mathf.Cos(phase * 0.8f),
                    Mathf.Sin(phase * 0.6f)) * turbulence * (0.12f + lead);
                if (fromCenter.magnitude > fieldRadius)
                    bp[i].acceleration -= fromCenter.normalized * (fromCenter.magnitude - fieldRadius) * 12f;
                if (Mathf.Abs(fromCenter.y) > fieldRadius * 0.57f)
                    bp[i].acceleration.y -= Mathf.Sign(fromCenter.y) * (Mathf.Abs(fromCenter.y) - fieldRadius * 0.57f) * 18f;
                bp[i].acceleration.z -= fromGroup.z * 2f + bp[i].velocity.z * 0.6f;
                bp[i].velocity += Vector3.ClampMagnitude(bp[i].acceleration, 30f) * dt;
                float steadySpeed = maxVelocity;
                if (group == 2) steadySpeed *= 0.72f;
                if (group == 3) steadySpeed *= 1.25f;
                float speedLimit = Mathf.Lerp(steadySpeed, Mathf.Max(steadySpeed, burstSpeed), Mathf.Clamp01(kickEnvelope + groupAttack[group]));
                float speed = bp[i].velocity.magnitude;
                if (speed > speedLimit)
                    bp[i].velocity *= Mathf.MoveTowards(speed, speedLimit, dt * 8f) / speed;
                bp[i].position += bp[i].velocity * dt;
                body[i].localPosition = bp[i].position;
                float intensity = glowIntensity * (0.65f + groupEnergy[group] * 0.5f + groupAttack[group] * 0.6f + kickEnvelope * 0.25f);
                SetColor(i, Palette(i) * intensity);
                float trailRatio = 0.4f;
                if (group == 0) trailRatio = 0.25f;
                if (group == 1) trailRatio = 0.5f;
                if (group == 2) trailRatio = 1f;
                trails[i].time = trailSeconds * trailRatio * (0.85f + groupEnergy[group] * 0.25f);
                trails[i].startWidth = (group == 2 ? 0.009f : 0.012f) + groupEnergy[group] * 0.005f + groupAttack[group] * 0.004f;
            }
            UpdateMetrics();
        }

        void UpdateForceSources(float dt)
        {
            sourceCount = 0;
            for (int role = 0; role < 4; role++) groupAttack[role] = 0f;
            for (int role = 0; role < 4; role++)
            {
                for (int note = 0; note < 128; note++)
                {
                    int index = role * 128 + note;
                    ForceSource source = sources[index];
                    float attack = synth != null ? synth.ConsumeNoteAttack(role, note) : 0f;
                    float held = synth != null ? synth.HeldNoteVelocity(role, note) : 0f;
                    if (role == 0 && (note == 35 || note == 36)) continue;
                    if (attack > 0f)
                    {
                        source.attack = attack * attack;
                        source.envelope = Mathf.Max(source.envelope, source.attack);
                        source.age = 0f;
                    }
                    else source.age += dt;
                    float heldTarget = held * held;
                    source.position = NotePosition(role, note);
                    float smooth = 1f - Mathf.Exp(-dt / Mathf.Max(0.02f, releaseSeconds));
                    source.envelope = Mathf.Lerp(source.envelope, heldTarget, smooth);
                    float onset = Mathf.Exp(-source.age / Mathf.Max(0.02f, attackSeconds));
                    float rolePush = 1f;
                    if (role == 0) rolePush = 0.4f;
                    if (role == 2) rolePush = 0.35f;
                    source.push = source.attack * onset * rolePush;
                    source.pull = role == 0 ? 0f : source.envelope * (1f - onset);
                    groupAttack[role] = Mathf.Max(groupAttack[role], source.push);
                    sources[index] = source;
                    if (source.pull + source.push > 0.015f) activeSources[sourceCount++] = index;
                }
            }
        }

        Vector3 NotePosition(int role, int note)
        {
            float angle = note % 12 / 12f * Mathf.PI * 2f + role * 0.7f;
            float register = Mathf.InverseLerp(36f, 96f, note);
            float radius = groupRadius * Mathf.Lerp(0.5f, 1.35f, register);
            return groupCenters[role] + new Vector3(Mathf.Sin(angle) * radius, Mathf.Cos(angle) * radius,
                (register - 0.5f) * 0.8f);
        }

        void UpdateGroupCenters(float time)
        {
            for (int group = 0; group < 4; group++)
            {
                float phase = group * Mathf.PI * 0.5f + time * (group % 2 == 0 ? 0.13f : -0.11f);
                groupCenters[group] = center + new Vector3(Mathf.Cos(phase) * groupSpread,
                    Mathf.Sin(phase) * groupSpread * 0.46f + Mathf.Sin(time * 0.31f + group * 1.7f) * 0.45f,
                    Mathf.Sin(time * 0.17f + group * 2f) * 0.55f);
            }
        }

        void OnDrawGizmosSelected()
        {
            if (Camera.current == null || Camera.current.cameraType != CameraType.SceneView) return;
            for (int s = 0; s < sourceCount; s++)
            {
                ForceSource source = sources[activeSources[s]];
                Gizmos.color = source.push > source.pull ? new Color(1f, 0.3f, 0.25f) : Color.cyan;
                Gizmos.DrawWireSphere(transform.TransformPoint(source.position), 0.1f + source.envelope * 0.12f);
            }
        }

        Vector3 CalculateGravity(Vector3 distance, float m1, float m2)
        {
            return G * m1 * m2 / (distance.magnitude + 0.1f) * distance.normalized;
        }

        Color Palette(int i)
        {
            int group = i % 4;
            Color color = new Color(0.5f, 1f, 0.85f);
            if (group == 0) color = new Color(1f, 0.65f, 0.3f);
            if (group == 1) color = new Color(0.05f, 0.65f, 1f);
            if (group == 2) color = new Color(0.55f, 0.28f, 1f);
            return Color.Lerp(color, new Color(0.85f, 0.92f, 1f), (Mathf.Sin(i * 0.7f) * 0.5f + 0.5f) * 0.22f);
        }

        void SetColor(int i, Color value)
        {
            Color trail = value / Mathf.Max(glowIntensity, 0.01f);
            trail.a = 0.55f;
            trails[i].startColor = trail;
            trails[i].endColor = new Color(trail.r, trail.g, trail.b, 0f);
        }

        void UpdateMetrics()
        {
            for (int g = 0; g < 4; g++)
            {
                centroids[g] = directions[g] = Vector3.zero;
                speeds[g] = spreads[g] = 0f;
                groupCounts[g] = 0;
            }
            for (int i = 0; i < bp.Length; i++)
            {
                int g = i % 4;
                centroids[g] += bp[i].position;
                directions[g] += bp[i].velocity.normalized;
                speeds[g] += bp[i].velocity.magnitude;
                groupCounts[g]++;
            }
            for (int g = 0; g < 4; g++) centroids[g] /= groupCounts[g];
            for (int i = 0; i < bp.Length; i++)
                spreads[i % 4] += Vector3.Distance(bp[i].position, centroids[i % 4]);
            if (synth == null) return;
            for (int g = 0; g < 4; g++)
            {
                float speed = Mathf.InverseLerp(maxVelocity * 0.2f, maxVelocity * 1.4f, speeds[g] / groupCounts[g]);
                float spread = Mathf.InverseLerp(groupRadius * 0.5f, groupRadius * 2.4f, spreads[g] / groupCounts[g]);
                float disorder = 1f - directions[g].magnitude / groupCounts[g];
                float pan = Mathf.Clamp((centroids[g].x - center.x) / Mathf.Max(fieldRadius, 0.01f), -0.8f, 0.8f);
                synth.SetBoidMetrics(g, spread, speed, disorder, pan);
            }
        }

        void OnEnable()
        {
            if (visuals != null) visuals.SetActive(true);
        }

        void OnDisable()
        {
            if (visuals != null) visuals.SetActive(false);
        }

        void OnDestroy()
        {
            if (visuals != null) Destroy(visuals);
            if (trailMaterial != null) Destroy(trailMaterial);
        }
    }
}
