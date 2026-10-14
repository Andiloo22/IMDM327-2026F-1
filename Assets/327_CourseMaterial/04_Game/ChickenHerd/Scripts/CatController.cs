using UnityEngine;

public class CatController : MonoBehaviour
{
    public Transform player;
    public ChickenBoids chickens;
    public Terrain terrain;
    public float speed = 5.5f;
    CharacterController controller;
    Animator animator;
    FarmFMSynth sound;
    Transform target;
    float changeTarget, meowTime;
    Vector3 startPosition;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponentInChildren<Animator>();
        sound = chickens.GetComponent<FarmFMSynth>();
        startPosition = transform.position;
    }

    public void Restart()
    {
        controller.enabled = false;
        transform.position = startPosition;
        controller.enabled = true;
        changeTarget = 0f;
    }

    void FixedUpdate()
    {
        changeTarget -= Time.fixedDeltaTime;
        meowTime -= Time.fixedDeltaTime;
        if (changeTarget <= 0f)
        {
            // Sometimes chase a chicken, sometimes get in the player's way.
            if (Random.value < 0.6f && chickens.transform.childCount > 0)
                target = chickens.transform.GetChild(Random.Range(0, chickens.transform.childCount));
            else target = player;
            changeTarget = Random.Range(1.5f, 3f);
        }
        Vector3 destination = target.position;
        if (target == player) destination += player.forward * 1.5f;
        Vector3 distance = destination - transform.position;
        distance.y = 0f;
        if (distance.magnitude > 0.6f)
        {
            controller.Move((distance.normalized * speed + Vector3.down * 3f) * Time.fixedDeltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(distance), 10f * Time.fixedDeltaTime);
        }
        else controller.Move(Vector3.down * 3f * Time.fixedDeltaTime);
        Vector3 velocity = controller.velocity;
        velocity.y = 0f;
        animator.SetFloat("Speed", velocity.magnitude, 0.1f, Time.fixedDeltaTime);
        if (meowTime <= 0f)
        {
            sound.Meow(transform.position, velocity.magnitude);
            meowTime = Random.Range(2f, 5f);
        }
    }
}
