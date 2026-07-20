namespace DynamicEngramSimulator;

/// <summary>Deterministic helpers for sparse and related experimental stimuli.</summary>
public static class StimulusFactory
{
    /// <summary>Creates a sparse stimulus with a fixed number of strongly driven channels.</summary>
    public static MemoryStimulus Sparse(
        int neuronCount,
        int activeChannels,
        ulong seed,
        double activeDrive = 1,
        double backgroundDrive = 0.02)
    {
        if (neuronCount < 1) throw new ArgumentOutOfRangeException(nameof(neuronCount));
        if (activeChannels is < 1 || activeChannels > neuronCount) throw new ArgumentOutOfRangeException(nameof(activeChannels));
        Probability(activeDrive, nameof(activeDrive));
        Probability(backgroundDrive, nameof(backgroundDrive));
        if (activeDrive <= backgroundDrive) throw new ArgumentException("Active drive must exceed background drive.");

        var values = Enumerable.Repeat(backgroundDrive, neuronCount).ToArray();
        foreach (int index in Enumerable.Range(0, neuronCount)
                     .OrderBy(index => Hash(seed, (ulong)index))
                     .Take(activeChannels))
        {
            values[index] = activeDrive;
        }

        return new MemoryStimulus(values);
    }

    /// <summary>Creates a related stimulus by retaining a fraction of the source's strongly driven channels.</summary>
    public static MemoryStimulus Related(MemoryStimulus source, double retainedFraction, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(source);
        Probability(retainedFraction, nameof(retainedFraction));
        double[] values = source.ToArray();
        int activeCount = Math.Max(1, values.Count(value => value >= 0.5));
        int retainedCount = (int)Math.Round(activeCount * retainedFraction, MidpointRounding.AwayFromZero);
        int[] active = Enumerable.Range(0, values.Length)
            .Where(index => values[index] >= 0.5)
            .OrderBy(index => Hash(seed, (ulong)index))
            .ToArray();
        int[] inactive = Enumerable.Range(0, values.Length)
            .Where(index => values[index] < 0.5)
            .OrderBy(index => Hash(seed ^ 0x9E3779B97F4A7C15UL, (ulong)index))
            .ToArray();
        double high = active.Length == 0 ? 1 : active.Max(index => values[index]);
        double low = inactive.Length == 0 ? 0 : inactive.Min(index => values[index]);

        foreach (int index in active.Skip(retainedCount)) values[index] = low;
        foreach (int index in inactive.Take(activeCount - retainedCount)) values[index] = high;
        return new MemoryStimulus(values);
    }

    private static ulong Hash(ulong seed, ulong value)
    {
        ulong mixed = seed + (value * 0x9E3779B97F4A7C15UL);
        mixed = (mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9UL;
        mixed = (mixed ^ (mixed >> 27)) * 0x94D049BB133111EBUL;
        return mixed ^ (mixed >> 31);
    }

    private static void Probability(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1) throw new ArgumentOutOfRangeException(name);
    }
}
