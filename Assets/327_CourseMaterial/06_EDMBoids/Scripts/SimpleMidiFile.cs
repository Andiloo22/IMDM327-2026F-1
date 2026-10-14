using System;
using System.Collections.Generic;
using System.Text;

namespace IMDM327.EDMBoids
{
    public struct MidiScheduledEvent
    {
        public double timeSeconds;
        public int track;
        public int channel;
        public int note;
        public int velocity;
        public bool noteOn;
    }

    public sealed class MidiSongData
    {
        public MidiScheduledEvent[] events = Array.Empty<MidiScheduledEvent>();
        public string[] trackNames = Array.Empty<string>();
        public double lengthSeconds;
        public float initialBpm = 120f;
    }

    public static class SimpleMidiFile
    {
        struct RawNote
        {
            public long tick;
            public int track, channel, note, velocity;
            public bool noteOn;
        }

        struct RawTempo
        {
            public long tick;
            public int microsecondsPerQuarter;
        }

        struct TempoSegment
        {
            public long tick;
            public double seconds;
            public int microsecondsPerQuarter;
        }

        public static MidiSongData Parse(byte[] data)
        {
            if (data == null || data.Length < 14)
                throw new ArgumentException("Not a valid MIDI file.");

            int pos = 0;
            if (ReadFourCC(data, ref pos) != "MThd")
                throw new ArgumentException("Missing MIDI MThd header.");

            int headerLength = ReadInt32BE(data, ref pos);
            if (headerLength < 6 || pos + headerLength > data.Length)
                throw new ArgumentException("Invalid MIDI header length.");

            int format = ReadInt16BE(data, ref pos);
            int trackCount = ReadInt16BE(data, ref pos);
            int division = ReadInt16BE(data, ref pos);

            if ((division & 0x8000) != 0)
                throw new NotSupportedException("SMPTE-time MIDI files are not supported in this teaching demo.");
            if (format != 0 && format != 1)
                throw new NotSupportedException("Only Standard MIDI File format 0 and 1 are supported.");

            pos = 8 + headerLength;

            var notes = new List<RawNote>(4096);
            // Default 120 BPM is handled by BuildTempoMap.
            // Keep this list for actual tempo events only, so a MIDI tempo at tick 0 wins deterministically.
            var tempos = new List<RawTempo>(16);
            var trackNames = new string[Math.Max(1, trackCount)];

            for (int track = 0; track < trackCount; track++)
            {
                if (pos + 8 > data.Length || ReadFourCC(data, ref pos) != "MTrk")
                    throw new ArgumentException("Missing MIDI MTrk chunk.");

                int trackLength = ReadInt32BE(data, ref pos);
                int end = Math.Min(data.Length, pos + trackLength);
                long tick = 0;
                int runningStatus = 0;

                while (pos < end)
                {
                    tick += ReadVariableLength(data, ref pos, end);
                    if (pos >= end) break;

                    int status = data[pos] & 0xFF;
                    if (status < 0x80)
                    {
                        if (runningStatus == 0)
                            throw new ArgumentException("Invalid MIDI running status.");
                        status = runningStatus;
                    }
                    else
                    {
                        pos++;
                        if (status < 0xF0)
                            runningStatus = status;
                    }

                    if (status == 0xFF)
                    {
                        if (pos >= end) break;
                        int metaType = data[pos++] & 0xFF;
                        int length = (int)ReadVariableLength(data, ref pos, end);
                        int metaEnd = Math.Min(end, pos + length);

                        if (metaType == 0x03)
                            trackNames[track] = Encoding.UTF8.GetString(data, pos, metaEnd - pos);
                        else if (metaType == 0x51 && metaEnd - pos >= 3)
                        {
                            int us = (data[pos] << 16) | (data[pos + 1] << 8) | data[pos + 2];
                            tempos.Add(new RawTempo { tick = tick, microsecondsPerQuarter = us });
                        }

                        pos = metaEnd;
                        if (metaType == 0x2F) break;
                        continue;
                    }

                    if (status == 0xF0 || status == 0xF7)
                    {
                        int length = (int)ReadVariableLength(data, ref pos, end);
                        pos = Math.Min(end, pos + length);
                        continue;
                    }

                    int command = status & 0xF0;
                    int channel = status & 0x0F;
                    int d1, d2;

                    switch (command)
                    {
                        case 0x80:
                        case 0x90:
                            d1 = ReadByte(data, ref pos, end);
                            d2 = ReadByte(data, ref pos, end);
                            notes.Add(new RawNote
                            {
                                tick = tick,
                                track = track,
                                channel = channel,
                                note = d1,
                                velocity = d2,
                                noteOn = command == 0x90 && d2 > 0
                            });
                            break;
                        case 0xA0:
                        case 0xB0:
                        case 0xE0:
                            ReadByte(data, ref pos, end);
                            ReadByte(data, ref pos, end);
                            break;
                        case 0xC0:
                        case 0xD0:
                            ReadByte(data, ref pos, end);
                            break;
                        default:
                            throw new ArgumentException("Unsupported MIDI status byte: 0x" + status.ToString("X2"));
                    }
                }

                pos = end;
            }

            var tempoSegments = BuildTempoMap(tempos, division);
            var scheduled = new List<MidiScheduledEvent>(notes.Count);
            double maxTime = 0.0;

            notes.Sort((a, b) =>
            {
                int t = a.tick.CompareTo(b.tick);
                if (t != 0) return t;
                if (a.noteOn != b.noteOn) return a.noteOn ? 1 : -1;
                return a.track.CompareTo(b.track);
            });

            foreach (RawNote note in notes)
            {
                double time = TickToSeconds(note.tick, tempoSegments, division);
                maxTime = Math.Max(maxTime, time);
                scheduled.Add(new MidiScheduledEvent
                {
                    timeSeconds = time,
                    track = note.track,
                    channel = note.channel,
                    note = note.note,
                    velocity = note.velocity,
                    noteOn = note.noteOn
                });
            }

            float initialBpm = 120f;
            for (int i = tempos.Count - 1; i >= 0; i--)
                if (tempos[i].tick == 0)
                {
                    initialBpm = 60000000f / tempos[i].microsecondsPerQuarter;
                    break;
                }

            for (int i = 0; i < trackNames.Length; i++)
                if (string.IsNullOrWhiteSpace(trackNames[i]))
                    trackNames[i] = "Track " + i;

            return new MidiSongData
            {
                events = scheduled.ToArray(),
                trackNames = trackNames,
                lengthSeconds = maxTime + 1.0,
                initialBpm = initialBpm
            };
        }

