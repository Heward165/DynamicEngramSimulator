namespace DynamicEngramSimulator;

/// <summary>Named regions in the explicit systems-consolidation circuit.</summary>
public enum EngramRegion
{
    /// <summary>Sparse input separation and neurogenesis-sensitive allocation.</summary>
    DentateGyrus,
    /// <summary>Recurrent hippocampal completion.</summary>
    CA3,
    /// <summary>Hippocampal output representation.</summary>
    CA1,
    /// <summary>Slowly consolidating distributed representation.</summary>
    Cortex
}

/// <summary>Alternative mechanism families that can be compared under one protocol.</summary>
public enum ConsolidationModelFamily
{
    /// <summary>Activity-dependent strengthening and passive decay.</summary>
    Hebbian,
    /// <summary>Hebbian change with stronger normalization toward a bounded target.</summary>
    Homeostatic,
    /// <summary>Recent traces receive an eligibility advantage during replay.</summary>
    SynapticTagging,
    /// <summary>Cortical consolidation depends primarily on offline replay.</summary>
    ReplayDependent,
    /// <summary>Dentate-gyrus membership is actively replaced and rewired.</summary>
    NeurogenesisRewiring
}

/// <summary>Direction in which a stored regional path is replayed.</summary>
public enum ReplayDirection
{
    /// <summary>Dentate gyrus to cortex.</summary>
    Forward,
    /// <summary>Cortex to dentate gyrus.</summary>
    Reverse
}

/// <summary>Configuration for one modeled region.</summary>
public sealed record EngramRegionDefinition(
    EngramRegion Region,
    int NeuronCount,
    double EngramFraction,
    double PlasticityRate,
    double ConsolidationTimeConstantDays);

/// <summary>Configuration for an explicit DG→CA3→CA1→cortex circuit.</summary>
public sealed record SystemsConsolidationOptions
{
    /// <summary>Four unique regions in circuit order.</summary>
    public IReadOnlyList<EngramRegionDefinition> Regions { get; init; } =
    [
        new(EngramRegion.DentateGyrus, 512, 0.04, 0.65, 21),
        new(EngramRegion.CA3, 256, 0.08, 0.60, 30),
        new(EngramRegion.CA1, 256, 0.10, 0.55, 45),
        new(EngramRegion.Cortex, 512, 0.12, 0.25, 120)
    ];

    /// <summary>Mechanism family used for a run.</summary>
    public ConsolidationModelFamily ModelFamily { get; init; } = ConsolidationModelFamily.ReplayDependent;

    /// <summary>Directed synapses created per source member between adjacent regions.</summary>
    public int SynapticFanOut { get; init; } = 2;

    /// <summary>Daily hippocampal strength loss before replay.</summary>
    public double HippocampalDecayPerDay { get; init; } = 0.012;

    /// <summary>Daily cortex growth that occurs without replay.</summary>
    public double BaselineCorticalConsolidationPerDay { get; init; } = 0.004;

    /// <summary>Expected daily dentate-gyrus population replacement.</summary>
    public double NeurogenesisFractionPerDay { get; init; } = 0.002;

    /// <summary>Deterministic simulation seed.</summary>
    public ulong RandomSeed { get; init; } = 0x53595354454D53UL;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Regions);
        if (Regions.Count != 4 || Regions.Select(region => region.Region).Distinct().Count() != 4 ||
            Regions.Select(region => region.Region).ToArray() is not
                [EngramRegion.DentateGyrus, EngramRegion.CA3, EngramRegion.CA1, EngramRegion.Cortex])
            throw new ArgumentException("Regions must contain DG, CA3, CA1, and cortex exactly once in circuit order.", nameof(Regions));
        foreach (EngramRegionDefinition region in Regions)
        {
            if (region.NeuronCount < 8 || !double.IsFinite(region.EngramFraction) ||
                region.EngramFraction is <= 0 or > 0.5 || !double.IsFinite(region.PlasticityRate) ||
                region.PlasticityRate is <= 0 or > 1 || !double.IsFinite(region.ConsolidationTimeConstantDays) ||
                region.ConsolidationTimeConstantDays <= 0)
                throw new ArgumentException("A region definition is invalid.", nameof(Regions));
        }

        if (!Enum.IsDefined(ModelFamily) || SynapticFanOut is < 1 or > 64 ||
            !Probability(HippocampalDecayPerDay) || !Probability(BaselineCorticalConsolidationPerDay) ||
            !Probability(NeurogenesisFractionPerDay))
            throw new ArgumentOutOfRangeException(nameof(SystemsConsolidationOptions));
    }

    private static bool Probability(double value) => double.IsFinite(value) && value is >= 0 and <= 1;
}

