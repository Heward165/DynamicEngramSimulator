using System.Buffers;
using System.Text.Json;

namespace DynamicEngramSimulator;

/// <summary>
/// Simulates sparse engram allocation, recurrent partial-cue recall, inhibitory separation,
/// consolidation, decay, and time-dependent population drift.
/// </summary>
/// <remarks>
/// This is a reproducible computational hypothesis. Its variables are dimensionless model
/// quantities and must not be interpreted as measurements from a biological brain.
/// </remarks>
public sealed class EngramSimulation
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly object sync = new();
    private readonly DynamicEngramOptions options;
    private readonly MutableNeuron[] neurons;
    private readonly Dictionary<Guid, MutableMemory> memories = [];
    private readonly Dictionary<MemoryPair, double> pairInhibition = [];
    private DateTimeOffset currentTime;
    private ulong randomState;
    private long revision;

    /// <summary>Creates a reproducible simulation population.</summary>
    public EngramSimulation(DateTimeOffset initializedAt, DynamicEngramOptions? options = null)
    {
        this.options = options ?? new DynamicEngramOptions();
        this.options.Validate();
        currentTime = RequireTimestamp(initializedAt, nameof(initializedAt));
        randomState = this.options.RandomSeed;
        neurons = new MutableNeuron[this.options.NeuronCount];
        for (int index = 0; index < neurons.Length; index++)
        {
            // Small deterministic heterogeneity makes excitability participate in allocation.
            double excitability = 0.35 + (0.30 * NextDouble());
            double inhibition = 0.02 + (0.04 * NextDouble());
            neurons[index] = new MutableNeuron(index, 0, excitability, inhibition, 0);
        }
    }

    private EngramSimulation(DynamicEngramState state, DynamicEngramOptions? options)
    {
        ArgumentNullException.ThrowIfNull(state);
        state = EngramStateMigrator.Upgrade(state);
        this.options = options ?? state.Options ?? throw new ArgumentException(
            "Version-one and version-two checkpoints omitted simulation options; supply the original options when restoring.",
            nameof(options));
        this.options.Validate();
        ValidateState(state, this.options);
        currentTime = state.CurrentTime.ToUniversalTime();
        randomState = state.RandomState;
        revision = state.Revision;
        neurons = state.Neurons!
            .OrderBy(neuron => neuron.Id)
            .Select(neuron => new MutableNeuron(
                neuron.Id,
                neuron.Activation,
                neuron.Excitability,
                neuron.Inhibition,
                neuron.Consolidation))
            .ToArray();

        foreach (MemoryEngramState memory in state.Memories!)
        {
            var mutable = new MutableMemory(
                memory.MemoryId,
                memory.Label.Trim(),
                memory.EncodedAt.ToUniversalTime(),
                (double[])memory.Stimulus.Clone(),
                memory.Members.ToDictionary(member => member.NeuronId, member => member.Strength),
                memory.Consolidation);
            memories.Add(mutable.Id, mutable);
            foreach ((int neuronId, double strength) in mutable.Members)
                neurons[neuronId].Memberships.Add(mutable.Id, strength);
        }

        foreach (PairInhibitionState pair in state.PairInhibitions!)
            pairInhibition.Add(new MemoryPair(pair.CueMemoryId, pair.CompetitorMemoryId), pair.Strength);
    }

    /// <summary>Current simulated time.</summary>
    public DateTimeOffset CurrentTime { get { lock (sync) return currentTime; } }

    /// <summary>Number of encoded memories.</summary>
    public int MemoryCount { get { lock (sync) return memories.Count; } }

    /// <summary>Monotonic state revision for experiment auditing.</summary>
    public long Revision { get { lock (sync) return revision; } }

    /// <summary>Allocates a sparse neuronal population to a memory.</summary>
    public EngramSnapshot Encode(EngramEncodingRequest request, DateTimeOffset encodedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Stimulus);
        string label = RequireId(request.Label, nameof(request.Label));
        encodedAt = RequireTimestamp(encodedAt, nameof(encodedAt));
        if (request.Stimulus.Count != neurons.Length)
            throw new ArgumentException("Stimulus dimension must equal NeuronCount.", nameof(request));
        int size = request.EngramSize ?? options.DefaultEngramSize;
        if (size is < 2 || size > neurons.Length) throw new ArgumentOutOfRangeException(nameof(request));
        if (!double.IsFinite(request.Importance) || request.Importance is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(request));

        lock (sync)
        {
            if (encodedAt < currentTime) throw new ArgumentException("Encoding cannot precede simulation time.", nameof(encodedAt));
            AdvanceCore(encodedAt);
            Guid memoryId = request.MemoryId ?? NextGuid();
            if (memoryId == Guid.Empty || memories.ContainsKey(memoryId))
                throw new ArgumentException("MemoryId must be non-empty and unique.", nameof(request));

            double[] allocationStimulus = request.Stimulus.ToArray();
            if (options.AllocationControl == EngramAllocationControl.Random)
                Shuffle(allocationStimulus);
            else if (options.AllocationControl == EngramAllocationControl.PermutedStimulus)
                allocationStimulus = Rotate(allocationStimulus, Math.Max(1, allocationStimulus.Length / 2));

            var scored = new (int Id, double Score)[neurons.Length];
            for (int index = 0; index < neurons.Length; index++)
            {
                MutableNeuron neuron = neurons[index];
                double noise = options.AllocationNoise * ((2 * NextDouble()) - 1);
                double excitability = options.Mechanisms.ExcitabilityAllocation &&
                                      options.AllocationControl != EngramAllocationControl.Random
                    ? options.ExcitabilityWeight * neuron.Excitability
                    : 0;
                double stimulus = options.AllocationControl == EngramAllocationControl.Random
                    ? 0
                    : options.StimulusWeight * allocationStimulus[index];
                double score = stimulus
                    + excitability
                    - (0.25 * neuron.Inhibition)
                    + noise;
                scored[index] = (index, score);
            }

            int[] selected = scored
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Id)
                .Take(size)
                .Select(item => item.Id)
                .ToArray();
            var members = new Dictionary<int, double>();
            foreach (int neuronId in selected)
            {
                MutableNeuron neuron = neurons[neuronId];
                double strength = Math.Clamp(
                    options.HebbianStrength * request.Importance * (0.65 + (0.35 * allocationStimulus[neuronId]))
                    + (0.10 * neuron.Excitability),
                    0.05,
                    1);
                members.Add(neuronId, strength);
                neuron.Memberships.Add(memoryId, strength);
                if (options.Mechanisms.ExcitabilityAllocation)
                    neuron.Excitability += options.EncodingExcitabilityBoost
                        * request.Importance
                        * (1 - neuron.Excitability);
                neuron.Consolidation = Math.Clamp(neuron.Consolidation + (0.03 * strength), 0, 1);
            }

            var memory = new MutableMemory(
                memoryId,
                label,
                encodedAt,
                allocationStimulus,
                members,
                0.05 * request.Importance);
            memories.Add(memoryId, memory);
            LearnAutomaticSeparation(memory);
            revision++;
            return SnapshotCore(memory);
        }
    }

    /// <summary>Creates a deterministic partial cue without consuming simulation random state.</summary>
    public RecallCue CreatePartialCue(Guid memoryId, double memberFraction, ulong seed, double drive = 1)
    {
        if (!double.IsFinite(memberFraction) || memberFraction is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(memberFraction));
        if (!double.IsFinite(drive) || drive is <= 0 or > 1) throw new ArgumentOutOfRangeException(nameof(drive));
        lock (sync)
        {
            MutableMemory memory = GetMemory(memoryId);
            int count = Math.Max(1, (int)Math.Ceiling(memory.Members.Count * memberFraction));
            int[] selected = memory.Members.Keys
                .OrderBy(neuronId => Hash(seed, (ulong)neuronId))
                .Take(count)
                .ToArray();
            return new RecallCue(memoryId, selected.ToDictionary(neuronId => neuronId, _ => drive));
        }
    }

    /// <summary>Runs recurrent pattern completion and reports selectivity and distortion.</summary>
    public RecallResult Recall(RecallCue cue, DateTimeOffset recalledAt)
    {
        ArgumentNullException.ThrowIfNull(cue);
        recalledAt = RequireTimestamp(recalledAt, nameof(recalledAt));
        lock (sync)
        {
            MutableMemory expected = GetMemory(cue.ExpectedMemoryId);
            if (recalledAt < currentTime) throw new ArgumentException("Recall cannot precede simulation time.", nameof(recalledAt));
            AdvanceCore(recalledAt);
            foreach ((int neuronId, double drive) in cue.NeuronDrive)
            {
                if ((uint)neuronId >= (uint)neurons.Length)
                    throw new ArgumentOutOfRangeException(nameof(cue), "Cue neuron is outside the simulation.");
                if (!double.IsFinite(drive) || drive is <= 0 or > 1)
                    throw new ArgumentException("Cue drives must be in (0,1].", nameof(cue));
            }

            foreach (MutableNeuron neuron in neurons) neuron.Activation = 0;
            foreach ((int neuronId, double drive) in cue.NeuronDrive) neurons[neuronId].Activation = drive;
            double activationCost = 0;

            double[] next = ArrayPool<double>.Shared.Rent(neurons.Length);
            try
            {
                for (int iteration = 0; iteration < options.RecallIterations; iteration++)
                {
                    Dictionary<Guid, double> support = memories.Values.ToDictionary(
                        memory => memory.Id,
                        memory => WeightedActivation(memory));
                    double global = options.Mechanisms.RecallInhibition
                        ? neurons.Average(neuron => neuron.Activation) * options.GlobalInhibition
                        : 0;

                    foreach (MutableNeuron neuron in neurons)
                    {
                        double recurrent = 0;
                        if (options.Mechanisms.RecurrentCompletion)
                        {
                            // Membership dictionaries are the sparse adjacency index: work scales
                            // with actual engram memberships rather than memory-count × neuron-count.
                            foreach ((Guid memoryId, double membership) in neuron.Memberships)
                                recurrent += membership * support[memoryId];
                            recurrent = options.RecurrentGain * recurrent / Math.Max(1, neuron.Memberships.Count);
                        }

                        double targetMembership = neuron.Memberships.GetValueOrDefault(expected.Id);
                        double separation = 0;
                        foreach ((Guid competitorId, double competitorMembership) in neuron.Memberships)
                        {
                            if (!options.Mechanisms.RecallInhibition
                                || competitorId == expected.Id
                                || competitorMembership <= targetMembership)
                                continue;
                            double learned = pairInhibition.GetValueOrDefault(new MemoryPair(expected.Id, competitorId));
                            separation += learned * (competitorMembership - targetMembership) * support[competitorId];
                        }

                        double intrinsicInhibition = options.Mechanisms.RecallInhibition ? neuron.Inhibition : 0;
                        double raw = recurrent - global - intrinsicInhibition - separation;
                        double candidate = Logistic(options.ActivationGain * (raw - options.ActivationMidpoint));
                        double blended = (options.ActivationLeak * neuron.Activation)
                            + ((1 - options.ActivationLeak) * candidate);
                        next[neuron.Id] = cue.NeuronDrive.TryGetValue(neuron.Id, out double clamped)
                            ? Math.Max(clamped, blended)
                            : blended;
                    }

                    for (int index = 0; index < neurons.Length; index++)
                    {
                        neurons[index].Activation = next[index];
                        activationCost += next[index];
                    }
                }
            }
            finally
            {
                ArrayPool<double>.Shared.Return(next, clearArray: true);
            }

            var activations = memories.Values
                .Select(memory => (Memory: memory, Activation: WeightedActivation(memory)))
                .OrderByDescending(item => item.Activation)
                .ThenBy(item => item.Memory.Id)
                .ToArray();
            (MutableMemory Memory, double Activation) winner = activations[0];
            double expectedActivation = activations.Single(item => item.Memory.Id == expected.Id).Activation;
            double competitorActivation = activations
                .Where(item => item.Memory.Id != expected.Id)
                .Select(item => item.Activation)
                .DefaultIfEmpty(0)
                .Max();
            double completion = expected.Members.Keys.Count(
                neuronId => neurons[neuronId].Activation >= options.ActivationThreshold)
                / (double)expected.Members.Count;
            int activeCount = neurons.Count(neuron => neuron.Activation >= options.ActivationThreshold);
            int distortedCount = neurons.Count(neuron =>
                neuron.Activation >= options.ActivationThreshold
                && !expected.Members.ContainsKey(neuron.Id)
                && neuron.Memberships.Count > 0);
            double distortion = activeCount == 0 ? 0 : distortedCount / (double)activeCount;
            bool success = winner.Memory.Id == expected.Id && expectedActivation >= options.SuccessThreshold;
            revision++;
            return new RecallResult(
                expected.Id,
                winner.Memory.Id,
                success,
                expectedActivation,
                competitorActivation,
                expectedActivation - competitorActivation,
                completion,
                distortion,
                activationCost / (options.RecallIterations * neurons.Length),
                options.RecallIterations,
                recalledAt);
        }
    }

    /// <summary>Advances consolidation, decay, excitability relaxation, and engram membership drift.</summary>
    public void AdvanceTo(DateTimeOffset time)
    {
        time = RequireTimestamp(time, nameof(time));
        lock (sync)
        {
            if (time < currentTime) throw new ArgumentException("Simulation time cannot move backward.", nameof(time));
            AdvanceCore(time);
        }
    }

    /// <summary>Strengthens directed inhibitory separation using current stimulus similarity.</summary>
    public double TrainInhibitorySeparation(Guid cueMemoryId, Guid competitorMemoryId, double amount)
    {
        if (!double.IsFinite(amount) || amount is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(amount));
        if (cueMemoryId == competitorMemoryId) throw new ArgumentException("A memory cannot inhibit itself.");
        lock (sync)
        {
            if (!options.Mechanisms.InhibitoryPlasticity)
                throw new InvalidOperationException("Inhibitory plasticity is disabled by the mechanism profile.");
            MutableMemory cue = GetMemory(cueMemoryId);
            MutableMemory competitor = GetMemory(competitorMemoryId);
            var key = new MemoryPair(cue.Id, competitor.Id);
            double learned = Math.Clamp(
                pairInhibition.GetValueOrDefault(key) + (amount * StimulusSimilarity(cue, competitor)),
                0,
                1);
            pairInhibition[key] = learned;
            revision++;
            return learned;
        }
    }

    /// <summary>
    /// Replays two memories together and deterministically increases their neuronal overlap.
    /// Membership count remains constant, making this an explicit retrospective-linking hypothesis.
    /// </summary>
    public EngramReplayResult ReplayTogether(
        Guid firstMemoryId,
        Guid secondMemoryId,
        double coupling,
        DateTimeOffset replayedAt)
    {
        if (firstMemoryId == secondMemoryId) throw new ArgumentException("Replay requires two memories.");
        if (!double.IsFinite(coupling) || coupling is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(coupling));
        replayedAt = RequireTimestamp(replayedAt, nameof(replayedAt));
        lock (sync)
        {
            if (replayedAt < currentTime) throw new ArgumentException("Replay cannot precede simulation time.", nameof(replayedAt));
            AdvanceCore(replayedAt);
            MutableMemory first = GetMemory(firstMemoryId);
            MutableMemory second = GetMemory(secondMemoryId);
            double before = PopulationOverlap(first, second);
            int changes = Math.Max(1, (int)Math.Round(Math.Min(first.Members.Count, second.Members.Count) * coupling));
            LinkMembers(first, second, changes);
            LinkMembers(second, first, changes);
            first.Consolidation = Math.Clamp(first.Consolidation + (0.05 * coupling), 0, 1);
            second.Consolidation = Math.Clamp(second.Consolidation + (0.05 * coupling), 0, 1);
            revision++;
            return new EngramReplayResult(
                firstMemoryId,
                secondMemoryId,
                before,
                PopulationOverlap(first, second),
                changes,
                replayedAt);
        }
    }

    /// <summary>Captures one memory's current neuronal membership.</summary>
    public EngramSnapshot GetEngram(Guid memoryId)
    {
        lock (sync) return SnapshotCore(GetMemory(memoryId));
    }

    /// <summary>Returns immutable neuron snapshots ordered by identifier.</summary>
    public IReadOnlyList<EngramNeuronSnapshot> GetNeurons()
    {
        lock (sync)
        {
            return neurons.Select(neuron => new EngramNeuronSnapshot(
                neuron.Id,
                neuron.Activation,
                neuron.Excitability,
                neuron.Inhibition,
                neuron.Consolidation,
                new Dictionary<Guid, double>(neuron.Memberships)))
                .ToArray();
        }
    }

    /// <summary>Exports a defensive deterministic checkpoint.</summary>
    public DynamicEngramState ExportState()
    {
        lock (sync)
        {
            return new DynamicEngramState(
                EngramStateMigrator.CurrentSchemaVersion,
                currentTime,
                randomState,
                revision,
                neurons.Select(neuron => new NeuronState(
                    neuron.Id,
                    neuron.Activation,
                    neuron.Excitability,
                    neuron.Inhibition,
                    neuron.Consolidation)).ToArray(),
                memories.Values.OrderBy(memory => memory.Id).Select(memory => new MemoryEngramState(
                    memory.Id,
                    memory.Label,
                    memory.EncodedAt,
                    (double[])memory.Stimulus.Clone(),
                    memory.Members.OrderBy(member => member.Key)
                        .Select(member => new EngramMemberState(member.Key, member.Value)).ToArray(),
                    memory.Consolidation)).ToArray(),
                pairInhibition.OrderBy(pair => pair.Key.Cue).ThenBy(pair => pair.Key.Competitor)
                    .Select(pair => new PairInhibitionState(pair.Key.Cue, pair.Key.Competitor, pair.Value))
                    .ToArray())
            {
                MechanismProfileId = options.Mechanisms.Id,
                Mechanisms = options.Mechanisms,
                Options = options
            };
        }
    }

    /// <summary>Serializes a complete checkpoint.</summary>
    public string ToJson() => JsonSerializer.Serialize(ExportState(), JsonOptions);

    /// <summary>Restores and validates a complete checkpoint.</summary>
    public static EngramSimulation FromState(DynamicEngramState state, DynamicEngramOptions? options = null) =>
        new(state, options);

    /// <summary>Deserializes and validates a complete checkpoint.</summary>
    public static EngramSimulation FromJson(string json, DynamicEngramOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        DynamicEngramState state = JsonSerializer.Deserialize<DynamicEngramState>(json, JsonOptions)
            ?? throw new ArgumentException("Checkpoint is empty.", nameof(json));
        return FromState(state, options);
    }

    private void AdvanceCore(DateTimeOffset time)
    {
        if (time == currentTime) return;
        DateTimeOffset originalTime = currentTime;

        // Continuous dynamics use closed-form updates. Drift is evaluated only at UTC
        // day boundaries with deterministic noise, so splitting one advance into many
        // calls cannot change which neurons turn over.
        while (currentTime < time)
        {
            DateTimeOffset nextBoundary = new(
                currentTime.UtcDateTime.Date.AddDays(1),
                TimeSpan.Zero);
            DateTimeOffset segmentEnd = time < nextBoundary ? time : nextBoundary;
            ApplyContinuousDynamics((segmentEnd - currentTime).TotalDays);
            currentTime = segmentEnd;
            if (segmentEnd == nextBoundary)
                ApplyDailyDrift(segmentEnd.UtcDateTime.Date.Ticks);
        }

        if (time > originalTime) revision++;
    }

    private void ApplyContinuousDynamics(double days)
    {
        double relaxation = 1 - Math.Exp(-options.ExcitabilityRelaxationRatePerDay * days);
        foreach (MutableNeuron neuron in neurons)
            neuron.Excitability += (0.5 - neuron.Excitability) * relaxation;

        foreach (MutableMemory memory in memories.Values)
        {
            double initialConsolidation = options.Mechanisms.Consolidation ? memory.Consolidation : 0;
            double rate = options.Mechanisms.Consolidation ? options.ConsolidationRatePerDay : 0;
            double protectionIntegral = rate == 0
                ? days * (1 - (0.8 * initialConsolidation))
                : (0.2 * days) +
                  (0.8 * (1 - initialConsolidation) * (1 - Math.Exp(-rate * days)) / rate);
            if (options.Mechanisms.Consolidation)
                memory.Consolidation = 1 - ((1 - initialConsolidation) * Math.Exp(-rate * days));
            double decay = Math.Exp(-Math.Log(2) * protectionIntegral / options.MembershipHalfLifeDays);
            foreach (int neuronId in memory.Members.Keys.ToArray())
            {
                double strength = Math.Clamp(memory.Members[neuronId] * decay, 1e-6, 1);
                memory.Members[neuronId] = strength;
                neurons[neuronId].Memberships[memory.Id] = strength;
            }
        }
    }

    private void ApplyDailyDrift(long dayKey)
    {
        if (!options.Mechanisms.PopulationDrift)
            return;

        foreach (MutableMemory memory in memories.Values)
        {
            double expectedTurnover = memory.Members.Count * options.DriftFractionPerDay;
            int replacements = (int)Math.Floor(expectedTurnover);
            if (StableNoise(memory.Id, dayKey, -1) < expectedTurnover - replacements) replacements++;
            replacements = Math.Min(replacements, memory.Members.Count - 1);
            if (replacements <= 0) continue;

            int[] removed = memory.Members
                .Select(member => (member.Key, Score: member.Value + (0.01 * StableNoise(memory.Id, dayKey, member.Key))))
                .OrderBy(item => item.Score)
                .Take(replacements)
                .Select(item => item.Key)
                .ToArray();
            var candidateScores = Enumerable.Range(0, neurons.Length)
                .Where(neuronId => !memory.Members.ContainsKey(neuronId))
                .Select(neuronId => (
                    Id: neuronId,
                    Score: (options.StimulusWeight * memory.Stimulus[neuronId])
                        + (options.ExcitabilityWeight * neurons[neuronId].Excitability)
                        + (options.AllocationNoise * StableNoise(memory.Id, dayKey, neuronId + neurons.Length))))
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Id)
                .Take(replacements)
                .ToArray();

            for (int index = 0; index < removed.Length; index++)
            {
                int oldId = removed[index];
                double inherited = memory.Members[oldId];
                memory.Members.Remove(oldId);
                neurons[oldId].Memberships.Remove(memory.Id);
                int newId = candidateScores[index].Id;
                double strength = Math.Clamp(
                    (0.75 * inherited)
                    + (0.25 * options.HebbianStrength * memory.Stimulus[newId]),
                    0.05,
                    1);
                memory.Members.Add(newId, strength);
                neurons[newId].Memberships.Add(memory.Id, strength);
            }
        }
    }

    private static double StableNoise(Guid memoryId, long dayKey, int discriminator)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte value in memoryId.ToByteArray()) hash = (hash ^ value) * 1099511628211UL;
        hash = Hash(hash, unchecked((ulong)dayKey));
        hash = Hash(hash, unchecked((ulong)discriminator));
        return (hash >> 11) * (1.0 / (1UL << 53));
    }

    private void LearnAutomaticSeparation(MutableMemory added)
    {
        if (!options.Mechanisms.InhibitoryPlasticity)
            return;

        foreach (MutableMemory other in memories.Values.Where(memory => memory.Id != added.Id))
        {
            double strength = Math.Clamp(options.PairInhibitionRate * StimulusSimilarity(added, other), 0, 1);
            if (strength == 0) continue;
            pairInhibition[new MemoryPair(added.Id, other.Id)] = strength;
            pairInhibition[new MemoryPair(other.Id, added.Id)] = strength;
        }
    }

    private double WeightedActivation(MutableMemory memory)
    {
        double weight = 0;
        double activation = 0;
        foreach ((int neuronId, double strength) in memory.Members)
        {
            weight += strength;
            activation += strength * neurons[neuronId].Activation;
        }

        return weight <= 0 ? 0 : activation / weight;
    }

    private void LinkMembers(MutableMemory target, MutableMemory source, int count)
    {
        int[] additions = source.Members
            .Where(member => !target.Members.ContainsKey(member.Key))
            .OrderByDescending(member => member.Value)
            .ThenBy(member => member.Key)
            .Take(count)
            .Select(member => member.Key)
            .ToArray();
        int[] removals = target.Members
            .Where(member => !source.Members.ContainsKey(member.Key))
            .OrderBy(member => member.Value)
            .ThenBy(member => member.Key)
            .Take(additions.Length)
            .Select(member => member.Key)
            .ToArray();
        for (int index = 0; index < additions.Length; index++)
        {
            int removed = removals[index];
            double inherited = target.Members[removed];
            target.Members.Remove(removed);
            neurons[removed].Memberships.Remove(target.Id);
            int added = additions[index];
            double strength = Math.Clamp((inherited + source.Members[added]) / 2, 0.05, 1);
            target.Members.Add(added, strength);
            neurons[added].Memberships[target.Id] = strength;
        }
    }

    private static double PopulationOverlap(MutableMemory first, MutableMemory second)
    {
        int intersection = first.Members.Keys.Intersect(second.Members.Keys).Count();
        int union = first.Members.Count + second.Members.Count - intersection;
        return union == 0 ? 1 : intersection / (double)union;
    }

    private static double[] Rotate(double[] values, int offset)
    {
        var rotated = new double[values.Length];
        for (int index = 0; index < values.Length; index++)
            rotated[(index + offset) % values.Length] = values[index];
        return rotated;
    }

    private void Shuffle(double[] values)
    {
        for (int index = values.Length - 1; index > 0; index--)
        {
            int other = (int)(NextDouble() * (index + 1));
            (values[index], values[other]) = (values[other], values[index]);
        }
    }

    private EngramSnapshot SnapshotCore(MutableMemory memory) => new(
        memory.Id,
        memory.Label,
        currentTime,
        memory.Members.Keys.Order().ToArray(),
        memory.Members.Values.Average(),
        memory.Consolidation);

    private MutableMemory GetMemory(Guid id) =>
        memories.TryGetValue(id, out MutableMemory? memory)
            ? memory
            : throw new KeyNotFoundException($"Memory '{id}' was not found.");

    private static double StimulusSimilarity(MutableMemory first, MutableMemory second)
    {
        int intersection = 0;
        int union = 0;
        for (int index = 0; index < first.Stimulus.Length; index++)
        {
            bool left = first.Stimulus[index] >= 0.5;
            bool right = second.Stimulus[index] >= 0.5;
            if (left || right) union++;
            if (left && right) intersection++;
        }

        return union == 0 ? 0 : intersection / (double)union;
    }

    private static void ValidateState(DynamicEngramState state, DynamicEngramOptions options)
    {
        if (state.SchemaVersion != EngramStateMigrator.CurrentSchemaVersion)
            throw new ArgumentException("Unsupported schema.", nameof(state));
        ArgumentException.ThrowIfNullOrWhiteSpace(state.MechanismProfileId);
        ArgumentNullException.ThrowIfNull(state.Mechanisms);
        state.Mechanisms.Validate();
        state.Options?.Validate();
        if (!string.Equals(state.MechanismProfileId, state.Mechanisms.Id, StringComparison.Ordinal))
            throw new ArgumentException("Checkpoint mechanism profile metadata is inconsistent.", nameof(state));
        if (state.Options is not null &&
            (state.Options.Mechanisms != state.Mechanisms ||
             state.Options.NeuronCount != state.Neurons?.Length))
            throw new ArgumentException("Checkpoint options do not match its profile or population.", nameof(state));
        if (state.Mechanisms != options.Mechanisms)
            throw new ArgumentException("Checkpoint mechanism profile does not match the supplied options.", nameof(state));
        _ = RequireTimestamp(state.CurrentTime, nameof(state));
        if (state.Revision < 0) throw new ArgumentException("Revision cannot be negative.", nameof(state));
        ArgumentNullException.ThrowIfNull(state.Neurons);
        ArgumentNullException.ThrowIfNull(state.Memories);
        ArgumentNullException.ThrowIfNull(state.PairInhibitions);
        if (state.Neurons.Length != options.NeuronCount) throw new ArgumentException("Neuron count does not match options.", nameof(state));
        int[] ids = state.Neurons.Select(neuron => neuron.Id).Order().ToArray();
        if (!ids.SequenceEqual(Enumerable.Range(0, options.NeuronCount))) throw new ArgumentException("Neuron identifiers are invalid.", nameof(state));
        foreach (NeuronState neuron in state.Neurons)
        {
            Probability(neuron.Activation, nameof(state));
            Probability(neuron.Excitability, nameof(state));
            Probability(neuron.Inhibition, nameof(state));
            Probability(neuron.Consolidation, nameof(state));
        }

        var memoryIds = new HashSet<Guid>();
        foreach (MemoryEngramState memory in state.Memories)
        {
            if (memory.MemoryId == Guid.Empty || !memoryIds.Add(memory.MemoryId))
                throw new ArgumentException("Memory identifiers must be non-empty and unique.", nameof(state));
            _ = RequireId(memory.Label, nameof(state));
            DateTimeOffset encoded = RequireTimestamp(memory.EncodedAt, nameof(state));
            if (encoded > state.CurrentTime) throw new ArgumentException("Memory encoding is in the future.", nameof(state));
            if (memory.Stimulus is null || memory.Stimulus.Length != options.NeuronCount
                || memory.Stimulus.Any(value => !double.IsFinite(value) || value is < 0 or > 1))
                throw new ArgumentException("Memory stimulus is invalid.", nameof(state));
            if (memory.Members is null || memory.Members.Length < 2
                || memory.Members.Select(member => member.NeuronId).Distinct().Count() != memory.Members.Length)
                throw new ArgumentException("Memory members are invalid.", nameof(state));
            foreach (EngramMemberState member in memory.Members)
                if ((uint)member.NeuronId >= (uint)options.NeuronCount
                    || !double.IsFinite(member.Strength) || member.Strength is <= 0 or > 1)
                    throw new ArgumentException("Memory membership is invalid.", nameof(state));
            Probability(memory.Consolidation, nameof(state));
        }

        var pairs = new HashSet<MemoryPair>();
        foreach (PairInhibitionState pair in state.PairInhibitions)
        {
            var key = new MemoryPair(pair.CueMemoryId, pair.CompetitorMemoryId);
            if (pair.CueMemoryId == pair.CompetitorMemoryId
                || !memoryIds.Contains(pair.CueMemoryId)
                || !memoryIds.Contains(pair.CompetitorMemoryId)
                || !pairs.Add(key))
                throw new ArgumentException("Pair inhibition reference is invalid.", nameof(state));
            Probability(pair.Strength, nameof(state));
        }
    }

    private static string RequireId(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value.Trim();
    }

    private static DateTimeOffset RequireTimestamp(DateTimeOffset value, string name)
    {
        if (value == default) throw new ArgumentOutOfRangeException(name);
        return value.ToUniversalTime();
    }

    private static void Probability(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1) throw new ArgumentOutOfRangeException(name);
    }

    private static double Logistic(double value)
    {
        if (value >= 30) return 1;
        if (value <= -30) return 0;
        return 1 / (1 + Math.Exp(-value));
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
        return new Guid(bytes);
    }

    private static ulong Hash(ulong seed, ulong value)
    {
        ulong mixed = seed + (value * 0x9E3779B97F4A7C15UL);
        mixed = (mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9UL;
        mixed = (mixed ^ (mixed >> 27)) * 0x94D049BB133111EBUL;
        return mixed ^ (mixed >> 31);
    }

    private sealed class MutableNeuron(
        int id,
        double activation,
        double excitability,
        double inhibition,
        double consolidation)
    {
        public int Id { get; } = id;
        public double Activation { get; set; } = activation;
        public double Excitability { get; set; } = excitability;
        public double Inhibition { get; } = inhibition;
        public double Consolidation { get; set; } = consolidation;
        public Dictionary<Guid, double> Memberships { get; } = [];
    }

    private sealed class MutableMemory(
        Guid id,
        string label,
        DateTimeOffset encodedAt,
        double[] stimulus,
        Dictionary<int, double> members,
        double consolidation)
    {
        public Guid Id { get; } = id;
        public string Label { get; } = label;
        public DateTimeOffset EncodedAt { get; } = encodedAt;
        public double[] Stimulus { get; } = stimulus;
        public Dictionary<int, double> Members { get; } = members;
        public double Consolidation { get; set; } = consolidation;
    }

    private readonly record struct MemoryPair(Guid Cue, Guid Competitor);
}