        static List<TempoSegment> BuildTempoMap(List<RawTempo> tempos, int division)
        {
            tempos.Sort((a, b) => a.tick.CompareTo(b.tick));
            var segments = new List<TempoSegment>(tempos.Count + 1);
            long lastTick = 0;
            int currentTempo = 500000;
            double seconds = 0.0;
            segments.Add(new TempoSegment { tick = 0, seconds = 0.0, microsecondsPerQuarter = currentTempo });

            foreach (RawTempo tempo in tempos)
            {
                if (tempo.tick < lastTick) continue;
                seconds += (tempo.tick - lastTick) * currentTempo / (division * 1000000.0);
                currentTempo = tempo.microsecondsPerQuarter;
                lastTick = tempo.tick;
                var segment = new TempoSegment { tick = tempo.tick, seconds = seconds, microsecondsPerQuarter = currentTempo };
                if (segments.Count > 0 && segments[segments.Count - 1].tick == tempo.tick)
                    segments[segments.Count - 1] = segment;
                else
                    segments.Add(segment);
            }
            return segments;
        }

        static double TickToSeconds(long tick, List<TempoSegment> segments, int division)
        {
            TempoSegment segment = segments[0];
            for (int i = 1; i < segments.Count; i++)
            {
                if (segments[i].tick > tick) break;
                segment = segments[i];
            }
            return segment.seconds + (tick - segment.tick) * segment.microsecondsPerQuarter / (division * 1000000.0);
        }

        static string ReadFourCC(byte[] data, ref int pos)
        {
            if (pos + 4 > data.Length) return "";
            string value = Encoding.ASCII.GetString(data, pos, 4);
            pos += 4;
            return value;
        }

        static int ReadInt32BE(byte[] data, ref int pos)
        {
            if (pos + 4 > data.Length) throw new ArgumentException("Unexpected end of MIDI file.");
            int value = (data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3];
            pos += 4;
            return value;
        }

        static int ReadInt16BE(byte[] data, ref int pos)
        {
            if (pos + 2 > data.Length) throw new ArgumentException("Unexpected end of MIDI file.");
            int value = (data[pos] << 8) | data[pos + 1];
            pos += 2;
            return value;
        }

        static long ReadVariableLength(byte[] data, ref int pos, int end)
        {
            long value = 0;
            int count = 0;
            while (pos < end && count++ < 4)
            {
                int b = data[pos++];
                value = (value << 7) | (uint)(b & 0x7F);
                if ((b & 0x80) == 0) return value;
            }
            return value;
        }

        static int ReadByte(byte[] data, ref int pos, int end)
        {
            if (pos >= end) throw new ArgumentException("Unexpected end of MIDI track.");
            return data[pos++] & 0xFF;
        }
    }
}
