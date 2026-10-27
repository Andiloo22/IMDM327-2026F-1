
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class InteractiveBody : MonoBehaviour
{
    [Header("Body Appearance")]
    public Material bodyMaterial;

    [Header("Gesture Audio")]
    public AudioSource gestureAudioSource;

    // Keep the single clips as fallbacks for compatibility with existing Inspector settings.
    public AudioClip openGestureClip;
    public AudioClip fistGestureClip;
    public AudioClip openApartGestureClip;
    public AudioClip deathClip;

    // Assign multiple clips to these arrays to choose a random clip each time.
    public AudioClip[] openGestureClips;
    public AudioClip[] fistGestureClips;
    public AudioClip[] openApartGestureClips;
    public AudioClip[] deathClips;

    [Header("Explosion Audio Control")]
    public float deathClipVolume = 0.02f;
    public float deathSoundCooldown = 0.5f;
    [Min(0f)]
    public float explosionGestureAudioMuteTime = 0.3f;

    private float nextDeathSoundTime;
    private float suppressGestureAudioUntil;

    private bool previousLeftOpen;
    private bool previousRightOpen;
    private bool previousLeftFist;
    private bool previousRightFist;
    private bool previousLeftOpenApart;
    private bool previousRightOpenApart;

    [Header("Gesture Audio Stability")]
    public float gestureConfirmTime = 0.12f;
    public float gestureAudioCooldown = 0.3f;
    public float handLostGracePeriod = 0.2f;

    private float leftOpenTimer, rightOpenTimer;
    private float leftFistTimer, rightFistTimer;
    private float leftOpenApartTimer, rightOpenApartTimer;

    private float nextGestureAudioTime;
    private float leftLastTrackedTime, rightLastTrackedTime;

    private const int NumberOfGhosts = 120;
    private const int GhostsPerHand = NumberOfGhosts / 2;

    [Header("Gravity")]
    public float G = 1f;
    public float fastforwardConst = 1f;
    public float radius = 3.5f;
    public float maxVelocity = 6f;
    public float closeDistance = 0.16f;
    public float interactiveMass = 50f;

    [Header("Hand Actions")]
    public float actionDuration = 1f;
    public float spawnInterval = 0.2f;
    public float releaseDebounce = 0.15f;
    public float fistDeleteRadius = 0.75f;

    [Header("Sphere Size")]
    public float minGhostSize = 0.65f;
    public float maxGhostSize = 1.25f;

    [Header("Sphere Colors")]
    public Color darkGreen = new Color(0.02f, 0.3f, 0.08f);
    public Color brightGreen = new Color(0.2f, 1f, 0.35f);
    public Color ghostFaceColor = new Color(0.025f, 0.04f, 0.035f);

    [Header("Trails")]
    public float trailWidthRatio = 0.12f;
    public float trailEndWidthRatio = 0.015f;
    public float trailDuration = 3f;
    public Shader trailShader;

    [Header("Ghostly Explosion")]
    public int explosionParticleCount = 45;
    public float explosionDuration = 2.5f;
    public float explosionMinSpeed = 0.6f;
    public float explosionMaxSpeed = 2.5f;
    public float explosionMinSize = 0.035f;
    public float explosionMaxSize = 0.12f;

    private struct BodyProperty
    {
        public float mass;
        public Vector3 velocity;
        public Vector3 acceleration;
    }

    private GameObject[] body;
    private Transform[] ghostVisuals;
    private TrailRenderer[] trailRenderer;
    private BodyProperty[] bp;

    // Circular slot search within each hand's 60-slot section.
    private int[] nextSpawnOffset = new int[2];

    private bool[] openArmed = new bool[2];
    private bool[] fistArmed = new bool[2];

    private bool[] openActionActive = new bool[2];
    private bool[] fistActionActive = new bool[2];

    private float[] openActionRemaining = new float[2];
    private float[] fistActionRemaining = new float[2];

    private float[] openReleaseTimer = new float[2];
    private float[] fistReleaseTimer = new float[2];
    private float[] spawnCooldown = new float[2];

    private MediaPipeBodyTracker mp;
    private Camera cam;

    private readonly Vector3[] leftLandmarkBuffer = new Vector3[21];
    private readonly Vector3[] rightLandmarkBuffer = new Vector3[21];

    private Material sharedFaceMaterial;

    private void Start()
    {
        mp = FindObjectOfType<MediaPipeBodyTracker>();
        cam = Camera.main;

        body = new GameObject[NumberOfGhosts];
        ghostVisuals = new Transform[NumberOfGhosts];
        trailRenderer = new TrailRenderer[NumberOfGhosts];
        bp = new BodyProperty[NumberOfGhosts];

        for (int hand = 0; hand < 2; hand++)
        {
            openArmed[hand] = true;
            fistArmed[hand] = true;
        }

        sharedFaceMaterial = CreateColoredMaterial(ghostFaceColor);

        if (mp == null)
            Debug.LogWarning("InteractiveBody could not find MediaPipeBodyTracker.");

        if (cam == null)
            Debug.LogWarning("InteractiveBody could not find a Main Camera.");
    }

    private void FixedUpdate()
    {
        if (mp == null)
            mp = FindObjectOfType<MediaPipeBodyTracker>();

        if (cam == null)
            cam = Camera.main;

        if (mp == null || cam == null)
            return;

        UpdateActionStates();

        mp.SetInteractionActionActive(
            openActionActive[0] || fistActionActive[0],
            openActionActive[1] || fistActionActive[1]
        );

        for (int i = 0; i < NumberOfGhosts; i++)
        {
            if (body[i] != null)
                bp[i].acceleration = Vector3.zero;
        }

        SpawnDuringOpenWindow();
        DeleteDuringFistWindow();

        // Check gesture sounds after explosion events so new gesture sounds are
        // suppressed on the same frame an explosion happens.
        CheckGestureAudio();

        ApplyBodyGravity();
        ApplyRandomMovement();
        ApplyPalmAttraction();
        UpdateGhostMovement();
    }

    // Debounces gesture releases so small detection flickers do not
    // repeatedly retrigger an action or permanently block recasting.
    private void UpdateActionStates()
    {
        float dt = Time.fixedDeltaTime;

        for (int hand = 0; hand < 2; hand++)
        {
            bool tracked = hand == 0
                ? mp.LeftHandTracked
                : mp.RightHandTracked;

            bool normalOpen = tracked && (
                hand == 0
                    ? mp.LeftHandOpen && !mp.LeftHandOpenApart
                    : mp.RightHandOpen && !mp.RightHandOpenApart
            );

            bool fist = tracked && (
                hand == 0 ? mp.LeftHandFist : mp.RightHandFist
            );

            // OPEN-HAND ACTION
            if (normalOpen)
            {
                openReleaseTimer[hand] = 0f;

                if (openArmed[hand])
                {
                    openArmed[hand] = false;
                    openActionActive[hand] = true;
                    openActionRemaining[hand] = actionDuration;
                    spawnCooldown[hand] = 0f;
                }
            }
            else
            {
                openReleaseTimer[hand] += dt;

                // Only rearm after a continuous, stable release.
                if (openReleaseTimer[hand] >= releaseDebounce)
                {
                    openArmed[hand] = true;
                    openActionActive[hand] = false;
                    openActionRemaining[hand] = 0f;
                    spawnCooldown[hand] = 0f;
                }
            }

            if (openActionActive[hand])
            {
                openActionRemaining[hand] -= dt;

                if (openActionRemaining[hand] <= 0f)
                {
                    openActionRemaining[hand] = 0f;
                    openActionActive[hand] = false;
                }
            }

            // FIST DESTRUCTION ACTION
            if (fist)
            {
                fistReleaseTimer[hand] = 0f;

                if (fistArmed[hand])
                {
                    fistArmed[hand] = false;
                    fistActionActive[hand] = true;
                    fistActionRemaining[hand] = actionDuration;
                }
            }
            else
            {
                fistReleaseTimer[hand] += dt;

                if (fistReleaseTimer[hand] >= releaseDebounce)
                {
                    fistArmed[hand] = true;
                    fistActionActive[hand] = false;
                    fistActionRemaining[hand] = 0f;
                }
            }

            if (fistActionActive[hand])
            {
                fistActionRemaining[hand] -= dt;

                if (fistActionRemaining[hand] <= 0f)
                {
                    fistActionRemaining[hand] = 0f;
                    fistActionActive[hand] = false;
                }
            }
        }
    }

    private void SpawnDuringOpenWindow()
    {
        for (int hand = 0; hand < 2; hand++)
        {
            if (!openActionActive[hand])
                continue;

            spawnCooldown[hand] -= Time.fixedDeltaTime;

            if (spawnCooldown[hand] > 0f)
                continue;

            int index = FindFreeSlot(hand);

            if (index >= 0)
            {
                Vector3 palmPosition;

                if (TryGetPalmWorldPosition(hand, out palmPosition))
                    SpawnBody(index, palmPosition);
            }

            spawnCooldown[hand] = Mathf.Max(0.01f, spawnInterval);
        }
    }

    // Reuses destroyed slots instead of permanently exhausting the
    // original 60 indices assigned to each hand.
    private int FindFreeSlot(int hand)
    {
        int start = hand * GhostsPerHand;

        for (int offset = 0; offset < GhostsPerHand; offset++)
        {
            int relativeIndex =
                (nextSpawnOffset[hand] + offset) % GhostsPerHand;

            int index = start + relativeIndex;

            if (body[index] == null)
            {
                nextSpawnOffset[hand] =
                    (relativeIndex + 1) % GhostsPerHand;

                return index;
            }
        }

        return -1;
    }

    private void DeleteDuringFistWindow()
    {
        for (int hand = 0; hand < 2; hand++)
        {
            if (!fistActionActive[hand])
                continue;

            Vector3 palmPosition;

            if (!TryGetPalmWorldPosition(hand, out palmPosition))
                continue;

            for (int i = 0; i < NumberOfGhosts; i++)
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

    private bool TryGetPalmWorldPosition(
        int hand,
        out Vector3 worldPosition
    )
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

        // 0 = wrist, 5/9/13/17 = finger-base knuckles.
        Vector3 palmCenter = (
            landmarks[0] +
            landmarks[5] +
            landmarks[9] +
            landmarks[13] +
            landmarks[17]
        ) / 5f;

        worldPosition = cam.ViewportToWorldPoint(
            new Vector3(
                1f - palmCenter.x,
                1f - palmCenter.y,
                -cam.transform.position.z
            )
        );

        return true;
    }

    private void ApplyBodyGravity()
    {
        for (int i = 0; i < NumberOfGhosts; i++)
        {
            if (body[i] == null)
                continue;

            for (int j = i + 1; j < NumberOfGhosts; j++)
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

                if (distance.sqrMagnitude > closeDistance * closeDistance)
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
        for (int i = 0; i < NumberOfGhosts; i++)
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
        for (int hand = 0; hand < 2; hand++)
        {
            bool tracked = hand == 0
                ? mp.LeftHandTracked
                : mp.RightHandTracked;

            bool openApart = tracked && (
                hand == 0
                    ? mp.LeftHandOpenApart
                    : mp.RightHandOpenApart
            );

            if (!openApart)
                continue;

            Vector3 palmPosition;

            if (!TryGetPalmWorldPosition(hand, out palmPosition))
                continue;

            for (int i = 0; i < NumberOfGhosts; i++)
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

    private void SpawnBody(int index, Vector3 spawnPosition)
    {
        // Create a simple built-in sphere.
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Body";
        sphere.transform.position = spawnPosition;

        float size = Random.Range(minGhostSize, maxGhostSize);
        sphere.transform.localScale = Vector3.one * size;

        // Remove the collider, as in the original method.
        Collider sphereCollider = sphere.GetComponent<Collider>();

        if (sphereCollider != null)
            Destroy(sphereCollider);

        // Use the material assigned in the Inspector.
        // If no material is assigned, use a randomly generated green material.
        Color sphereColor = Random.ColorHSV(
            0.25f, 0.40f,
            0.65f, 1f,
            0.75f, 1f
        );

        Renderer sphereRenderer = sphere.GetComponent<Renderer>();

        if (sphereRenderer != null)
        {
            if (bodyMaterial != null)
            {
                sphereRenderer.material = bodyMaterial;
                sphereColor = bodyMaterial.color;
            }
            else
            {
                sphereRenderer.material = CreateColoredMaterial(sphereColor);
            }
        }

        body[index] = sphere;

        // Keep the transform reference for compatibility with existing code.
        ghostVisuals[index] = sphere.transform;

        // No eyes or mouth: the body is just a sphere.

        // Preserve the original initial velocity.
        bp[index].velocity = new Vector3(
            Mathf.Cos(index * 2f * Mathf.PI / NumberOfGhosts),
            -Mathf.Sin(index * 2f * Mathf.PI / NumberOfGhosts),
            0f
        ) * maxVelocity * 0.3f;

        // Preserve the original mass.
        bp[index].mass = Random.Range(0.01f, 0.05f);

        // Preserve the trail behavior and scale-based width.
        TrailRenderer trail = sphere.AddComponent<TrailRenderer>();
        trailRenderer[index] = trail;

        trail.time = trailDuration;
        trail.minVertexDistance = 0.02f;
        trail.startWidth = GetGhostWorldWidth(index) * trailWidthRatio;
        trail.endWidth = GetGhostWorldWidth(index) * trailEndWidthRatio;

        Shader actualTrailShader = trailShader != null
            ? trailShader
            : Shader.Find("Sprites/Default");

        if (actualTrailShader != null)
            trail.material = new Material(actualTrailShader);

        Gradient gradient = new Gradient();

        gradient.SetKeys(
            new GradientColorKey[]
            {
            new GradientColorKey(
                Color.Lerp(sphereColor, Color.white, 0.4f), 0f),
            new GradientColorKey(sphereColor, 0.4f),
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

        // Spawn the green explosion effect.
        CreateGhostlyExplosion(spawnPosition, brightGreen);
    }

    private float GetGhostWorldWidth(int index)
    {
        if (body == null || body[index] == null)
            return 0f;

        // Sphere diameter in world space.
        return Mathf.Abs(body[index].transform.lossyScale.x);
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

        if (material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 0.25f);
        }

        return material;
    }

    private void UpdateGhostMovement()
    {
        for (int i = 0; i < NumberOfGhosts; i++)
        {
            if (body[i] == null)
                continue;

            Vector3 position = body[i].transform.position;

            if (position.magnitude > radius)
            {
                bp[i].acceleration -=
                    position.normalized *
                    (position.magnitude - radius) * 6f;
            }

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

            // Rotate the sphere and face toward its travel direction.
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

            if (trailRenderer[i] != null)
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

        GameObject deletedSphere = body[index];
        Vector3 position = deletedSphere.transform.position;

        Renderer sphereRenderer = deletedSphere.GetComponent<Renderer>();

        Color explosionColor = sphereRenderer != null
            ? sphereRenderer.material.color
            : brightGreen;

        CreateGhostlyExplosion(position, explosionColor);

        // Stop adding trail points; allow old ones to fade out.
        TrailRenderer trail = trailRenderer[index];

        if (trail != null)
        {
            trail.emitting = false;
            trail.autodestruct = false;
        }

        // Hide the sphere and its face without hiding its trail.
        Renderer[] renderers =
            deletedSphere.GetComponentsInChildren<Renderer>(true);

        foreach (Renderer renderer in renderers)
        {
            if (!(renderer is TrailRenderer))
                renderer.enabled = false;
        }

        // Release the array slot immediately for future spawning.
        body[index] = null;
        ghostVisuals[index] = null;
        trailRenderer[index] = null;

        Destroy(deletedSphere, trailDuration + 0.25f);
    }

    private void CheckGestureAudio()
    {
        if (mp == null)
            return;

        // Track when each hand was last detected.
        if (mp.LeftHandTracked)
            leftLastTrackedTime = Time.time;

        if (mp.RightHandTracked)
            rightLastTrackedTime = Time.time;

        // Allow brief tracking interruptions without resetting gesture states.
        bool leftTrackingValid =
            mp.LeftHandTracked ||
            Time.time - leftLastTrackedTime <= handLostGracePeriod;

        bool rightTrackingValid =
            mp.RightHandTracked ||
            Time.time - rightLastTrackedTime <= handLostGracePeriod;

        // Current gestures, ignoring short tracking dropouts.
        bool leftOpen = leftTrackingValid &&
                        mp.LeftHandOpen &&
                        !mp.LeftHandOpenApart;

        bool rightOpen = rightTrackingValid &&
                         mp.RightHandOpen &&
                         !mp.RightHandOpenApart;

        bool leftFist = leftTrackingValid &&
                        mp.LeftHandTracked &&
                        mp.LeftHandFist;

        bool rightFist = rightTrackingValid &&
                         mp.RightHandTracked &&
                         mp.RightHandFist;

        bool leftOpenApart = leftTrackingValid &&
                             mp.LeftHandOpenApart;

        bool rightOpenApart = rightTrackingValid &&
                              mp.RightHandOpenApart;

        // Require each gesture to remain active long enough to be confirmed.
        bool confirmedLeftOpen =
            ConfirmGesture(leftOpen, ref leftOpenTimer);

        bool confirmedRightOpen =
            ConfirmGesture(rightOpen, ref rightOpenTimer);

        bool confirmedLeftFist =
            ConfirmGesture(leftFist, ref leftFistTimer);

        bool confirmedRightFist =
            ConfirmGesture(rightFist, ref rightFistTimer);

        bool confirmedLeftOpenApart =
            ConfirmGesture(leftOpenApart, ref leftOpenApartTimer);

        bool confirmedRightOpenApart =
            ConfirmGesture(rightOpenApart, ref rightOpenApartTimer);

        // Play audio only on the transition into a confirmed gesture.
        if (confirmedLeftOpen && !previousLeftOpen)
            PlayGestureAudio(GetRandomClip(openGestureClips, openGestureClip));

        if (confirmedRightOpen && !previousRightOpen)
            PlayGestureAudio(GetRandomClip(openGestureClips, openGestureClip));

        if (confirmedLeftFist && !previousLeftFist)
            PlayGestureAudio(GetRandomClip(fistGestureClips, fistGestureClip));

        if (confirmedRightFist && !previousRightFist)
            PlayGestureAudio(GetRandomClip(fistGestureClips, fistGestureClip));

        if (confirmedLeftOpenApart && !previousLeftOpenApart)
            PlayGestureAudio(GetRandomClip(openApartGestureClips, openApartGestureClip));

        if (confirmedRightOpenApart && !previousRightOpenApart)
            PlayGestureAudio(GetRandomClip(openApartGestureClips, openApartGestureClip));

        // Store confirmed states, not raw MediaPipe detections.
        previousLeftOpen = confirmedLeftOpen;
        previousRightOpen = confirmedRightOpen;

        previousLeftFist = confirmedLeftFist;
        previousRightFist = confirmedRightFist;

        previousLeftOpenApart = confirmedLeftOpenApart;
        previousRightOpenApart = confirmedRightOpenApart;
    }

    private AudioClip GetRandomClip(AudioClip[] clips, AudioClip fallbackClip)
    {
        if (clips == null || clips.Length == 0)
            return fallbackClip;

        int validClipCount = 0;
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null)
                validClipCount++;
        }

        if (validClipCount == 0)
            return fallbackClip;

        int selectedIndex = Random.Range(0, validClipCount);

        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] == null)
                continue;

            if (selectedIndex == 0)
                return clips[i];

            selectedIndex--;
        }

        return fallbackClip;
    }

    private void PlayDeathSound()
    {
        AudioClip clip = GetRandomClip(deathClips, deathClip);
        if (clip == null)
            return;

        // One shared cooldown limits how often death sounds start, even when
        // many spheres explode at once. Existing sounds are never stopped.
        if (Time.time < nextDeathSoundTime)
            return;

        float pitch = Random.Range(0.85f, 1.15f);
        float minimumGap = Mathf.Max(
            Mathf.Max(0.5f, deathSoundCooldown),
            clip.length * 0.5f
        );
        nextDeathSoundTime = Time.time + minimumGap;

        // Each playback gets its own AudioSource, so its pitch/volume cannot
        // change or cut off another death or gesture sound.
        PlayIndependentAudio(
            clip,
            null,
            Mathf.Clamp01(deathClipVolume),
            pitch,
            "DeathAudioVoice"
        );
    }

    private void PlayGestureAudio(AudioClip clip)
    {
        if (gestureAudioSource == null || clip == null)
            return;

        // Suppress new gesture sounds during explosions, but do not stop a
        // gesture sound that is already playing.
        if (Time.time < suppressGestureAudioUntil)
            return;

        if (Time.time < nextGestureAudioTime)
            return;

        PlayIndependentAudio(
            clip,
            gestureAudioSource,
            1f,
            Random.Range(0.85f, 1.15f),
            "GestureAudioVoice"
        );

        nextGestureAudioTime = Time.time + gestureAudioCooldown;
    }

    private void PlayIndependentAudio(
        AudioClip clip,
        AudioSource template,
        float volume,
        float pitch,
        string objectName)
    {
        if (clip == null)
            return;

        GameObject audioObject = new GameObject(objectName);
        audioObject.transform.position = template != null
            ? template.transform.position
            : transform.position;
        audioObject.transform.SetParent(transform, true);

        AudioSource voice = audioObject.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.loop = false;
        voice.clip = clip;
        voice.volume = Mathf.Clamp01(volume * (template != null ? template.volume : 1f));
        voice.pitch = pitch;

        if (template != null)
        {
            // Preserve the Inspector-configured routing and playback behavior.
            voice.outputAudioMixerGroup = template.outputAudioMixerGroup;
            voice.mute = template.mute;
            voice.bypassEffects = template.bypassEffects;
            voice.bypassListenerEffects = template.bypassListenerEffects;
            voice.bypassReverbZones = template.bypassReverbZones;
            voice.priority = template.priority;
            voice.panStereo = template.panStereo;
            voice.spatialBlend = template.spatialBlend;
            voice.reverbZoneMix = template.reverbZoneMix;
            voice.dopplerLevel = template.dopplerLevel;
            voice.spread = template.spread;
            voice.rolloffMode = template.rolloffMode;
            voice.minDistance = template.minDistance;
            voice.maxDistance = template.maxDistance;
            voice.velocityUpdateMode = template.velocityUpdateMode;
        }
        else
        {
            voice.priority = 128;
            voice.spatialBlend = 0f;
        }

        voice.Play();
        Destroy(audioObject, (clip.length / Mathf.Max(0.01f, Mathf.Abs(pitch))) + 0.1f);
    }

    private bool ConfirmGesture(
    bool gestureActive,
    ref float timer)
    {
        if (!gestureActive)
        {
            timer = 0f;
            return false;
        }

        timer += Time.deltaTime;

        return timer >= gestureConfirmTime;
    }

    private void CreateGhostlyExplosion(Vector3 position, Color baseColor)
    {
        // Block new gesture sounds briefly during explosions (for example,
        // prevent the bell from triggering), without cutting off sounds already
        // playing. Every playback uses its own AudioSource.
        suppressGestureAudioUntil = Mathf.Max(
            suppressGestureAudioUntil,
            Time.time + explosionGestureAudioMuteTime
        );

        GameObject explosion = new GameObject("GhostlyGreenExplosion");
        explosion.transform.position = position;

        ParticleSystem particles =
            explosion.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = particles.main;
        main.playOnAwake = false;

        particles.Stop(
            true,
            ParticleSystemStopBehavior.StopEmittingAndClear
        );

        main.loop = false;
        main.duration = explosionDuration;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(
            0.8f, explosionDuration
        );
        main.startSpeed = new ParticleSystem.MinMaxCurve(
            explosionMinSpeed, explosionMaxSpeed
        );
        main.startSize = new ParticleSystem.MinMaxCurve(
            explosionMinSize, explosionMaxSize
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

        PlayDeathSound();

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
        if (sharedFaceMaterial != null)
            Destroy(sharedFaceMaterial);
    }
}
