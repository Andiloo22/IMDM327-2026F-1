// InteractiveBody Starter Code
// Fall 2025. IMDM 327
// Instructor. Myungin Lee
using UnityEngine;

public class InteractiveBody : MonoBehaviour
{
    private float[] timer = new float[2];
    private float waitTime = 2.0f;
    private int[] spawnIndex = new int[2];
    public float G = 1f;
    private GameObject[] body;
    BodyProperty[] bp;
    private int numberOfSphere = 120;
    public float fastforwardConst = 1f;
    public float radius = 3.5f;
    TrailRenderer[] trailRenderer;
    private GameObject[] interactivePoint = new GameObject[2];
    private Vector3[] previousHandPosition = new Vector3[2];
    private bool[] handWasPinched = new bool[2];
    private float[] actuation = new float[2];
    public float interactiveMass = 18f;
    public float shakeSensitivity = 3f;
    public float gravityDecay = 2f;
    MediaPipeBodyTracker mp;
    Camera cam;
    public float maxVelocity = 6f;
    public float closeDistance = 0.16f;
    public Shader trailShader;
    public Shader pointShader;

    struct BodyProperty
    {
        public float mass;
        public Vector3 velocity;
        public Vector3 acceleration;
    }


    void Start()
    {
        if (mp == null)
        {
            mp = FindObjectOfType<MediaPipeBodyTracker>();
            if (mp == null)
            {
                Debug.LogWarning("InteractiveBody could not locate a MediaPipeBodyTracker in the scene.");
            }
        }
        cam = Camera.main;
        for (int hand = 0; hand < 2; hand++)
        {
            interactivePoint[hand] = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            interactivePoint[hand].transform.localScale = Vector3.one * 0.05f;
            Destroy(interactivePoint[hand].GetComponent<Collider>());
            interactivePoint[hand].GetComponent<Renderer>().material = new Material(pointShader != null ? pointShader : Shader.Find("Universal Render Pipeline/Unlit"));
            interactivePoint[hand].SetActive(false);
        }
        bp = new BodyProperty[numberOfSphere];
        body = new GameObject[numberOfSphere];
        trailRenderer = new TrailRenderer[numberOfSphere];
        spawnIndex[0] = 0;
        spawnIndex[1] = numberOfSphere / 2;
    }

