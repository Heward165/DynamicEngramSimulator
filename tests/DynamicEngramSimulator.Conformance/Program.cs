using DynamicEngramSimulator;

var checks = new (string Name, Action Run)[]
{
    ("Sparse allocation is deterministic", SparseAllocationIsDeterministic),
    ("Excitability biases allocation", ExcitabilityBiasesAllocation),
    ("Partial cues complete an engram", PartialCuesCompleteEngram),
    ("Related stimuli overlap more", RelatedStimuliOverlapMore),
    ("Inhibitory plasticity improves selectivity", InhibitionImprovesSelectivity),
    ("Identity metric is exact", IdentityMetricIsExact),
    ("Population drift changes membership", DriftChangesMembership),
    ("Behavior can survive population drift", BehaviorSurvivesDrift),
    ("Overlapping engrams activate competitors", OverlapActivatesCompetitor),
    ("JSON checkpoints continue deterministically", JsonContinuesDeterministically),
    ("Corrupted checkpoints are rejected", CorruptedStateIsRejected),
    ("Chronology and dimensions fail closed", InvalidInputsFailClosed),
    ("Temporal integration is partition invariant", TemporalIntegrationIsPartitionInvariant),
    ("Multi-seed reports export JSON and CSV", MultiSeedReportsExport),
};

int failures = 0;
foreach ((string name, Action run) in checks)
{
    try
    {
        run();
        Console.WriteLine($"PASS: {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL: {name}");
        Console.Error.WriteLine(exception.Message);
    }
}

Console.WriteLine($"{checks.Length - failures}/{checks.Length} conformance checks passed.");
return failures == 0 ? 0 : 1;

static void SparseAllocationIsDeterministic()
{
    DynamicEngramOptions options = Options();
    MemoryStimulus stimulus = StimulusFactory.Sparse(options.NeuronCount, 24, 10);
    var first = new EngramSimulation(Moment(), options);
    var second = new EngramSimulation(Moment(), options);
    EngramSnapshot left = first.Encode(new("memory", stimulus), Moment());
    EngramSnapshot right = second.Encode(new("memory", stimulus), Moment());

    Check(left.MemoryId == right.MemoryId, "Deterministic identifiers diverged.");
    Check(left.NeuronIds.SequenceEqual(right.NeuronIds), "Deterministic allocations diverged.");
    Check(left.NeuronIds.Count == options.DefaultEngramSize, "Engram was not sparse at the configured size.");
}

static void ExcitabilityBiasesAllocation()
{
    DynamicEngramOptions options = Options() with
    {
        DefaultEngramSize = 8,
        StimulusWeight = 0.01,
        ExcitabilityWeight = 1,
        AllocationNoise = 0,
    };
    var original = new EngramSimulation(Moment(), options);
    DynamicEngramState state = original.ExportState();
    for (int index = 0; index < state.Neurons.Length; index++)
        state.Neurons[index] = state.Neurons[index] with { Excitability = index < 8 ? 1 : 0 };
    var restored = EngramSimulation.FromState(state, options);
    var uniform = new MemoryStimulus(Enumerable.Repeat(0.5, options.NeuronCount));
    EngramSnapshot engram = restored.Encode(new("biased", uniform), Moment());

    Check(engram.NeuronIds.SequenceEqual(Enumerable.Range(0, 8)), "Most excitable neurons were not allocated.");
}

static void PartialCuesCompleteEngram()
{
    DynamicEngramOptions options = Options();
    var simulation = new EngramSimulation(Moment(), options);
    EngramSnapshot memory = simulation.Encode(
        new("partial", StimulusFactory.Sparse(options.NeuronCount, 24, 11)),
        Moment());
    RecallResult recall = simulation.Recall(
        simulation.CreatePartialCue(memory.MemoryId, 0.20, 5),
        Moment());

    Check(recall.Success, "A clean partial cue did not retrieve its memory.");
    Check(recall.CompletionRatio >= 0.75, "Pattern completion left most members inactive.");
}

static void RelatedStimuliOverlapMore()
{
    DynamicEngramOptions options = Options();
    var simulation = new EngramSimulation(Moment(), options);
    MemoryStimulus original = StimulusFactory.Sparse(options.NeuronCount, 24, 12);
    MemoryStimulus related = StimulusFactory.Related(original, 0.75, 13);
    MemoryStimulus unrelated = StimulusFactory.Sparse(options.NeuronCount, 24, 999);
    EngramSnapshot first = simulation.Encode(new("first", original), Moment());
    EngramSnapshot second = simulation.Encode(new("related", related), Moment());
    EngramSnapshot third = simulation.Encode(new("unrelated", unrelated), Moment());

    double relatedIdentity = PopulationIdentity(first, second);
    double unrelatedIdentity = PopulationIdentity(first, third);
    Check(relatedIdentity > unrelatedIdentity, "Related input did not create greater population overlap.");
}