/// <summary>A bounded contextual feature vector used for encoding and probing.</summary>
public sealed class SystemsMemoryContext
{
    private readonly double[] values;

    /// <summary>Creates a context with at least four bounded features.</summary>
    public SystemsMemoryContext(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        this.values = values.ToArray();
        if (this.values.Length < 4 || this.values.Any(value => !double.IsFinite(value) || value is < 0 or > 1))
            throw new ArgumentException("Context features must be finite, bounded, and contain at least four values.", nameof(values));
    }

    /// <summary>Number of context features.</summary>
    public int Count => values.Length;

    /// <summary>Gets one feature.</summary>
    public double this[int index] => values[index];

    internal double[] Copy() => values.ToArray();

    /// <summary>Creates a deterministic related context by replacing a fraction of features.</summary>
    public static SystemsMemoryContext Related(SystemsMemoryContext source, double retainedFraction, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!double.IsFinite(retainedFraction) || retainedFraction is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(retainedFraction));
        double[] result = source.Copy();
        int retained = (int)Math.Round(result.Length * retainedFraction);
        ulong state = seed;
        int[] order = Enumerable.Range(0, result.Length).OrderBy(_ => Next(ref state)).ToArray();
        for (int index = retained; index < order.Length; index++)
            result[order[index]] = (Next(ref state) >> 11) * (1.0 / (1UL << 53));
        return new(result);
    }

    private static ulong Next(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        ulong value = state;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}

/// <summary>Request to encode one event.</summary>
public sealed record SystemsMemoryRequest(string Label, SystemsMemoryContext Context);

/// <summary>One region's current engram population.</summary>
public sealed record RegionalEngramSnapshot(
    EngramRegion Region,
    IReadOnlyList<int> NeuronIds,
    double MeanStrength,
    double PopulationIdentity);

/// <summary>One explicit directed connection between modeled regional populations.</summary>
public sealed record EngramSynapseSnapshot(
    EngramRegion SourceRegion,
    int SourceNeuronId,
    EngramRegion TargetRegion,
    int TargetNeuronId,
    double Weight);

/// <summary>Inspectable cell and synapse state for one memory.</summary>
public sealed record SystemsMemorySnapshot(
    Guid MemoryId,
    string Label,
    DateTimeOffset EncodedAt,
    IReadOnlyList<RegionalEngramSnapshot> Regions,
    IReadOnlyList<EngramSynapseSnapshot> Synapses,
    double SynapticIdentity);

/// <summary>One requested replay and its queue priority.</summary>
public sealed record SleepReplayRequest(Guid MemoryId, double Priority = 1, ReplayDirection Direction = ReplayDirection.Forward);

/// <summary>Controls a deterministic NREM-like replay epoch.</summary>
public sealed record SleepEpochPlan(
    IReadOnlyList<SleepReplayRequest> Queue,
    int MaximumReplays = 8,
    double InterruptionFraction = 0,
    double PlasticityGain = 0.08);

/// <summary>Result of one sleep epoch, including requests skipped by interruption or capacity.</summary>
public sealed record SleepEpochResult(
    int RequestedReplays,
    int CompletedReplays,
    IReadOnlyList<Guid> ReplayedMemories,
    IReadOnlyList<Guid> SkippedMemories);

/// <summary>Recall, generalization, false-positive, identity, and regional-dependence metrics.</summary>
public sealed record SystemsRecallProbe(
    Guid MemoryId,
    DateTimeOffset ProbedAt,
    double ExactContextRecall,
    double RelatedContextGeneralization,
    double FalsePositiveRecall,
    double MemoryIdentity,
    double SynapticIdentity,
    double HippocampalInvolvement,
    double CorticalInvolvement);

/// <summary>
/// Deterministic multi-region simulator that keeps cell allocation, directed synapses,
/// systems consolidation, replay, and neurogenesis as separate inspectable mechanisms.
/// </summary>
public sealed class SystemsConsolidationSimulator
{
    private readonly SystemsConsolidationOptions options;
    private readonly Dictionary<Guid, MutableSystemsMemory> memories = [];
    private ulong randomState;

