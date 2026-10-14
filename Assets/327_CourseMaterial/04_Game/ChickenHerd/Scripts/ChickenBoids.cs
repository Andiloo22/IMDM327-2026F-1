// IMDM 327. Based on InteractiveBody's gravity and repulsion.
using UnityEngine;

public class ChickenBoids : MonoBehaviour
{
    public GameObject chickenPrefab;
    public Transform player;
    public BoxCollider pen;
    public GameObject gate;
    public Terrain terrain;
    [UnityEngine.Serialization.FormerlySerializedAs("dog")] public Transform cat;
    public int numberOfChickens = 100;
    public float G = 1.5f;
    public float closeDistance = 0.85f; // Squared distance.
    public float walkSpeed = 1.8f;
    public float runSpeed = 4.8f;
    public float fearDistance = 4.2f;
    public float fearForce = 28f;
    public float wanderForce = 2.5f;
    public int numberOfGroups = 5;
    public LayerMask obstacleLayers = 1;

    struct BodyProperty
    {
        public float mass;
        public Vector3 velocity, acceleration;
        public Vector3 wander;
        public float turnTime, sprintTime, panic;
        public float cluckTime;
        public int group;
        public bool inPen;
    }

    GameObject[] body;
    BodyProperty[] bp;
    Animator[] animator;
    FarmFMSynth sound;
    ChickenPlayer farmer;
    Vector3[] groupTarget;
    float[] groupTime;
    bool[] groupWalking, groupRunning;
    int chickenCount;

    void Start()
    {
        body = new GameObject[numberOfChickens];
        bp = new BodyProperty[numberOfChickens];
        animator = new Animator[numberOfChickens];
        sound = GetComponent<FarmFMSynth>();
        farmer = player.GetComponent<ChickenPlayer>();
        groupTarget = new Vector3[numberOfGroups];
        groupTime = new float[numberOfGroups];
        groupWalking = new bool[numberOfGroups];
        groupRunning = new bool[numberOfGroups];
        for (int i = 0; i < numberOfChickens; i++)
        {
            body[i] = Instantiate(chickenPrefab, transform);
            body[i].name = "Chicken " + (i + 1);
            bp[i].mass = Random.Range(0.02f, 0.04f);
            animator[i] = body[i].GetComponentInChildren<Animator>();
            animator[i].applyRootMotion = false;
            animator[i].Play(0, 0, Random.value);
            bp[i].group = i % numberOfGroups;
        }
        Restart();
    }

    public void Restart()
    {
        chickenCount = 0;
        gate.SetActive(false);
        for (int group = 0; group < numberOfGroups; group++)
        {
            groupWalking[group] = true;
            groupRunning[group] = group % 2 == 0;
            groupTime[group] = Random.Range(2f, 5f);
            groupTarget[group] = new Vector3(Random.Range(-8f, 8f), 0f, Random.Range(-7f, 5f));
        }
        for (int i = 0; i < body.Length; i++)
        {
            int columns = Mathf.CeilToInt(Mathf.Sqrt(body.Length));
            body[i].transform.position = new Vector3((i % columns - (columns - 1) * 0.5f) * 1.1f,
                0f, (i / columns - (columns - 1) * 0.5f) * 1.1f - 1f);
            Vector3 position = body[i].transform.position;
            position.y = terrain.SampleHeight(position) + terrain.transform.position.y;
            body[i].transform.position = position;
            float angle = Random.Range(0f, 2f * Mathf.PI);
            bp[i].wander = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            bp[i].velocity = bp[i].wander * walkSpeed;
            bp[i].acceleration = Vector3.zero;
            bp[i].turnTime = Random.Range(0.1f, 1f);
            bp[i].sprintTime = 0f;
            bp[i].panic = 0f;
            bp[i].cluckTime = Random.Range(0.2f, 6f);
            bp[i].inPen = false;
        }
    }