    void FixedUpdate()
    {
        for (int hand = 0; hand < 2; hand++)
        {
            bool isHandOpen = hand == 0 ? mp.LeftHandOpen : mp.RightHandOpen;
            if (isHandOpen)
            {
                if (timer[hand] > 0)
                {
                    timer[hand] -= Time.deltaTime;
                }
                if (timer[hand] <= 0)
                {
                    int idx = spawnIndex[hand];
                    int limitIdx = (hand == 0) ? numberOfSphere / 2 : numberOfSphere;

                    if (idx < limitIdx)
                    {
                        body[idx] = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        Destroy(body[idx].GetComponent<Collider>());

                        Vector3 handPos = hand == 0 ? mp.LeftHandPosition : mp.RightHandPosition;
                        Vector3 spawnPosition = cam.ViewportToWorldPoint(new Vector3(1f - handPos.x, 1f - handPos.y, -cam.transform.position.z));
                        body[idx].transform.position = spawnPosition;

                        bp[idx].velocity = new Vector3(Mathf.Cos(idx * 2f * Mathf.PI / numberOfSphere),
                                                    -Mathf.Sin(idx * 2f * Mathf.PI / numberOfSphere), 0f) * maxVelocity * 0.3f;
                        bp[idx].mass = Random.Range(0.01f, 0.05f);
                        body[idx].GetComponent<MeshRenderer>().enabled = false;

                        trailRenderer[idx] = body[idx].AddComponent<TrailRenderer>();
                        trailRenderer[idx].time = 3.0f;
                        trailRenderer[idx].startWidth = 0.025f;
                        trailRenderer[idx].endWidth = 0.002f;
                        trailRenderer[idx].material = new Material(trailShader != null ? trailShader : Shader.Find("Sprites/Default"));
                        Gradient gradient = new Gradient();
                        float h = (idx / (float)numberOfSphere) % 1f;
                        float s = 0.6f;
                        float v = 1f;
                        Color c = Color.HSVToRGB(h, s, v);

                        gradient.SetKeys(
                            new GradientColorKey[] { new GradientColorKey(c, 0.0f),
                                                    new GradientColorKey(c, 1f) },
                            new GradientAlphaKey[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(0.0f, 1.0f) }
                        );
                        trailRenderer[idx].colorGradient = gradient;
                        spawnIndex[hand]++;
                    }

                    timer[hand] = waitTime;
                }
            }
        }

        for (int i = 0; i < numberOfSphere; i++)
        {
            bp[i].acceleration = Vector3.zero;
        }

        for (int i = 0; i < numberOfSphere; i++)
        {
            for (int j = i + 1; j < numberOfSphere; j++)
            {
                if (body[i] == null || body[j] == null) continue;
                Vector3 distance = body[j].transform.position - body[i].transform.position;
                Vector3 gravity = CalculateGravity(distance, bp[i].mass, bp[j].mass);

                if (distance.sqrMagnitude > closeDistance)
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

        for (int i = 0; i < numberOfSphere; i++)
        {
            float randomScale = 1f;
            if (Random.Range(0f, 1.05f) > 1f)
            {
                bp[i].acceleration += new Vector3(randomScale * Random.Range(-1f, 1f), randomScale * Random.Range(-1f, 1f), randomScale * Random.Range(-1f, 1f));
            }
        }


        if (mp == null)
        {
            mp = FindObjectOfType<MediaPipeBodyTracker>();
        }

        for (int hand = 0; hand < 2; hand++)
        {
            bool handTracked = mp != null && (hand == 0 ? mp.LeftHandTracked : mp.RightHandTracked);
            bool handPinched = handTracked && (hand == 0 ? mp.LeftHandOpen : mp.RightHandOpen);
            interactivePoint[hand].SetActive(handPinched);
            if (handPinched && cam != null)
            {
                Vector3 handPosition = hand == 0 ? mp.LeftHandPosition : mp.RightHandPosition;
                Vector3 interactPoint = cam.ViewportToWorldPoint(new Vector3(1f - handPosition.x, 1f - handPosition.y, -cam.transform.position.z));
                interactivePoint[hand].transform.position = interactPoint;
                if (!handWasPinched[hand]) previousHandPosition[hand] = handPosition;

                float speed = (handPosition - previousHandPosition[hand]).magnitude / Time.fixedDeltaTime;
                float movement = Mathf.Clamp01((speed - 0.15f) * shakeSensitivity);
                actuation[hand] = Mathf.Max(movement * movement, Mathf.MoveTowards(actuation[hand], 0f, gravityDecay * Time.fixedDeltaTime));
                interactivePoint[hand].transform.localScale = Vector3.one * (0.05f + 0.12f * actuation[hand]);

                for (int i = 0; i < numberOfSphere; i++)
                {
                    if (body[i] == null) continue;
                    Vector3 distance = interactPoint - body[i].transform.position;
                    bp[i].acceleration += CalculateGravity(distance, bp[i].mass, interactiveMass) / bp[i].mass * actuation[hand];
                }
                previousHandPosition[hand] = handPosition;
            }
            else
            {
                actuation[hand] = 0f;
            }
            handWasPinched[hand] = handPinched;
        }

        for (int i = 0; i < numberOfSphere; i++)
        {
            if (body[i] == null) continue;
            Vector3 distance = body[i].transform.position;
            if (distance.magnitude > radius)
            {
                bp[i].acceleration -= distance.normalized * (distance.magnitude - radius) * 6f;
            }
            bp[i].acceleration.z -= distance.z * 3f + bp[i].velocity.z;
            bp[i].acceleration = Vector3.ClampMagnitude(bp[i].acceleration, 20f);
            bp[i].velocity += bp[i].acceleration * Time.deltaTime * fastforwardConst;
            if (bp[i].velocity.magnitude > maxVelocity)
            {
                bp[i].velocity = maxVelocity * bp[i].velocity.normalized;
            }
            body[i].transform.position += bp[i].velocity * Time.deltaTime * fastforwardConst;
            if (bp[i].velocity.sqrMagnitude > 0f)
                body[i].transform.LookAt(body[i].transform.position + bp[i].velocity);
        }

    }


    private Vector3 CalculateGravity(Vector3 distanceVector, float m1, float m2)
    {
        Vector3 gravity = new Vector3(0f, 0f, 0f);
        float eps = 0.1f;
        gravity = G * m1 * m2 / (distanceVector.magnitude + eps) * distanceVector.normalized;
        return gravity;
    }
}