    /// <summary>Creates a simulator at a fixed UTC-capable instant.</summary>
    public SystemsConsolidationSimulator(DateTimeOffset startedAt, SystemsConsolidationOptions? options = null)
    {
        if (startedAt == default)
            throw new ArgumentOutOfRangeException(nameof(startedAt));
        this.options = options ?? new SystemsConsolidationOptions();
        this.options.Validate();
        randomState = this.options.RandomSeed;
        CurrentTime = startedAt.ToUniversalTime();
    }

    /// <summary>Current simulation time.</summary>
    public DateTimeOffset CurrentTime { get; private set; }

    /// <summary>Encodes sparse populations and directed synapses across every region.</summary>
    public SystemsMemorySnapshot Encode(SystemsMemoryRequest request, DateTimeOffset encodedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Label);
        ArgumentNullException.ThrowIfNull(request.Context);
        AdvanceTo(encodedAt);
        Guid id = NextGuid();
        var populations = new Dictionary<EngramRegion, Dictionary<int, double>>();
        var originals = new Dictionary<EngramRegion, HashSet<int>>();
        foreach (EngramRegionDefinition region in options.Regions)
        {
            int count = Math.Max(2, (int)Math.Round(region.NeuronCount * region.EngramFraction));
            int regionSalt = (int)region.Region * 10_000;
            int[] selected = Enumerable.Range(0, region.NeuronCount)
                .Select(neuron => (Neuron: neuron, Score: AllocationScore(request.Context, neuron, regionSalt)))
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Neuron)
                .Take(count)
                .Select(candidate => candidate.Neuron)
                .ToArray();
            populations[region.Region] = selected.ToDictionary(neuron => neuron, _ => region.PlasticityRate);
            originals[region.Region] = selected.ToHashSet();
        }

        Dictionary<SynapseKey, double> synapses = BuildSynapses(populations);
        var memory = new MutableSystemsMemory(
            id,
            request.Label.Trim(),
            encodedAt.ToUniversalTime(),
            request.Context.Copy(),
            populations,
            originals,
            synapses,
            synapses.Keys.ToHashSet());
        memories.Add(id, memory);
        return Snapshot(memory);
    }

    /// <summary>Advances passive consolidation, decay, and optional neurogenesis in one-day steps.</summary>
    public void AdvanceTo(DateTimeOffset target)
    {
        target = target.ToUniversalTime();
        if (target < CurrentTime)
            throw new ArgumentOutOfRangeException(nameof(target));
        while ((target - CurrentTime).TotalDays >= 1)
        {
            CurrentTime = CurrentTime.AddDays(1);
            foreach (MutableSystemsMemory memory in memories.Values.OrderBy(memory => memory.Id))
                AdvanceOneDay(memory);
        }

        CurrentTime = target;
    }

    /// <summary>Executes a prioritized, capacity-limited, interruptible NREM-like replay queue.</summary>
    public SleepEpochResult RunSleepEpoch(SleepEpochPlan plan, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(plan.Queue);
        if (plan.MaximumReplays < 1 || !double.IsFinite(plan.InterruptionFraction) ||
            plan.InterruptionFraction is < 0 or > 1 || !double.IsFinite(plan.PlasticityGain) ||
            plan.PlasticityGain is <= 0 or > 1 || plan.Queue.Any(request =>
                request.MemoryId == Guid.Empty || !double.IsFinite(request.Priority) || request.Priority < 0 ||
                !Enum.IsDefined(request.Direction)))
            throw new ArgumentOutOfRangeException(nameof(plan));
        AdvanceTo(at);

        SleepReplayRequest[] ordered = plan.Queue
            .OrderByDescending(request => request.Priority)
            .ThenBy(request => request.MemoryId)
            .ToArray();
        int capacity = Math.Min(plan.MaximumReplays,
            (int)Math.Floor(ordered.Length * (1 - plan.InterruptionFraction)));
        SleepReplayRequest[] selected = ordered.Take(capacity).ToArray();
        foreach (SleepReplayRequest request in selected)
            Replay(GetMemory(request.MemoryId), request.Direction, plan.PlasticityGain);
        return new(
            ordered.Length,
            selected.Length,
            selected.Select(request => request.MemoryId).ToArray(),
            ordered.Skip(capacity).Select(request => request.MemoryId).ToArray());
    }

    /// <summary>Probes exact recall, related-context generalization, and competitor activation.</summary>
    public SystemsRecallProbe Probe(
        Guid memoryId,
        SystemsMemoryContext exactContext,
        SystemsMemoryContext relatedContext,
        DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(exactContext);
        ArgumentNullException.ThrowIfNull(relatedContext);
        MutableSystemsMemory memory = GetMemory(memoryId);
        if (exactContext.Count != memory.Context.Length || relatedContext.Count != memory.Context.Length)
            throw new ArgumentException("Probe context dimensions must match the encoded context.");
        AdvanceTo(at);

        double hippocampal = new[] { EngramRegion.DentateGyrus, EngramRegion.CA3, EngramRegion.CA1 }
            .Average(region => MeanStrength(memory.Populations[region]));
        double cortex = MeanStrength(memory.Populations[EngramRegion.Cortex]);
        double exactSimilarity = Cosine(memory.Context, exactContext.Copy());
        double relatedSimilarity = Cosine(memory.Context, relatedContext.Copy());
        double exact = Math.Clamp(exactSimilarity * ((0.72 * hippocampal) + (0.28 * cortex)), 0, 1);
        double generalization = Math.Clamp(Math.Sqrt(relatedSimilarity) *
            ((0.20 * hippocampal) + (0.80 * cortex)), 0, 1);
        double falsePositive = memories.Values
            .Where(other => other.Id != memoryId)
            .Select(other => Cosine(other.Context, exactContext.Copy()) * 0.35 *
                other.Populations.Values.Average(MeanStrength))
            .DefaultIfEmpty(0)
            .Max();
        return new(
            memoryId,
            CurrentTime,
            exact,
            generalization,
            Math.Clamp(falsePositive, 0, 1),
            PopulationIdentity(memory),
            SynapticIdentity(memory),
            hippocampal,
            cortex);
    }

    /// <summary>Returns current cells and synapses for a memory.</summary>
    public SystemsMemorySnapshot GetMemorySnapshot(Guid memoryId) => Snapshot(GetMemory(memoryId));

    private void AdvanceOneDay(MutableSystemsMemory memory)
    {
        foreach (EngramRegion region in new[] { EngramRegion.DentateGyrus, EngramRegion.CA3, EngramRegion.CA1 })
        {
            Dictionary<int, double> population = memory.Populations[region];
            foreach (int neuron in population.Keys.ToArray())
                population[neuron] *= 1 - options.HippocampalDecayPerDay;
        }

        Dictionary<int, double> cortex = memory.Populations[EngramRegion.Cortex];
        double baseline = options.ModelFamily == ConsolidationModelFamily.ReplayDependent
            ? options.BaselineCorticalConsolidationPerDay * 0.25
            : options.BaselineCorticalConsolidationPerDay;
        foreach (int neuron in cortex.Keys.ToArray())
            cortex[neuron] += baseline * (1 - cortex[neuron]);

        if (options.ModelFamily == ConsolidationModelFamily.Homeostatic)
            Normalize(memory);
        if (options.ModelFamily == ConsolidationModelFamily.NeurogenesisRewiring || options.NeurogenesisFractionPerDay > 0)
            RewireDentateGyrus(memory);
    }

    private void Replay(MutableSystemsMemory memory, ReplayDirection direction, double gain)
    {
        EngramRegion[] order = direction == ReplayDirection.Forward
            ? [EngramRegion.DentateGyrus, EngramRegion.CA3, EngramRegion.CA1, EngramRegion.Cortex]
            : [EngramRegion.Cortex, EngramRegion.CA1, EngramRegion.CA3, EngramRegion.DentateGyrus];
        double ageDays = Math.Max(0, (CurrentTime - memory.EncodedAt).TotalDays);
        double familyGain = options.ModelFamily switch
        {
            ConsolidationModelFamily.SynapticTagging when ageDays <= 3 => 1.6,
            ConsolidationModelFamily.ReplayDependent => 1.4,
            ConsolidationModelFamily.Homeostatic => 0.75,
            _ => 1
        };
        for (int index = 0; index < order.Length; index++)
        {
            EngramRegion region = order[index];
            EngramRegionDefinition definition = options.Regions.Single(item => item.Region == region);
            // Replay drives slow cortical transfer while only modestly refreshing
            // the hippocampal path; otherwise remote recall never shifts systems.
            double regionalMultiplier = region == EngramRegion.Cortex ? 1.8 : 0.05 + (0.02 * index);
            double regionGain = gain * familyGain * definition.PlasticityRate * regionalMultiplier;
            foreach (int neuron in memory.Populations[region].Keys.ToArray())
            {
                double current = memory.Populations[region][neuron];
                memory.Populations[region][neuron] = Math.Clamp(current + (regionGain * (1 - current)), 0, 1);
            }
        }

        foreach (SynapseKey edge in memory.Synapses.Keys.ToArray())
            memory.Synapses[edge] = Math.Clamp(memory.Synapses[edge] + (gain * familyGain * (1 - memory.Synapses[edge])), 0, 1);
        if (options.ModelFamily == ConsolidationModelFamily.Homeostatic)
            Normalize(memory);
    }

    private void RewireDentateGyrus(MutableSystemsMemory memory)
    {
        EngramRegionDefinition definition = options.Regions.Single(region => region.Region == EngramRegion.DentateGyrus);
        Dictionary<int, double> population = memory.Populations[EngramRegion.DentateGyrus];
        double rate = options.ModelFamily == ConsolidationModelFamily.NeurogenesisRewiring
            ? Math.Max(0.01, options.NeurogenesisFractionPerDay)
            : options.NeurogenesisFractionPerDay;
        int replacements = Math.Min(population.Count,
            (int)Math.Floor(population.Count * rate + NextDouble()));
        if (replacements == 0)
            return;
        int[] removed = population.OrderBy(member => member.Value).ThenBy(member => member.Key)
            .Take(replacements).Select(member => member.Key).ToArray();
        int[] added = Enumerable.Range(0, definition.NeuronCount)
            .Where(neuron => !population.ContainsKey(neuron))
            .Select(neuron => (Neuron: neuron, Score: AllocationScore(new SystemsMemoryContext(memory.Context), neuron,
                50_000 + CurrentTime.DayOfYear)))
            .OrderByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.Neuron)
            .Take(replacements).Select(candidate => candidate.Neuron).ToArray();
        for (int index = 0; index < added.Length; index++)
        {
            double inherited = population[removed[index]] * 0.9;
            population.Remove(removed[index]);
            population.Add(added[index], inherited);
            RewireEdges(memory, removed[index], added[index]);
        }
    }

    private static void RewireEdges(MutableSystemsMemory memory, int removed, int added)
    {
        foreach (SynapseKey edge in memory.Synapses.Keys.Where(edge =>
                     (edge.SourceRegion == EngramRegion.DentateGyrus && edge.SourceNeuron == removed) ||
                     (edge.TargetRegion == EngramRegion.DentateGyrus && edge.TargetNeuron == removed)).ToArray())
        {
            double weight = memory.Synapses[edge] * 0.9;
            memory.Synapses.Remove(edge);
            var replacement = edge with
            {
                SourceNeuron = edge.SourceRegion == EngramRegion.DentateGyrus ? added : edge.SourceNeuron,
                TargetNeuron = edge.TargetRegion == EngramRegion.DentateGyrus ? added : edge.TargetNeuron
            };
            memory.Synapses[replacement] = weight;
        }
    }

    private Dictionary<SynapseKey, double> BuildSynapses(
        IReadOnlyDictionary<EngramRegion, Dictionary<int, double>> populations)
    {
        var result = new Dictionary<SynapseKey, double>();
        EngramRegion[] regions = [EngramRegion.DentateGyrus, EngramRegion.CA3, EngramRegion.CA1, EngramRegion.Cortex];
        for (int regionIndex = 0; regionIndex < regions.Length - 1; regionIndex++)
        {
            EngramRegion sourceRegion = regions[regionIndex];
            EngramRegion targetRegion = regions[regionIndex + 1];
            int[] targets = populations[targetRegion].Keys.Order().ToArray();
            foreach (int source in populations[sourceRegion].Keys.Order())
            {
                for (int fan = 0; fan < options.SynapticFanOut; fan++)
                {
                    int target = targets[(source + fan + regionIndex) % targets.Length];
                    var key = new SynapseKey(sourceRegion, source, targetRegion, target);
                    result[key] = (populations[sourceRegion][source] + populations[targetRegion][target]) / 2;
                }
            }
        }

        return result;
    }

    private double AllocationScore(SystemsMemoryContext context, int neuron, int salt) =>
        (0.75 * context[(neuron + salt) % context.Count]) + (0.25 * NextDouble());

    private static void Normalize(MutableSystemsMemory memory)
    {
        foreach (Dictionary<int, double> population in memory.Populations.Values)
        {
            double mean = MeanStrength(population);
            foreach (int neuron in population.Keys.ToArray())
                population[neuron] = Math.Clamp(0.8 * population[neuron] + (0.2 * mean), 0.05, 1);
        }
    }

    private SystemsMemorySnapshot Snapshot(MutableSystemsMemory memory) => new(
        memory.Id,
        memory.Label,
        memory.EncodedAt,
        options.Regions.Select(region => new RegionalEngramSnapshot(
            region.Region,
            memory.Populations[region.Region].Keys.Order().ToArray(),
            MeanStrength(memory.Populations[region.Region]),
            Jaccard(memory.Populations[region.Region].Keys, memory.OriginalPopulations[region.Region]))).ToArray(),
        memory.Synapses.OrderBy(edge => edge.Key.SourceRegion).ThenBy(edge => edge.Key.SourceNeuron)
            .ThenBy(edge => edge.Key.TargetNeuron)
            .Select(edge => new EngramSynapseSnapshot(edge.Key.SourceRegion, edge.Key.SourceNeuron,
                edge.Key.TargetRegion, edge.Key.TargetNeuron, edge.Value)).ToArray(),
        SynapticIdentity(memory));

    private MutableSystemsMemory GetMemory(Guid id) => memories.TryGetValue(id, out MutableSystemsMemory? memory)
        ? memory
        : throw new KeyNotFoundException($"Memory '{id}' was not found.");

    private static double PopulationIdentity(MutableSystemsMemory memory) => memory.Populations
        .Average(pair => Jaccard(pair.Value.Keys, memory.OriginalPopulations[pair.Key]));

    private static double SynapticIdentity(MutableSystemsMemory memory) =>
        Jaccard(memory.Synapses.Keys, memory.OriginalSynapses);

    private static double Jaccard<T>(IEnumerable<T> current, IEnumerable<T> original) where T : notnull
    {
        var left = current.ToHashSet();
        var right = original.ToHashSet();
        int union = left.Union(right).Count();
        return union == 0 ? 1 : left.Intersect(right).Count() / (double)union;
    }

    private static double MeanStrength(IReadOnlyDictionary<int, double> population) =>
        population.Count == 0 ? 0 : population.Values.Average();

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

        return left == 0 || right == 0 ? 0 : Math.Clamp(dot / Math.Sqrt(left * right), 0, 1);
    }

    private ulong NextUInt64()
    {
        randomState += 0x9E3779B97F4A7C15UL;
        ulong value = randomState;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    private double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    private Guid NextGuid()
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes[..8], NextUInt64());
        BitConverter.TryWriteBytes(bytes[8..], NextUInt64());
        return new(bytes);
    }

    private sealed class MutableSystemsMemory(
        Guid id,
        string label,
        DateTimeOffset encodedAt,
        double[] context,
        Dictionary<EngramRegion, Dictionary<int, double>> populations,
        Dictionary<EngramRegion, HashSet<int>> originalPopulations,
        Dictionary<SynapseKey, double> synapses,
        HashSet<SynapseKey> originalSynapses)
    {
        public Guid Id { get; } = id;
        public string Label { get; } = label;
        public DateTimeOffset EncodedAt { get; } = encodedAt;
        public double[] Context { get; } = context;
        public Dictionary<EngramRegion, Dictionary<int, double>> Populations { get; } = populations;
        public Dictionary<EngramRegion, HashSet<int>> OriginalPopulations { get; } = originalPopulations;
        public Dictionary<SynapseKey, double> Synapses { get; } = synapses;
        public HashSet<SynapseKey> OriginalSynapses { get; } = originalSynapses;
    }

    private readonly record struct SynapseKey(
        EngramRegion SourceRegion,
        int SourceNeuron,
        EngramRegion TargetRegion,
        int TargetNeuron);
}