static void InhibitionImprovesSelectivity()
{
    DynamicEngramOptions plainOptions = Options() with { PairInhibitionRate = 0 };
    DynamicEngramOptions inhibitedOptions = plainOptions with { PairInhibitionRate = 1 };
    (EngramSimulation plain, Guid plainTarget) = TwoRelatedMemories(plainOptions);
    (EngramSimulation inhibited, Guid inhibitedTarget) = TwoRelatedMemories(inhibitedOptions);
    RecallResult without = plain.Recall(plain.CreatePartialCue(plainTarget, 0.15, 8), Moment());
    RecallResult with = inhibited.Recall(inhibited.CreatePartialCue(inhibitedTarget, 0.15, 8), Moment());

    Check(with.SelectivityMargin > without.SelectivityMargin, "Pair inhibition did not improve the target margin.");
}

static void IdentityMetricIsExact()
{
    Guid id = Guid.NewGuid();
    var before = new EngramSnapshot(id, "m", Moment(), [1, 2, 3], 1, 0);
    var after = new EngramSnapshot(id, "m", Moment(), [2, 3, 4], 1, 0);
    var recall = new RecallResult(id, id, true, 0.8, 0.1, 0.7, 1, 0, 0.1, 1, Moment());
    EngramComparison comparison = EngramMetrics.Compare(before, after, recall, recall);

    Near(0.5, comparison.PopulationIdentity, 0, "Jaccard identity");
    Check(comparison.RetainedNeurons == 2 && comparison.AddedNeurons == 1 && comparison.RemovedNeurons == 1,
        "Population accounting is incorrect.");
    Near(1, comparison.BehavioralStability, 0, "behavioral stability");
}

static void DriftChangesMembership()
{
    DynamicEngramOptions options = Options() with { DriftFractionPerDay = 0.02 };
    var simulation = new EngramSimulation(Moment(), options);
    EngramSnapshot before = simulation.Encode(
        new("drift", StimulusFactory.Sparse(options.NeuronCount, 24, 14)),
        Moment());
    simulation.AdvanceTo(Moment().AddDays(20));
    EngramSnapshot after = simulation.GetEngram(before.MemoryId);

    Check(PopulationIdentity(before, after) < 1, "Configured drift did not replace any member.");
    Check(before.NeuronIds.Count == after.NeuronIds.Count, "Drift changed population sparsity.");
}

static void BehaviorSurvivesDrift()
{
    DynamicEngramOptions options = Options() with { DriftFractionPerDay = 0.01 };
    var simulation = new EngramSimulation(Moment(), options);
    EngramSnapshot before = simulation.Encode(
        new("stable-behavior", StimulusFactory.Sparse(options.NeuronCount, 24, 15)),
        Moment());
    RecallResult initial = simulation.Recall(
        simulation.CreatePartialCue(before.MemoryId, 0.25, 1),
        Moment());
    simulation.AdvanceTo(Moment().AddDays(20));
    EngramSnapshot after = simulation.GetEngram(before.MemoryId);
    RecallResult later = simulation.Recall(
        simulation.CreatePartialCue(before.MemoryId, 0.25, 1),
        Moment().AddDays(20));
    EngramComparison comparison = EngramMetrics.Compare(before, after, initial, later);

    Check(comparison.PopulationIdentity < 1, "The experiment did not produce population turnover.");
    Check(initial.Success && later.Success, "Recall was not behaviorally stable through moderate drift.");
    Check(comparison.BehavioralStability >= 0.8, "Activation changed too much through moderate drift.");
}

static void OverlapActivatesCompetitor()
{
    DynamicEngramOptions options = Options() with { PairInhibitionRate = 0 };
    (EngramSimulation simulation, Guid target) = TwoRelatedMemories(options, retainedFraction: 0.90);
    RecallResult recall = simulation.Recall(
        simulation.CreatePartialCue(target, 0.15, 44),
        Moment());

    Check(recall.StrongestCompetitorActivation >= 0.35, "A highly overlapping competitor remained implausibly inactive.");
}

static void JsonContinuesDeterministically()
{
    DynamicEngramOptions options = Options();
    var simulation = new EngramSimulation(Moment(), options);
    simulation.Encode(new("first", StimulusFactory.Sparse(options.NeuronCount, 24, 16)), Moment());
    EngramSimulation restored = EngramSimulation.FromJson(simulation.ToJson(), options);
    var next = new EngramEncodingRequest("second", StimulusFactory.Sparse(options.NeuronCount, 24, 17));
    EngramSnapshot left = simulation.Encode(next, Moment().AddDays(1));
    EngramSnapshot right = restored.Encode(next, Moment().AddDays(1));

    Check(left.MemoryId == right.MemoryId, "Random continuation changed after JSON restore.");
    Check(left.NeuronIds.SequenceEqual(right.NeuronIds), "Allocation changed after JSON restore.");
    Check(simulation.Revision == restored.Revision, "Revision changed after deterministic continuation.");
}