    void FixedUpdate()
    {
        for (int group = 0; group < numberOfGroups; group++)
        {
            groupTime[group] -= Time.fixedDeltaTime;
            if (groupTime[group] <= 0f)
            {
                groupWalking[group] = !groupWalking[group];
                groupRunning[group] = Random.value < 0.45f;
                groupTime[group] = groupWalking[group] ? Random.Range(4f, 8f) : Random.Range(0.7f, 1.5f);
                groupTarget[group] = new Vector3(Random.Range(-8f, 8f), 0f, Random.Range(-7f, 5f));
            }
        }
        float shout = farmer.shout;
        float scareDistance = fearDistance + shout * 4f;
        for (int i = 0; i < body.Length; i++)
        {
            bp[i].acceleration = Vector3.zero;
            bp[i].panic = Mathf.MoveTowards(bp[i].panic, 0f, Time.fixedDeltaTime * 0.7f);
            Vector3 distance = body[i].transform.position - player.position;
            distance.y = 0f;
            if (!bp[i].inPen && distance.sqrMagnitude < scareDistance * scareDistance) bp[i].panic = 1f;
            Vector3 catDistance = body[i].transform.position - cat.position;
            catDistance.y = 0f;
            if (!bp[i].inPen && catDistance.sqrMagnitude < 16f) bp[i].panic = 1f;
            if (bp[i].inPen) bp[i].panic = 0f;
        }
        // Same pair loop and gravity formula as InteractiveBody.
        for (int i = 0; i < body.Length; i++)
        {
            for (int j = i + 1; j < body.Length; j++)
            {
                if (bp[i].inPen != bp[j].inPen) continue; // IF one chicken is in the pen, ignore the other.
                Vector3 distance = body[j].transform.position - body[i].transform.position;
                distance.y = 0f;
                Vector3 gravity = CalculateGravity(distance, bp[i].mass, bp[j].mass);
                if (distance.sqrMagnitude > closeDistance)
                {
                    if (!bp[i].inPen && bp[i].group != bp[j].group) gravity *= 0.1f;
                    bp[i].acceleration += gravity / bp[i].mass;
                    bp[j].acceleration -= gravity / bp[j].mass;
                }
                else
                {
                    bp[i].acceleration -= 3f * gravity / bp[i].mass;
                    bp[j].acceleration += 3f * gravity / bp[j].mass;
                }
                // A frightened chicken alarms its nearby neighbours.
                if (!bp[i].inPen && distance.sqrMagnitude < 6.25f)
                {
                    float panic = Mathf.Max(bp[i].panic, bp[j].panic) * 0.8f;
                    bp[i].panic = Mathf.Max(bp[i].panic, panic);
                    bp[j].panic = Mathf.Max(bp[j].panic, panic);
                }
            }
        }

        Bounds penBounds = pen.bounds;
        for (int i = 0; i < body.Length; i++)
        {
            Vector3 position = body[i].transform.position;
            Vector3 fromPlayer = position - player.position;
            fromPlayer.y = 0f;
            bool scared = !bp[i].inPen && fromPlayer.magnitude < scareDistance;

            // Keep moving, change direction often, and occasionally sprint.
            bp[i].turnTime -= Time.fixedDeltaTime;
            bp[i].sprintTime -= Time.fixedDeltaTime;
            if (bp[i].turnTime <= 0f)
            {
                float angle = Mathf.Atan2(bp[i].velocity.x, bp[i].velocity.z) + Random.Range(-80f, 80f) * Mathf.Deg2Rad;
                bp[i].wander = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                bp[i].turnTime = Random.Range(0.45f, 1.25f);
                if (!bp[i].inPen && Random.value < 0.4f) bp[i].sprintTime = Random.Range(0.4f, 0.9f);
            }
            int group = bp[i].group;
            float groupSpeed = groupWalking[group] ? (groupRunning[group] ? runSpeed * 0.75f : walkSpeed) : 0f;
            float maxVelocity = bp[i].inPen ? 0.9f : Mathf.Lerp(groupSpeed, runSpeed,
                Mathf.Max(bp[i].panic, bp[i].sprintTime > 0f ? 1f : 0f));
            Vector3 toGroup = groupTarget[group] - position;
            toGroup.y = 0f;
            Vector3 direction = bp[i].inPen ? bp[i].wander : (toGroup.normalized + bp[i].wander * 0.35f).normalized;
            bp[i].acceleration += (direction * maxVelocity - bp[i].velocity) * wanderForce;

            // The player replaces the hand's force source, pushing chickens away.
            if (scared)
            {
                bp[i].acceleration += fromPlayer.normalized * fearForce * (1f + shout * 1.5f) * (1f - fromPlayer.magnitude / scareDistance);
            }
            Vector3 fromCat = position - cat.position;
            fromCat.y = 0f;
            if (!bp[i].inPen && fromCat.magnitude < 4f)
                bp[i].acceleration += fromCat.normalized * fearForce * (1f - fromCat.magnitude / 4f);
            if (!bp[i].inPen && bp[i].panic > 0f)
            {
                Vector3 sideways = new Vector3(-fromPlayer.z, 0f, fromPlayer.x).normalized;
                bp[i].acceleration += sideways * Mathf.Sin(Time.fixedTime * 5f + i * 2.5f) * 7f * bp[i].panic;
            }

            if (bp[i].inPen)
            {
                Vector3 distance = penBounds.center - position;
                distance.y = 0f;
                bp[i].acceleration += distance * 0.4f;
            }
            // Turn back before reaching the outer fence.
            if (Mathf.Abs(position.x) > 9.5f)
                bp[i].acceleration.x -= Mathf.Sign(position.x) * (Mathf.Abs(position.x) - 9.5f) * 5f;
            if (position.z < -8f) bp[i].acceleration.z += (-8f - position.z) * 5f;
            if (position.z > 17f) bp[i].acceleration.z -= (position.z - 17f) * 5f;

            bp[i].acceleration = Vector3.ClampMagnitude(bp[i].acceleration, 20f);
            bp[i].velocity += bp[i].acceleration * Time.fixedDeltaTime;
            bp[i].velocity.y = 0f;
            if (bp[i].velocity.magnitude > maxVelocity)
                bp[i].velocity = bp[i].velocity.normalized * maxVelocity;

            Vector3 movement = bp[i].velocity * Time.fixedDeltaTime;
            // Bounce away from a fence.
            if (movement.sqrMagnitude > 0f && Physics.SphereCast(position + Vector3.up * 0.45f, 0.3f,
                movement.normalized, out RaycastHit hit, movement.magnitude + 0.04f, obstacleLayers, QueryTriggerInteraction.Ignore))
            {
                movement = movement.normalized * Mathf.Max(0f, hit.distance - 0.04f);
                bp[i].velocity = Vector3.Reflect(bp[i].velocity, hit.normal) * 0.8f;
                bp[i].wander = Vector3.Reflect(bp[i].wander, hit.normal);
            }
            position += movement;

            // Count each chicken once, after it has entered the pen.
            if (!bp[i].inPen && penBounds.Contains(position))
            {
                bp[i].inPen = true;
                chickenCount++;
                if (chickenCount == body.Length) gate.SetActive(true);
            }
            if (bp[i].inPen)
            {
                // Caught chickens stay inside the pen.
                float x = Mathf.Clamp(position.x, penBounds.min.x + 0.35f, penBounds.max.x - 0.35f);
                float z = Mathf.Clamp(position.z, penBounds.min.z + 0.35f, penBounds.max.z - 0.35f);
                if (x != position.x) { bp[i].velocity.x *= -0.5f; bp[i].wander.x *= -1f; }
                if (z != position.z) { bp[i].velocity.z *= -0.5f; bp[i].wander.z *= -1f; }
                position.x = x;
                position.z = z;
            }
            position.y = terrain.SampleHeight(position) + terrain.transform.position.y;
            body[i].transform.position = position;
            if (bp[i].velocity.sqrMagnitude > 0.02f)
                body[i].transform.rotation = Quaternion.Slerp(body[i].transform.rotation,
                    Quaternion.LookRotation(bp[i].velocity), 12f * Time.fixedDeltaTime);
            animator[i].SetFloat("Speed", bp[i].velocity.magnitude, 0.1f, Time.fixedDeltaTime);
            float hop = !bp[i].inPen && (bp[i].panic > 0.5f || bp[i].sprintTime > 0f)
                ? Mathf.Abs(Mathf.Sin(Time.fixedTime * 16f + i)) * 0.12f : 0f;
            animator[i].transform.localPosition = Vector3.up * (0.12f + hop);
            bp[i].cluckTime -= Time.fixedDeltaTime;
            if (bp[i].cluckTime <= 0f)
            {
                sound.Cluck(i, position, bp[i].velocity.magnitude / runSpeed, bp[i].panic, bp[i].inPen);
                bp[i].cluckTime = bp[i].panic > 0.3f ? Random.Range(0.8f, 2f) : Random.Range(3f, 9f);
            }
        }
    }

    Vector3 CalculateGravity(Vector3 distance, float m1, float m2)
    {
        return G * m1 * m2 / (distance.magnitude + 0.1f) * distance.normalized;
    }

}