/// <summary>Defines the flagship precision-to-generalization protocol.</summary>
public sealed record SystemsConsolidationExperimentDefinition
{
    /// <summary>Model options.</summary>
    public SystemsConsolidationOptions Options { get; init; } = new();
    /// <summary>Duration of the experiment.</summary>
    public int Days { get; init; } = 42;
    /// <summary>Days at which recall is probed.</summary>
    public IReadOnlyList<int> ProbeDays { get; init; } = [0, 7, 21, 42];
    /// <summary>Whether both memories receive one replay each simulated night.</summary>
    public bool SleepReplayEnabled { get; init; } = true;
    /// <summary>Fraction of each replay queue interrupted.</summary>
    public double ReplayInterruptionFraction { get; init; }
    /// <summary>Context dimension.</summary>
    public int ContextDimensions { get; init; } = 32;
    /// <summary>Fraction of features retained in the related probe.</summary>
    public double RelatedContextRetention { get; init; } = 0.65;
    /// <summary>Protocol seed.</summary>
    public ulong StimulusSeed { get; init; } = 42;
}

/// <summary>One longitudinal observation in the flagship protocol.</summary>
public sealed record SystemsConsolidationObservation(int Day, SystemsRecallProbe Probe);

/// <summary>Complete longitudinal output for one mechanism family.</summary>
public sealed record SystemsConsolidationReport(
    ConsolidationModelFamily ModelFamily,
    bool SleepReplayEnabled,
    IReadOnlyList<SystemsConsolidationObservation> Observations,
    SystemsMemorySnapshot FinalMemory);

