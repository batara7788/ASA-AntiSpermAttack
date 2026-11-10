namespace Asa.Core;

public enum Timbre
{
    Brass,
    Bell,
    Alarm,
    Soft
}

public sealed record Note(double Frequency, double StartSeconds, double DurationSeconds, double Volume, Timbre Timbre);

public static class SoundSynthesizer
{
    public const int SampleRate = 44100;

    public static byte[] CreateStartupFanfare()
    {
        var notes = new List<Note>
        {
            new(Frequency("G4"), 0.00, 0.22, 0.55, Timbre.Brass),
            new(Frequency("C5"), 0.16, 0.22, 0.55, Timbre.Brass),
            new(Frequency("E5"), 0.32, 0.22, 0.55, Timbre.Brass),
            new(Frequency("G5"), 0.48, 0.30, 0.6, Timbre.Brass),
            new(Frequency("C5"), 0.70, 1.30, 0.42, Timbre.Brass),
            new(Frequency("E5"), 0.70, 1.30, 0.38, Timbre.Brass),
            new(Frequency("G5"), 0.70, 1.30, 0.38, Timbre.Brass),
            new(Frequency("C6"), 0.70, 1.30, 0.34, Timbre.Brass),
            new(Frequency("C3"), 0.70, 1.30, 0.40, Timbre.Soft),
            new(Frequency("C7"), 0.72, 1.50, 0.12, Timbre.Bell)
        };
        return Render(notes, 2.6);
    }

    public static byte[] CreateThreatAlarm()
    {
        var notes = new List<Note>();
        for (int repeat = 0; repeat < 3; repeat++)
        {
            double start = repeat * 0.42;
            notes.Add(new Note(Frequency("A5"), start, 0.18, 0.5, Timbre.Alarm));
            notes.Add(new Note(Frequency("E5"), start + 0.2, 0.18, 0.5, Timbre.Alarm));
        }

        notes.Add(new Note(Frequency("A2"), 0, 1.3, 0.35, Timbre.Soft));
        return Render(notes, 1.6);
    }

    public static byte[] CreateScanCompleteChime()
    {
        var notes = new List<Note>
        {
            new(Frequency("E6"), 0.00, 1.2, 0.45, Timbre.Bell),
            new(Frequency("B5"), 0.14, 1.2, 0.40, Timbre.Bell),
            new(Frequency("G#5"), 0.28, 1.6, 0.40, Timbre.Bell),
            new(Frequency("E5"), 0.28, 1.6, 0.25, Timbre.Soft)
        };
        return Render(notes, 2.2);
    }

    public static byte[] CreateDatabaseUpdatedChime()
    {
        var notes = new List<Note>
        {
            new(Frequency("C6"), 0.00, 0.9, 0.38, Timbre.Bell),
            new(Frequency("G6"), 0.12, 1.4, 0.34, Timbre.Bell),
            new(Frequency("C5"), 0.12, 1.2, 0.20, Timbre.Soft)
        };
        return Render(notes, 1.8);
    }

    public static double Frequency(string noteName)
    {
        string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        string pitch = noteName.Substring(0, noteName.Length - 1);
        int octave = int.Parse(noteName.Substring(noteName.Length - 1));
        int semitone = Array.IndexOf(names, pitch);
        if (semitone < 0)
        {
            throw new ArgumentException("Неизвестная нота: " + noteName);
        }

        int midiNumber = (octave + 1) * 12 + semitone;
        return 440.0 * Math.Pow(2, (midiNumber - 69) / 12.0);
    }

    public static byte[] Render(IEnumerable<Note> notes, double totalSeconds)
    {
        int sampleCount = (int)(totalSeconds * SampleRate);
        var mix = new double[sampleCount];

        foreach (Note note in notes)
        {
            int startSample = (int)(note.StartSeconds * SampleRate);
            int tailSamples = (int)((note.DurationSeconds + ReleaseSeconds(note.Timbre)) * SampleRate);
            for (int offset = 0; offset < tailSamples && startSample + offset < sampleCount; offset++)
            {
                double time = offset / (double)SampleRate;
                mix[startSample + offset] += note.Volume * Envelope(note, time) * Wave(note, time);
            }
        }

        ApplyReverb(mix);
        Normalize(mix, 0.8);
        return EncodeWav(mix);
    }

