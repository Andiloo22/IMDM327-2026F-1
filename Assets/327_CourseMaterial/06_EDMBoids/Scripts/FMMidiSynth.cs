using System;
using System.Collections;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace IMDM327.EDMBoids
{
    [RequireComponent(typeof(AudioSource))]
    public class FMMidiSynth : MonoBehaviour
    {
        public enum SynthRole
        {
            Drums = 0,
            Bass = 1,
            Pad = 2,
            Lead = 3
        }

        public enum Instrument
        {
            Bass,
            Pad,
            Lead,
            ElectricPiano,
            Bell,
            Pluck,
            Organ,
            Sine
        }

        [Serializable]
        public class VoiceSettings
        {
            public Instrument preset;
            [Range(0.1f, 16f)] public float modulatorRatio;
            [Range(0f, 20f)] public float modulationIndex;
            [Range(0f, 1f)] public float amplitude;
            [Range(0.001f, 2f)] public float attackSeconds;
            [Range(0.001f, 4f)] public float decaySeconds;
            [Range(0f, 1f)] public float sustain;
            [Range(0.001f, 4f)] public float releaseSeconds;
            [SerializeField, HideInInspector] Instrument previousPreset;

            public VoiceSettings(Instrument instrument)
            {
                SetPreset(instrument);
            }

            public void UpdatePreset()
            {
                if (preset != previousPreset) SetPreset(preset);
            }

            public void SetPreset(Instrument instrument)
            {
                preset = instrument;
                previousPreset = instrument;
                if (instrument == Instrument.Bass) SetValues(1f, 2.8f, 0.34f, 0.004f, 0.10f, 0.52f, 0.12f);
                if (instrument == Instrument.Pad) SetValues(2f, 1.15f, 0.13f, 0.16f, 0.45f, 0.72f, 0.80f);
                if (instrument == Instrument.Lead) SetValues(2.01f, 4.8f, 0.18f, 0.006f, 0.08f, 0.58f, 0.18f);
                if (instrument == Instrument.ElectricPiano) SetValues(2f, 2.4f, 0.22f, 0.003f, 0.65f, 0.18f, 0.35f);
                if (instrument == Instrument.Bell) SetValues(3.5f, 5f, 0.18f, 0.002f, 1.8f, 0f, 1.2f);
                if (instrument == Instrument.Pluck) SetValues(1f, 3.5f, 0.24f, 0.002f, 0.22f, 0f, 0.12f);
                if (instrument == Instrument.Organ) SetValues(2f, 0.7f, 0.18f, 0.012f, 0.05f, 0.95f, 0.12f);
                if (instrument == Instrument.Sine) SetValues(1f, 0f, 0.22f, 0.01f, 0.08f, 0.8f, 0.25f);
            }

            void SetValues(float ratio, float index, float gain, float attack, float decay, float level, float release)
            {
                modulatorRatio = ratio;
                modulationIndex = index;
                amplitude = gain;
                attackSeconds = attack;
                decaySeconds = decay;
                sustain = level;
                releaseSeconds = release;
            }
        }

        [Serializable]
        public class DrumSettings
        {
            [Range(0f, 1f)] public float amplitude = 1f;
            [Range(0.25f, 4f)] public float pitch = 1f;
            [Range(0f, 3f)] public float modulationIndex = 1f;
            [Range(0.1f, 4f)] public float modulatorRatio = 1f;
            [Range(0.1f, 3f)] public float decay = 1f;
        }

        struct Parameters
        {
            public float ratio, index, gain, attack, decay, sustain, release, pitch;
        }

        struct Voice
        {
            public bool active, held;
            public int track, note;
            public SynthRole role;
            public double frequency, carrierPhase, modulatorPhase;
            public float envelope, releaseStep, ageSeconds, velocity, pan;
        }
        public string midiRelativePath = "EDMBoidsDemo/demo_edm.mid";
        public bool autoPlay = true;
        public bool loop = true;
        [Range(0f, 1f)] public float masterGain = 0.65f;
        [Range(0f, 2f)] public float interactionAmount = 1f;
        public bool boidsAffectAudio = true;
        public VoiceSettings bass = new VoiceSettings(Instrument.Bass);
        public VoiceSettings pad = new VoiceSettings(Instrument.Pad);
        public VoiceSettings lead = new VoiceSettings(Instrument.Lead);
        public DrumSettings drums = new DrumSettings();
        readonly object parameterLock = new object();
        readonly Parameters[] parameters = new Parameters[4];
        readonly Parameters[] audioParameters = new Parameters[4];
        const int MaxVoices = 64;
        const double TwoPi = Math.PI * 2.0;
        readonly Voice[] voices = new Voice[MaxVoices];
        readonly int[] muteRole = new int[4];
        // MIDI note state is shared with the audio thread.
        readonly int[] heldNotes = new int[4 * 128];
        readonly int[] noteVelocities = new int[4 * 128];
        readonly int[] noteAttacks = new int[4 * 128];

        public float ConsumeNoteAttack(int role, int note)
        {
            if (role < 0 || role > 3 || note < 0 || note > 127) return 0f;
            int velocity = Interlocked.Exchange(ref noteAttacks[role * 128 + note], 0);
            return !IsMuted(role) && transportPlaying ? velocity / 127f : 0f;
        }

        public float HeldNoteVelocity(int role, int note)
        {
            if (role < 0 || role > 3 || note < 0 || note > 127 || IsMuted(role) || !transportPlaying) return 0f;
            int index = role * 128 + note;
            return Volatile.Read(ref heldNotes[index]) > 0 ? Volatile.Read(ref noteVelocities[index]) / 127f : 0f;
        }

        MidiSongData song;
        SynthRole[] rolesByTrack = Array.Empty<SynthRole>();
        AudioSource source;
        int sampleRate, nextEvent;
        long songSample;
        volatile bool transportPlaying;
        int toggleRequested, restartRequested, kickTrigger;
        volatile float drumEnergy, bassEnergy, padEnergy, leadEnergy;
        readonly float[] targetSpread = new float[4], targetSpeed = new float[4], targetDisorder = new float[4], targetPan = new float[4];
        readonly float[] spread = new float[4], speed = new float[4], disorder = new float[4], pan = new float[4];
        readonly float[] brightness = { 1f, 1f, 1f, 1f }, movementPan = new float[4];

        float currentReverbWet = 0.18f, currentCutoff = 10000f;

        float lowpassL, lowpassR;
        SimpleStereoReverb reverb;

        public float GetEnergy(int role)
        {
            if (role == 0) return drumEnergy;
            if (role == 1) return bassEnergy;
            if (role == 2) return padEnergy;
            if (role == 3) return leadEnergy;
            return 0f;
        }

        void Awake()
        {
            sampleRate = AudioSettings.outputSampleRate;
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            AudioClip silent = AudioClip.Create("EDMBoids_Silent", sampleRate, 1, sampleRate, false);
            source.clip = silent;
            reverb = new SimpleStereoReverb(sampleRate);
            UpdateParameters();
        }

        void OnValidate()
        {
            UpdateParameters();
        }

        void UpdateParameters()
        {
            if (bass == null) bass = new VoiceSettings(Instrument.Bass);
            if (pad == null) pad = new VoiceSettings(Instrument.Pad);
            if (lead == null) lead = new VoiceSettings(Instrument.Lead);
            if (drums == null) drums = new DrumSettings();
            bass.UpdatePreset();
            pad.UpdatePreset();
            lead.UpdatePreset();
            // Copy Inspector values once per audio buffer, as in SoundFMPiano.
            lock (parameterLock)
            {
                parameters[0] = new Parameters
                {
                    gain = Mathf.Clamp01(drums.amplitude),
                    pitch = Mathf.Clamp(drums.pitch, 0.25f, 4f),
                    index = Mathf.Clamp(drums.modulationIndex, 0f, 3f),
                    ratio = Mathf.Clamp(drums.modulatorRatio, 0.1f, 4f),
                    decay = Mathf.Clamp(drums.decay, 0.1f, 3f)
                };
                parameters[1] = ReadParameters(bass);
                parameters[2] = ReadParameters(pad);
                parameters[3] = ReadParameters(lead);
            }
        }

        Parameters ReadParameters(VoiceSettings settings)
        {
            return new Parameters
            {
                ratio = Mathf.Clamp(settings.modulatorRatio, 0.1f, 16f),
                index = Mathf.Clamp(settings.modulationIndex, 0f, 20f),
                gain = Mathf.Clamp01(settings.amplitude),
                attack = Mathf.Clamp(settings.attackSeconds, 0.001f, 2f),
                decay = Mathf.Clamp(settings.decaySeconds, 0.001f, 4f),
                sustain = Mathf.Clamp01(settings.sustain),
                release = Mathf.Clamp(settings.releaseSeconds, 0.001f, 4f)
            };
        }

        IEnumerator Start()
        {
            source.Play();
            yield return LoadMidi();
        }

        IEnumerator LoadMidi()
        {
            string path = Path.Combine(Application.streamingAssetsPath, midiRelativePath).Replace("\\", "/");
            if (!path.Contains("://")) path = "file://" + path;

            using (UnityWebRequest request = UnityWebRequest.Get(path))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("EDM Boids MIDI load failed: " + request.error + "\n" + path);
                    yield break;
                }

                try
                {
                    song = SimpleMidiFile.Parse(request.downloadHandler.data);
                    BuildTrackRoles();
                    Debug.Log($"Loaded MIDI: {midiRelativePath} | {song.events.Length} note events | {song.initialBpm:0.#} BPM");
                    if (autoPlay) Restart();
                }
                catch (Exception e)
                {
                    Debug.LogError("Could not parse MIDI: " + e);
                }
            }
        }

        void BuildTrackRoles()
        {
            rolesByTrack = new SynthRole[song.trackNames.Length];
            for (int i = 0; i < rolesByTrack.Length; i++)
            {
                string n = song.trackNames[i].ToLowerInvariant();
                if (n.Contains("drum") || n.Contains("kick") || n.Contains("perc")) rolesByTrack[i] = SynthRole.Drums;
                else if (n.Contains("bass")) rolesByTrack[i] = SynthRole.Bass;
                else if (n.Contains("pad") || n.Contains("chord") || n.Contains("string")) rolesByTrack[i] = SynthRole.Pad;
                else if (n.Contains("lead") || n.Contains("melody") || n.Contains("arp")) rolesByTrack[i] = SynthRole.Lead;
                else
                {
                    int cycle = i % 3;
                    if (cycle == 0) rolesByTrack[i] = SynthRole.Lead;
                    else if (cycle == 1) rolesByTrack[i] = SynthRole.Bass;
                    else rolesByTrack[i] = SynthRole.Pad;
                }
            }
        }

        void Update()
        {
            UpdateParameters();
            if (Input.GetKeyDown(KeyCode.Space)) TogglePlayPause();
            if (Input.GetKeyDown(KeyCode.R)) Restart();
            if (Input.GetKeyDown(KeyCode.Alpha1)) ToggleMute(0);
            if (Input.GetKeyDown(KeyCode.Alpha2)) ToggleMute(1);
            if (Input.GetKeyDown(KeyCode.Alpha3)) ToggleMute(2);
            if (Input.GetKeyDown(KeyCode.Alpha4)) ToggleMute(3);
            if (Input.GetKeyDown(KeyCode.B)) boidsAffectAudio = !boidsAffectAudio;
        }

        public void TogglePlayPause()
        {
            Interlocked.Exchange(ref toggleRequested, 1);
        }

        public void Restart()
        {
            Interlocked.Exchange(ref restartRequested, 1);
        }

        public void ToggleMute(int role)
        {
            if (role >= 0 && role < 4) muteRole[role] = 1 - muteRole[role];
        }

        public bool IsMuted(int role)
        {
            return role >= 0 && role < 4 && muteRole[role] != 0;
        }

        public void SetBoidMetrics(int group, float spread, float speed, float disorder, float pan)
        {
            lock (parameterLock)
            {
                targetSpread[group] = spread;
                targetSpeed[group] = speed;
                targetDisorder[group] = disorder;
                targetPan[group] = pan;
            }
        }

        public float ConsumeKickStrength()
        {
            int velocity = Interlocked.Exchange(ref kickTrigger, 0);
            return !IsMuted(0) && transportPlaying ? velocity / 127f : 0f;
        }

        void ResetTransport(bool startPlaying)
        {
            nextEvent = 0;
            songSample = 0;
            Array.Clear(voices, 0, voices.Length);
            for (int i = 0; i < heldNotes.Length; i++)
            {
                Volatile.Write(ref heldNotes[i], 0);
                Volatile.Write(ref noteVelocities[i], 0);
                Interlocked.Exchange(ref noteAttacks[i], 0);
            }
            Interlocked.Exchange(ref kickTrigger, 0);
            reverb.Clear();
            lowpassL = lowpassR = 0f;
            transportPlaying = startPlaying;
        }

        // Fill the audio buffer.
        void OnAudioFilterRead(float[] data, int channels)
        {
            if (reverb == null || sampleRate <= 0 || channels <= 0)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }
            lock (parameterLock)
            {
                for (int i = 0; i < parameters.Length; i++)
                {
                    audioParameters[i] = parameters[i];
                    spread[i] = targetSpread[i];
                    speed[i] = targetSpeed[i];
                    disorder[i] = targetDisorder[i];
                    pan[i] = targetPan[i];
                }
            }
            float amount = boidsAffectAudio ? Mathf.Clamp(interactionAmount, 0f, 2f) : 0f;
            float averageSpread = 0f, averageSpeed = 0f;
            for (int g = 0; g < 4; g++)
            {
                averageSpread += spread[g] * 0.25f;
                averageSpeed += speed[g] * 0.25f;
            }
            float reverbTarget = Mathf.Clamp(Mathf.LerpUnclamped(0.18f, Mathf.Lerp(0.02f, 0.78f, averageSpread), amount), 0f, 0.85f);
            float cutoffTarget = Mathf.Pow(2f, Mathf.LerpUnclamped(Mathf.Log(10000f, 2f),
                Mathf.Lerp(Mathf.Log(350f, 2f), Mathf.Log(18000f, 2f), averageSpeed), amount));
            cutoffTarget = Mathf.Clamp(cutoffTarget, 200f, 18000f);
            float smoothing = 1f - Mathf.Exp(-1f / (sampleRate * 0.06f));
            if (Interlocked.Exchange(ref restartRequested, 0) == 1) ResetTransport(true);
            if (Interlocked.Exchange(ref toggleRequested, 0) == 1 && song != null) transportPlaying = !transportPlaying;

            double drumAbs = 0, bassAbs = 0, padAbs = 0, leadAbs = 0;
            int frameCount = Math.Max(1, data.Length / Math.Max(1, channels));

            for (int i = 0; i < data.Length; i += channels)
            {
                if (song != null && transportPlaying)
                {
                    double now = songSample / (double)sampleRate;
                    while (nextEvent < song.events.Length && song.events[nextEvent].timeSeconds <= now)
                    {
                        ProcessMidiEvent(song.events[nextEvent]);
                        nextEvent++;
                    }
                    if (now >= song.lengthSeconds)
                    {
                        if (loop) ResetTransport(true);
                        else transportPlaying = false;
                    }
                }

                currentReverbWet += (reverbTarget - currentReverbWet) * smoothing;
                currentCutoff += (cutoffTarget - currentCutoff) * smoothing;
                for (int g = 0; g < 4; g++)
                {
                    float movement = Mathf.Clamp01(speed[g] * 0.75f + disorder[g] * 0.25f);
                    float target = Mathf.Clamp(Mathf.LerpUnclamped(1f, Mathf.Lerp(0.15f, 3.5f, movement), amount), 0.1f, 5f);
                    brightness[g] += (target - brightness[g]) * smoothing;
                    movementPan[g] += (pan[g] * amount - movementPan[g]) * smoothing;
                }

                double drumsL = 0, drumsR = 0, bassL = 0, bassR = 0, padL = 0, padR = 0, leadL = 0, leadR = 0;
                if (transportPlaying)
                {
                    for (int v = 0; v < voices.Length; v++)
                    {
                        if (!voices[v].active) continue;
                        Voice voice = voices[v];
                        int role = (int)voice.role;
                        float s = RenderVoice(ref voice, brightness[role], audioParameters[role]);
                        voices[v] = voice;
                        float voicePan = Mathf.Clamp(voice.pan + movementPan[role], -0.95f, 0.95f);
                        float left = s * Mathf.Sqrt(0.5f * (1f - voicePan));
                        float right = s * Mathf.Sqrt(0.5f * (1f + voicePan));
                        switch (voice.role)
                        {
                            case SynthRole.Drums:
                                drumsL += left;
                                drumsR += right;
                                break;
                            case SynthRole.Bass:
                                bassL += left;
                                bassR += right;
                                break;
                            case SynthRole.Pad:
                                padL += left;
                                padR += right;
                                break;
                            case SynthRole.Lead:
                                leadL += left;
                                leadR += right;
                                break;
                        }
                    }
                }

                if (muteRole[0] != 0) drumsL = drumsR = 0;
                if (muteRole[1] != 0) bassL = bassR = 0;
                if (muteRole[2] != 0) padL = padR = 0;
                if (muteRole[3] != 0) leadL = leadR = 0;

                drumAbs += Math.Abs(drumsL) + Math.Abs(drumsR);
                bassAbs += Math.Abs(bassL) + Math.Abs(bassR);
                padAbs += Math.Abs(padL) + Math.Abs(padR);
                leadAbs += Math.Abs(leadL) + Math.Abs(leadR);

                float dryL = (float)(drumsL + bassL + padL + leadL);
                float dryR = (float)(drumsR + bassR + padR + leadR);
                float alpha = 1f - Mathf.Exp(-2f * Mathf.PI * currentCutoff / sampleRate);
                lowpassL += alpha * (dryL - lowpassL);
                lowpassR += alpha * (dryR - lowpassR);
                reverb.Process(lowpassL, lowpassR, currentReverbWet, out float wetL, out float wetR);
                float outputL = (float)Math.Tanh(wetL * masterGain * 1.35f);
                float outputR = (float)Math.Tanh(wetR * masterGain * 1.35f);

                if (channels == 1) data[i] = (outputL + outputR) * 0.5f;
                else
                {
                    data[i] = outputL;
                    data[i + 1] = outputR;
                    for (int c = 2; c < channels; c++) data[i + c] = (outputL + outputR) * 0.5f;
                }
                if (transportPlaying) songSample++;
            }

            float scale = 0.5f / frameCount;
            drumEnergy = SmoothMeter(drumEnergy, (float)(drumAbs * scale * 2.5));
            bassEnergy = SmoothMeter(bassEnergy, (float)(bassAbs * scale * 2.5));
            padEnergy = SmoothMeter(padEnergy, (float)(padAbs * scale * 3.0));
            leadEnergy = SmoothMeter(leadEnergy, (float)(leadAbs * scale * 3.0));
        }

        float SmoothMeter(float previous, float next)
        {
            next = Mathf.Clamp01(next);
            return next > previous ? Mathf.Lerp(previous, next, 0.55f) : Mathf.Lerp(previous, next, 0.12f);
        }

        void ProcessMidiEvent(MidiScheduledEvent e)
        {
            SynthRole role = RoleFor(e.track, e.channel);
            if (e.noteOn)
            {
                int index = (int)role * 128 + e.note;
                Volatile.Write(ref noteVelocities[index], e.velocity);
                if (role != SynthRole.Drums) Interlocked.Increment(ref heldNotes[index]);
                Interlocked.Exchange(ref noteAttacks[index], e.velocity);
                if (role == SynthRole.Drums && (e.note == 35 || e.note == 36) && !IsMuted(0))
                    Interlocked.Exchange(ref kickTrigger, e.velocity);
                StartVoice(e.track, e.note, e.velocity, role);
            }
            else if (role != SynthRole.Drums)
            {
                int index = (int)role * 128 + e.note;
                if (Volatile.Read(ref heldNotes[index]) > 0) Interlocked.Decrement(ref heldNotes[index]);
                ReleaseVoice(e.track, e.note);
            }
        }

        SynthRole RoleFor(int track, int channel)
        {
            if (channel == 9) return SynthRole.Drums;
            if (track >= 0 && track < rolesByTrack.Length) return rolesByTrack[track];
            return SynthRole.Lead;
        }

        void StartVoice(int track, int note, int velocity, SynthRole role)
        {
            int slot = -1;
            float quietest = float.MaxValue;
            for (int i = 0; i < voices.Length; i++)
            {
                if (!voices[i].active)
                {
                    slot = i;
                    break;
                }
                if (voices[i].envelope < quietest)
                {
                    quietest = voices[i].envelope;
                    slot = i;
                }
            }

            double frequency = 440.0 * Math.Pow(2.0, (note - 69) / 12.0);
            float pan = 0f;
            if (role == SynthRole.Pad) pan = Mathf.Clamp((note % 12 - 5.5f) / 9f, -0.65f, 0.65f);
            if (role == SynthRole.Lead) pan = Mathf.Clamp((note - 72) / 24f, -0.55f, 0.55f);
            if (role == SynthRole.Drums && note >= 42) pan = (note % 2 == 0) ? -0.35f : 0.35f;

            voices[slot] = new Voice
            {
                active = true, held = role != SynthRole.Drums, track = track, note = note, role = role,
                frequency = frequency, carrierPhase = 0, modulatorPhase = 0,
                envelope = role == SynthRole.Drums ? 1f : 0f, releaseStep = 0f,
                ageSeconds = 0f, velocity = Mathf.Clamp01(velocity / 127f), pan = pan
            };
        }

        void ReleaseVoice(int track, int note)
        {
            for (int i = 0; i < voices.Length; i++)
            {
                if (!voices[i].active || voices[i].track != track || voices[i].note != note) continue;
                Voice v = voices[i];
                v.held = false;
                v.releaseStep = v.envelope / Mathf.Max(1f, audioParameters[(int)v.role].release * sampleRate);
                voices[i] = v;
            }
        }

        float RenderVoice(ref Voice v, float brightness, Parameters settings)
        {
            v.ageSeconds += 1f / sampleRate;
            if (v.role == SynthRole.Drums) return RenderDrum(ref v, brightness, settings);
            if (v.held)
            {
                if (v.ageSeconds < settings.attack)
                    v.envelope = v.ageSeconds / settings.attack;
                else
                    v.envelope = Mathf.Lerp(1f, settings.sustain,
                        Mathf.Clamp01((v.ageSeconds - settings.attack) / settings.decay));
            }
            else
            {
                if (v.releaseStep <= 0f) v.releaseStep = v.envelope / Mathf.Max(1f, settings.release * sampleRate);
                v.envelope = Mathf.Max(0f, v.envelope - v.releaseStep);
                if (v.envelope <= 0.0001f)
                {
                    v.active = false;
                    return 0f;
                }
            }

            float ratio = settings.ratio;
            float index = settings.index * brightness;
            float gain = settings.gain;
            float s = (float)Math.Sin(v.carrierPhase + index * Math.Sin(v.modulatorPhase)) * v.envelope * v.velocity * gain;
            v.carrierPhase = (v.carrierPhase + TwoPi * v.frequency / sampleRate) % TwoPi;
            v.modulatorPhase = (v.modulatorPhase + TwoPi * v.frequency * ratio / sampleRate) % TwoPi;
            return s;
        }

        float RenderDrum(ref Voice v, float brightness, Parameters settings)
        {
            float t = v.ageSeconds / settings.decay;
            float s;
            brightness *= settings.index;
            if (v.note == 35 || v.note == 36)
            {
                float env = Mathf.Exp(-t * 15f);
                double freq = (48.0 + 120.0 * Math.Exp(-t * 28.0)) * settings.pitch;
                s = (float)Math.Sin(v.carrierPhase + 5.5 * brightness * env * Math.Sin(v.modulatorPhase)) * env * v.velocity * 0.95f;
                v.carrierPhase = (v.carrierPhase + TwoPi * freq / sampleRate) % TwoPi;
                v.modulatorPhase = (v.modulatorPhase + TwoPi * freq * 1.5 * settings.ratio / sampleRate) % TwoPi;
                if (t > 0.55f) v.active = false;
            }
            else if (v.note == 38 || v.note == 40)
            {
                float env = Mathf.Exp(-t * 22f);
                double f = 185.0 * settings.pitch;
                s = (float)Math.Sin(v.carrierPhase + 13.0 * brightness * env * Math.Sin(v.modulatorPhase)) * env * v.velocity * 0.48f;
                v.carrierPhase = (v.carrierPhase + TwoPi * f / sampleRate) % TwoPi;
                v.modulatorPhase = (v.modulatorPhase + TwoPi * f * 7.13 * settings.ratio / sampleRate) % TwoPi;
                if (t > 0.32f) v.active = false;
            }
            else
            {
                float env = Mathf.Exp(-t * 42f);
                double f = (390.0 + (v.note % 5) * 37.0) * settings.pitch;
                s = (float)Math.Sin(v.carrierPhase + 20.0 * brightness * Math.Sin(v.modulatorPhase)) * env * v.velocity * 0.22f;
                v.carrierPhase = (v.carrierPhase + TwoPi * f / sampleRate) % TwoPi;
                v.modulatorPhase = (v.modulatorPhase + TwoPi * f * 13.71 * settings.ratio / sampleRate) % TwoPi;
                if (t > 0.16f) v.active = false;
            }
            return s * settings.gain;
        }

        sealed class SimpleStereoReverb
        {
            readonly float[][] delay = new float[4][];
            readonly int[] index = new int[4];
            readonly float[] dampingState = new float[4];

            public SimpleStereoReverb(int sr)
            {
                delay[0] = new float[Mathf.Max(8, Mathf.RoundToInt(sr * 0.071f))];
                delay[1] = new float[Mathf.Max(8, Mathf.RoundToInt(sr * 0.089f))];
                delay[2] = new float[Mathf.Max(8, Mathf.RoundToInt(sr * 0.113f))];
                delay[3] = new float[Mathf.Max(8, Mathf.RoundToInt(sr * 0.127f))];
            }

            public void Clear()
            {
                for (int i = 0; i < delay.Length; i++)
                {
                    Array.Clear(delay[i], 0, delay[i].Length);
                    index[i] = 0;
                    dampingState[i] = 0f;
                }
            }

            public void Process(float inL, float inR, float wet, out float outL, out float outR)
            {
                float feedback = Mathf.Lerp(0.48f, 0.76f, wet);
                float damping = Mathf.Lerp(0.18f, 0.55f, wet);
                float a = Comb(0, inL + inR * 0.15f, feedback, damping);
                float b = Comb(1, inR + inL * 0.15f, feedback, damping);
                float c = Comb(2, inL + inR * 0.08f, feedback * 0.94f, damping);
                float d = Comb(3, inR + inL * 0.08f, feedback * 0.94f, damping);
                outL = Mathf.Lerp(inL, (a + c) * 0.48f, wet);
                outR = Mathf.Lerp(inR, (b + d) * 0.48f, wet);
            }

            float Comb(int i, float input, float feedback, float damping)
            {
                float delayed = delay[i][index[i]];
                dampingState[i] = delayed * (1f - damping) + dampingState[i] * damping;
                delay[i][index[i]] = input + dampingState[i] * feedback;
                if (++index[i] >= delay[i].Length) index[i] = 0;
                return delayed;
            }
        }
    }
}