/// <summary>Runs matched multi-region, replay, and precision-to-generalization experiments.</summary>
public static class SystemsConsolidationExperiment
{
    /// <summary>Runs one deterministic longitudinal protocol.</summary>
    public static SystemsConsolidationReport Run(
        SystemsConsolidationExperimentDefinition definition,
        DateTimeOffset epoch)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(definition.Options);
        if (epoch == default || definition.Days < 1 || definition.ContextDimensions < 4 ||
            definition.ProbeDays.Count == 0 || definition.ProbeDays.Any(day => day < 0 || day > definition.Days) ||
            !double.IsFinite(definition.RelatedContextRetention) || definition.RelatedContextRetention is < 0 or > 1 ||
            !double.IsFinite(definition.ReplayInterruptionFraction) || definition.ReplayInterruptionFraction is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(definition));

        SystemsMemoryContext exact = RandomContext(definition.ContextDimensions, definition.StimulusSeed);
        SystemsMemoryContext related = SystemsMemoryContext.Related(exact, definition.RelatedContextRetention,
            definition.StimulusSeed + 1);
        SystemsMemoryContext competitor = SystemsMemoryContext.Related(exact, 0.30, definition.StimulusSeed + 2);
        var simulation = new SystemsConsolidationSimulator(epoch, definition.Options);
        SystemsMemorySnapshot target = simulation.Encode(new("precise event", exact), epoch);
        SystemsMemorySnapshot distractor = simulation.Encode(new("related distractor", competitor), epoch.AddMinutes(5));
        var observations = new List<SystemsConsolidationObservation>();

