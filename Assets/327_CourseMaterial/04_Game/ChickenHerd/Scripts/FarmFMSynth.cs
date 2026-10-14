using System;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class FarmFMSynth : MonoBehaviour
{
    public float chickenFrequency = 650f;
    public float catFrequency = 480f;
    public float shoutFrequency = 150f;
    public float chickenIndex = 2.5f;
    public float catIndex = 3f;
    public float shoutIndex = 5f;
    [Range(0f, 1f)] public float volume = 0.8f;

    struct Note
    {
        public float frequency, endFrequency, ratio, index, gain, duration, pan;
        public int type, pulses;
        public bool trigger;
    }
    struct Voice
    {
        public Note note;
        public double carrierPhase, modulatorPhase, time;
        public bool playing;
    }

    Note[] notes;
    Voice[] voices;
    readonly object noteLock = new object();
    AudioSource source;
    Transform listener;
    int sampleRate, chickenCount;
    const double TwoPi = Math.PI * 2.0;

    void Awake()
    {
        chickenCount = GetComponent<ChickenBoids>().numberOfChickens;
        notes = new Note[chickenCount + 2];
        voices = new Voice[notes.Length];
        sampleRate = AudioSettings.outputSampleRate;
        listener = Camera.main.transform;
        source = GetComponent<AudioSource>();
        source.clip = null;
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        source.volume = 1f;
    }

    void OnEnable() { source.Play(); }
    void OnDisable() { source.Stop(); }

    public void Cluck(int chicken, Vector3 position, float speed, float panic, bool inPen)
    {
        float pitch = UnityEngine.Random.Range(0.85f, 1.2f);
        bool alarm = panic > 0.3f;
        Note note = new Note();
        note.type = 0;
        note.frequency = chickenFrequency * pitch * (1f + speed * 0.15f + panic * 0.6f);
        note.endFrequency = note.frequency * 0.45f;
        note.ratio = 1.5f + panic * 0.5f;
        note.index = chickenIndex + speed + panic * 2f;
        note.gain = inPen ? 0.025f : (alarm ? 0.065f : 0.08f);
        note.duration = alarm ? 0.36f : 0.18f;
        note.pulses = alarm ? 3 : 1;
        Play(chicken, position, note);
    }

    public void Meow(Vector3 position, float speed)
    {
        Note note = new Note();
        note.type = 1;
        note.frequency = catFrequency * UnityEngine.Random.Range(0.9f, 1.1f);
        note.endFrequency = note.frequency * 0.6f;
        note.ratio = 2f;
        note.index = catIndex + speed * 0.15f;
        note.gain = 0.17f;
        note.duration = 0.65f;
        note.pulses = 1;
        Play(chickenCount, position, note);
    }

    public void Shout(Vector3 position, float strength)
    {
        Note note = new Note();
        note.type = 2;
        note.frequency = shoutFrequency * (1f + strength * 0.2f);
        note.endFrequency = note.frequency * 0.65f;
        note.ratio = 2f;
        note.index = shoutIndex * strength;
        note.gain = 0.3f * strength;
        note.duration = 0.5f;
        note.pulses = 1;
        Play(chickenCount + 1, position, note);
    }

    void Play(int voice, Vector3 position, Note note)
    {
        Vector3 distance = position - listener.position;
        note.pan = Mathf.Clamp(Vector3.Dot(distance.normalized, listener.right), -1f, 1f);
        note.gain *= volume / (1f + distance.sqrMagnitude * 0.015f);
        note.trigger = true;
        // Send the note to the audio thread once per buffer.
        lock (noteLock) notes[voice] = note;
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        lock (noteLock)
        {
            for (int voice = 0; voice < voices.Length; voice++)
            {
                if (!notes[voice].trigger) continue;
                voices[voice].note = notes[voice];
                voices[voice].time = 0.0;
                voices[voice].playing = true;
                notes[voice].trigger = false;
            }
        }
        Array.Clear(data, 0, data.Length);
        for (int voice = 0; voice < voices.Length; voice++)
        {
            if (!voices[voice].playing) continue;
            Voice v = voices[voice];
            Note note = v.note;
            float left = (float)Math.Sqrt((1f - note.pan) * 0.5f);
            float right = (float)Math.Sqrt((1f + note.pan) * 0.5f);
            for (int i = 0; i < data.Length; i += channels)
            {
                if (v.time >= note.duration) { v.playing = false; break; }
                double progress = v.time / note.duration;
                double frequency = note.frequency + (note.endFrequency - note.frequency) * progress;
                double envelope;
                if (note.type == 0)
                {
                    double pulse = progress * note.pulses % 1.0;
                    envelope = Math.Min(pulse * note.duration / note.pulses / 0.004, 1.0) * Math.Exp(-pulse * 6.0);
                }
                else
                {
                    frequency *= 1.0 + 0.25 * Math.Sin(progress * Math.PI);
                    frequency += Math.Sin(v.time * TwoPi * 6.0) * 8.0;
                    envelope = Math.Min(v.time / 0.025, 1.0) * Math.Exp(-progress * 2.5);
                }
                envelope *= Math.Min((note.duration - v.time) / 0.015, 1.0);
                // Same FM formula as SoundFM, with a changing pitch and envelope.
                float sample = note.gain * (float)envelope * FM(v.carrierPhase, v.modulatorPhase, note.index * envelope);
                if (channels == 1) data[i] += sample;
                else
                {
                    data[i] += sample * left;
                    data[i + 1] += sample * right;
                }
                frequency = Math.Min(Math.Max(frequency, 20.0), sampleRate * 0.2);
                v.carrierPhase = (v.carrierPhase + TwoPi * frequency / sampleRate) % TwoPi;
                v.modulatorPhase = (v.modulatorPhase + TwoPi * frequency * note.ratio / sampleRate) % TwoPi;
                v.time += 1.0 / sampleRate;
            }
            voices[voice] = v;
        }
        for (int i = 0; i < data.Length; i++) data[i] = (float)Math.Tanh(data[i]);
    }

    public float FM(double carrierPhase, double modulatorPhase, double index)
    {
        return (float)Math.Sin(carrierPhase + index * Math.Sin(modulatorPhase));
    }
}