    public static byte[] EncodeWav(double[] samples)
    {
        int dataSize = samples.Length * 2;
        using var memory = new MemoryStream(44 + dataSize);
        using var writer = new BinaryWriter(memory);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);
        foreach (double sample in samples)
        {
            double clamped = Math.Clamp(sample, -1, 1);
            writer.Write((short)(clamped * short.MaxValue));
        }

        writer.Flush();
        return memory.ToArray();
    }

    private static double ReleaseSeconds(Timbre timbre) => timbre switch
    {
        Timbre.Bell => 1.2,
        Timbre.Brass => 0.35,
        Timbre.Alarm => 0.05,
        _ => 0.5
    };

    private static double Envelope(Note note, double time)
    {
        double attack = note.Timbre switch
        {
            Timbre.Bell => 0.004,
            Timbre.Brass => 0.045,
            Timbre.Alarm => 0.01,
            _ => 0.12
        };

        if (time < attack)
        {
            return time / attack;
        }

        if (note.Timbre == Timbre.Bell)
        {
            return Math.Exp(-(time - attack) * 3.2);
        }

        double sustainLevel = note.Timbre == Timbre.Brass ? 0.78 : 1.0;
        double decay = note.Timbre == Timbre.Brass ? Math.Max(sustainLevel, 1 - (time - attack) * 2.5) : 1.0;
        if (time <= note.DurationSeconds)
        {
            return decay;
        }

        double release = ReleaseSeconds(note.Timbre);
        double releaseProgress = (time - note.DurationSeconds) / release;
        return releaseProgress >= 1 ? 0 : decay * (1 - releaseProgress) * (1 - releaseProgress);
    }

    private static double Wave(Note note, double time)
    {
        double phase = 2 * Math.PI * note.Frequency * time;
        switch (note.Timbre)
        {
            case Timbre.Bell:
                return Math.Sin(phase)
                    + 0.5 * Math.Sin(phase * 2.756) * Math.Exp(-time * 4)
                    + 0.28 * Math.Sin(phase * 5.404) * Math.Exp(-time * 7)
                    + 0.12 * Math.Sin(phase * 8.933) * Math.Exp(-time * 10);
            case Timbre.Brass:
                double vibrato = 1 + 0.004 * Math.Sin(2 * Math.PI * 5.5 * time) * Math.Min(1, time * 3);
                double brassPhase = phase * vibrato;
                return Math.Sin(brassPhase)
                    + 0.62 * Math.Sin(brassPhase * 2)
                    + 0.45 * Math.Sin(brassPhase * 3)
                    + 0.28 * Math.Sin(brassPhase * 4)
                    + 0.16 * Math.Sin(brassPhase * 5)
                    + 0.08 * Math.Sin(brassPhase * 6);
            case Timbre.Alarm:
                return 0.7 * Math.Sin(phase) + 0.3 * Math.Sin(phase * 3) + 0.15 * Math.Sin(phase * 5);
            default:
                return Math.Sin(phase) + 0.25 * Math.Sin(phase * 2);
        }
    }

    private static void ApplyReverb(double[] samples)
    {
        int[] delays = { (int)(0.0297 * SampleRate), (int)(0.0371 * SampleRate), (int)(0.0411 * SampleRate), (int)(0.0437 * SampleRate) };
        double[] feedback = { 0.42, 0.38, 0.35, 0.32 };
        var wet = new double[samples.Length];
        for (int line = 0; line < delays.Length; line++)
        {
            var buffer = new double[samples.Length];
            for (int index = 0; index < samples.Length; index++)
            {
                double delayed = index >= delays[line] ? buffer[index - delays[line]] : 0;
                buffer[index] = samples[index] + delayed * feedback[line];
                wet[index] += delayed;
            }
        }

        for (int index = 0; index < samples.Length; index++)
        {
            samples[index] = samples[index] * 0.82 + wet[index] * 0.12;
        }
    }

    private static void Normalize(double[] samples, double peak)
    {
        double maximum = samples.Select(Math.Abs).DefaultIfEmpty(0).Max();
        if (maximum <= 0)
        {
            return;
        }

        double factor = peak / maximum;
        for (int index = 0; index < samples.Length; index++)
        {
            samples[index] *= factor;
        }

        int fadeSamples = Math.Min(samples.Length, SampleRate / 20);
        for (int index = 0; index < fadeSamples; index++)
        {
            samples[samples.Length - 1 - index] *= index / (double)fadeSamples;
        }
    }
}