        for (int day = 0; day <= definition.Days; day++)
        {
            DateTimeOffset at = epoch.ToUniversalTime().AddDays(day).AddHours(1);
            simulation.AdvanceTo(at);
            if (day > 0 && definition.SleepReplayEnabled)
            {
                simulation.RunSleepEpoch(new(
                [
                    new(target.MemoryId, 1 + (day / (double)definition.Days), ReplayDirection.Forward),
                    new(distractor.MemoryId, 1, day % 2 == 0 ? ReplayDirection.Reverse : ReplayDirection.Forward)
                ],
                MaximumReplays: 2,
                InterruptionFraction: definition.ReplayInterruptionFraction), at);
            }

            if (definition.ProbeDays.Contains(day))
                observations.Add(new(day, simulation.Probe(target.MemoryId, exact, related, at)));
        }

        return new(definition.Options.ModelFamily, definition.SleepReplayEnabled,
            observations.AsReadOnly(), simulation.GetMemorySnapshot(target.MemoryId));
    }

    /// <summary>Runs every mechanism family under an otherwise matched protocol.</summary>
    public static IReadOnlyList<SystemsConsolidationReport> CompareModelFamilies(
        SystemsConsolidationExperimentDefinition definition,
        DateTimeOffset epoch) => Enum.GetValues<ConsolidationModelFamily>()
        .Select(family => Run(definition with { Options = definition.Options with { ModelFamily = family } }, epoch))
        .ToArray();

    private static SystemsMemoryContext RandomContext(int count, ulong seed)
    {
        var values = new double[count];
        ulong state = seed;
        for (int index = 0; index < values.Length; index++)
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong value = state;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            value ^= value >> 31;
            values[index] = (value >> 11) * (1.0 / (1UL << 53));
        }

        return new(values);
    }
}
