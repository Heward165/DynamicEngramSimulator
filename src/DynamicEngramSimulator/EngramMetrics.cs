namespace DynamicEngramSimulator;

/// <summary>Population and behavior metrics for dynamic engram experiments.</summary>
public static class EngramMetrics
{
    /// <summary>
    /// Compares two population snapshots using Jaccard identity and two recall outcomes
    /// using bounded activation stability.
    /// </summary>
    public static EngramComparison Compare(
        EngramSnapshot before,
        EngramSnapshot after,
        RecallResult beforeRecall,
        RecallResult afterRecall)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(beforeRecall);
        ArgumentNullException.ThrowIfNull(afterRecall);
        if (before.MemoryId != after.MemoryId
            || before.MemoryId != beforeRecall.ExpectedMemoryId
            || before.MemoryId != afterRecall.ExpectedMemoryId)
            throw new ArgumentException("All observations must refer to the same memory.");

        var first = before.NeuronIds.ToHashSet();
        var second = after.NeuronIds.ToHashSet();
        int retained = first.Intersect(second).Count();
        int union = first.Union(second).Count();
        double identity = union == 0 ? 1 : retained / (double)union;
        double behavioralStability = 1 - Math.Abs(
            beforeRecall.ExpectedActivation - afterRecall.ExpectedActivation);
        return new EngramComparison(
            identity,
            Math.Clamp(behavioralStability, 0, 1),
            retained,
            second.Except(first).Count(),
            first.Except(second).Count());
    }

    /// <summary>
    /// Compares weighted population representations across checkpoints. This separates
    /// cell-identity turnover from preservation of stimulus-aligned information.
    /// </summary>
    public static EngramRepresentationComparison CompareRepresentations(
        DynamicEngramState before,
        DynamicEngramState after,
        Guid memoryId)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        MemoryEngramState first = before.Memories.SingleOrDefault(memory => memory.MemoryId == memoryId)
            ?? throw new KeyNotFoundException($"Memory '{memoryId}' is absent from the first checkpoint.");
        MemoryEngramState second = after.Memories.SingleOrDefault(memory => memory.MemoryId == memoryId)
            ?? throw new KeyNotFoundException($"Memory '{memoryId}' is absent from the second checkpoint.");
        if (first.Stimulus.Length != second.Stimulus.Length)
            throw new ArgumentException("Checkpoint dimensions differ.");

        double[] left = Dense(first, first.Stimulus.Length);
        double[] right = Dense(second, second.Stimulus.Length);
        double cosine = Cosine(left, right);
        double identity = Jaccard(first.Members.Select(member => member.NeuronId),
            second.Members.Select(member => member.NeuronId));
        double beforeAlignment = Cosine(left, first.Stimulus);
        double afterAlignment = Cosine(right, second.Stimulus);
        return new EngramRepresentationComparison(
            identity,
            cosine,
            beforeAlignment,
            afterAlignment,
            afterAlignment - beforeAlignment);
    }

    private static double[] Dense(MemoryEngramState memory, int count)
    {
        var values = new double[count];
        foreach (EngramMemberState member in memory.Members) values[member.NeuronId] = member.Strength;
        return values;
    }

    private static double Cosine(IReadOnlyList<double> first, IReadOnlyList<double> second)
    {
        double dot = 0;
        double left = 0;
        double right = 0;
        for (int index = 0; index < first.Count; index++)
        {
            dot += first[index] * second[index];
            left += first[index] * first[index];
            right += second[index] * second[index];
        }

        return left == 0 || right == 0 ? 0 : dot / Math.Sqrt(left * right);
    }

    private static double Jaccard(IEnumerable<int> first, IEnumerable<int> second)
    {
        var left = first.ToHashSet();
        int intersection = left.Intersect(second).Count();
        int union = left.Union(second).Count();
        return union == 0 ? 1 : intersection / (double)union;
    }
}
