namespace DynamicEngramSimulator;

/// <summary>One temporally distinct component of an acquisition experience.</summary>
public sealed record EngramEncodingPhase(
    string Name,
    MemoryStimulus Stimulus,
    TimeSpan Offset,
    double Importance = 1,
    int? EngramSize = null);

/// <summary>One encoded phase and its overlap with every earlier phase.</summary>
public sealed record EngramPhaseResult(
    EngramEncodingPhase Phase,
    EngramSnapshot Engram,
    IReadOnlyDictionary<Guid, double> PriorPopulationOverlaps);

/// <summary>Distinct ensembles recruited across one multi-phase experience.</summary>
public sealed record EngramExperienceResult(
    string Label,
    DateTimeOffset StartedAt,
    IReadOnlyList<EngramPhaseResult> Phases);

/// <summary>Encodes context, salient-event, and post-event phases without collapsing their identities.</summary>
public static class EngramExperienceEncoder
{
    /// <summary>Encodes chronologically ordered phases into the supplied simulation.</summary>
    public static EngramExperienceResult Encode(
        EngramSimulation simulation,
        string label,
        DateTimeOffset startedAt,
        IEnumerable<EngramEncodingPhase> phases)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(phases);
        EngramEncodingPhase[] ordered = phases.OrderBy(phase => phase.Offset).ToArray();
        if (startedAt == default || ordered.Length == 0 || ordered.Any(phase =>
                string.IsNullOrWhiteSpace(phase.Name) || phase.Stimulus is null || phase.Offset < TimeSpan.Zero))
            throw new ArgumentException("An experience requires valid non-negative phases.", nameof(phases));

        var results = new List<EngramPhaseResult>(ordered.Length);
        foreach (EngramEncodingPhase phase in ordered)
        {
            EngramSnapshot snapshot = simulation.Encode(
                new EngramEncodingRequest(
                    $"{label}:{phase.Name}",
                    phase.Stimulus,
                    phase.EngramSize,
                    phase.Importance),
                startedAt + phase.Offset);
            var overlap = results.ToDictionary(
                result => result.Engram.MemoryId,
                result => Jaccard(result.Engram.NeuronIds, snapshot.NeuronIds));
            results.Add(new EngramPhaseResult(phase, snapshot, overlap));
        }

        return new EngramExperienceResult(label.Trim(), startedAt.ToUniversalTime(), results);
    }

    private static double Jaccard(IReadOnlyList<int> first, IReadOnlyList<int> second)
    {
        var left = first.ToHashSet();
        int intersection = left.Intersect(second).Count();
        int union = left.Union(second).Count();
        return union == 0 ? 1 : intersection / (double)union;
    }
}
