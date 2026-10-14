using UnityEngine;
using UnityEngine.InputSystem;

public class ChickenPlayer : MonoBehaviour
{
    public float speed = 4.5f;
    public float runSpeed = 6.5f;
    public ChickenBoids chickens;
    [UnityEngine.Serialization.FormerlySerializedAs("dog")] public CatController cat;
    public bool useMicrophone = true;
    public float microphoneSensitivity = 25f;
    public float shout;
    CharacterController controller;
    Animator animator;
    Vector3 startPosition;
    FarmFMSynth sound;
    AudioClip microphone;
    string device;
    float[] samples = new float[256];
    float shoutTime;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponentInChildren<Animator>();
        startPosition = transform.position;
        sound = chickens.GetComponent<FarmFMSynth>();
        if (useMicrophone && Microphone.devices.Length > 0)
        {
            device = Microphone.devices[0];
            microphone = Microphone.Start(device, true, 1, 16000);
        }
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        float volume = 0f;
        if (microphone != null)
        {
            int position = Microphone.GetPosition(device);
            if (position >= samples.Length)
            {
                microphone.GetData(samples, position - samples.Length);
                for (int i = 0; i < samples.Length; i++) volume += samples[i] * samples[i];
                volume = Mathf.Sqrt(volume / samples.Length);
            }
        }
        // Louder speech creates a stronger shout. Space works without a microphone.
        float voice = Mathf.Clamp01((volume - 0.03f) * microphoneSensitivity);
        shout = Mathf.Max(voice,
            Mathf.MoveTowards(shout, 0f, Time.deltaTime * 2f));
        shoutTime -= Time.deltaTime;
        if (voice > 0.3f && shoutTime <= 0f)
        {
            sound.Shout(transform.position, voice);
            shoutTime = 0.65f;
        }
        if (keyboard == null) return;
        if (keyboard.spaceKey.isPressed) shout = 1f;
        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            sound.Shout(transform.position, 1f);
            shoutTime = 0.65f;
        }
        Vector3 movement = Vector3.zero;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) movement.z++;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) movement.z--;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) movement.x++;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) movement.x--;
        movement = movement.normalized;
        float currentSpeed = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed ? runSpeed : speed;
        controller.Move((movement * currentSpeed + Vector3.down * 2f) * Time.deltaTime);
        Vector3 velocity = controller.velocity;
        velocity.y = 0f;
        animator.SetFloat("Speed", velocity.magnitude, 0.1f, Time.deltaTime);
        if (movement.sqrMagnitude > 0f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(movement), 12f * Time.deltaTime);

        if (keyboard.rKey.wasPressedThisFrame)
        {
            controller.enabled = false;
            transform.position = startPosition;
            controller.enabled = true;
            chickens.Restart();
            cat.Restart();
            shout = 0f;
        }
    }

    void OnDisable()
    {
        if (microphone != null)
        {
            Microphone.End(device);
            Destroy(microphone);
        }
    }
}