static void CorruptedStateIsRejected()
{
    DynamicEngramOptions options = Options();
    var simulation = new EngramSimulation(Moment(), options);
    simulation.Encode(new("m", StimulusFactory.Sparse(options.NeuronCount, 24, 18)), Moment());
    DynamicEngramState state = simulation.ExportState();
    state.Memories[0].Members[0] = state.Memories[0].Members[0] with { NeuronId = options.NeuronCount };
    Throws<ArgumentException>(() => EngramSimulation.FromState(state, options));
}

static void InvalidInputsFailClosed()
{
    DynamicEngramOptions options = Options();
    var simulation = new EngramSimulation(Moment(), options);
    Throws<ArgumentException>(() => simulation.Encode(
        new("wrong dimension", new MemoryStimulus([1, 0])),
        Moment()));
    EngramSnapshot memory = simulation.Encode(
        new("valid", StimulusFactory.Sparse(options.NeuronCount, 24, 19)),
        Moment());
    Throws<ArgumentException>(() => simulation.AdvanceTo(Moment().AddMinutes(-1)));
    Throws<ArgumentOutOfRangeException>(() => simulation.Recall(
        new RecallCue(memory.MemoryId, new Dictionary<int, double> { [options.NeuronCount] = 1 }),
        Moment()));
}

static void TemporalIntegrationIsPartitionInvariant()
{
    DynamicEngramOptions options = Options() with { DriftFractionPerDay = 0.02 };
    MemoryStimulus stimulus = StimulusFactory.Sparse(options.NeuronCount, 24, 77);
    var direct = new EngramSimulation(Moment(), options);
    var stepped = new EngramSimulation(Moment(), options);
    EngramSnapshot directMemory = direct.Encode(new("m", stimulus), Moment());
    EngramSnapshot steppedMemory = stepped.Encode(new("m", stimulus), Moment());
    direct.AdvanceTo(Moment().AddDays(30));
    for (int day = 1; day <= 30; day++) stepped.AdvanceTo(Moment().AddDays(day));
    EngramSnapshot left = direct.GetEngram(directMemory.MemoryId);
    EngramSnapshot right = stepped.GetEngram(steppedMemory.MemoryId);
    Check(left.NeuronIds.SequenceEqual(right.NeuronIds), "Advance partition changed drift membership.");
    Near(left.MeanMembership, right.MeanMembership, 1e-12, "partitioned membership strength");
}

static void MultiSeedReportsExport()
{
    var report = EngramExperimentRunner.Run(new(Options(), [1, 2, 3], ActiveStimulusChannels: 24));
    Check(report.Runs.Count == 3, "Seed sweep omitted runs.");
    Check(EngramExperimentRunner.ToJson(report).Contains("meanPopulationIdentity", StringComparison.Ordinal), "JSON omitted summary.");
    Check(EngramExperimentRunner.ToCsv(report).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 4, "CSV row count is wrong.");
}

static (EngramSimulation Simulation, Guid Target) TwoRelatedMemories(
    DynamicEngramOptions options,
    double retainedFraction = 0.75)
{
    var simulation = new EngramSimulation(Moment(), options);
    MemoryStimulus original = StimulusFactory.Sparse(options.NeuronCount, 24, 20);
    MemoryStimulus related = StimulusFactory.Related(original, retainedFraction, 21);
    EngramSnapshot target = simulation.Encode(new("target", original), Moment());
    simulation.Encode(new("competitor", related), Moment());
    return (simulation, target.MemoryId);
}

static DynamicEngramOptions Options() => new()
{
    NeuronCount = 128,
    DefaultEngramSize = 16,
    AllocationNoise = 0.01,
    RandomSeed = 12345,
};

static double PopulationIdentity(EngramSnapshot first, EngramSnapshot second)
{
    var left = first.NeuronIds.ToHashSet();
    var right = second.NeuronIds.ToHashSet();
    return left.Intersect(right).Count() / (double)left.Union(right).Count();
}

static DateTimeOffset Moment() => new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Near(double expected, double actual, double tolerance, string message)
{
    if (!double.IsFinite(actual) || Math.Abs(expected - actual) > tolerance)
        throw new InvalidOperationException($"{message}: expected {expected} +/- {tolerance}, actual {actual}.");
}

static void Throws<TException>(Action action) where TException : Exception
{
    try { action(); } catch (TException) { return; }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

