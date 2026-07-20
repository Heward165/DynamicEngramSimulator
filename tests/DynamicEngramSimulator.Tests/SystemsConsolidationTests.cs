using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DynamicEngramSimulator.Tests;

[TestClass]
public sealed class SystemsConsolidationTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void EncodingCreatesFourPopulationsAndExplicitDirectedSynapses()
    {
        SystemsConsolidationOptions options = Options();
        var simulator = new SystemsConsolidationSimulator(Epoch, options);
        SystemsMemorySnapshot memory = simulator.Encode(new("event", Context(1)), Epoch);

        Assert.HasCount(4, memory.Regions);
        Assert.IsTrue(memory.Regions.All(region => region.NeuronIds.Count >= 2));
        Assert.IsGreaterThan(0, memory.Synapses.Count);
        Assert.IsTrue(memory.Synapses.All(edge => edge.SourceRegion < edge.TargetRegion));
        Assert.AreEqual(1d, memory.SynapticIdentity, 1e-12);
    }

    [TestMethod]
    public void SleepReplayMovesRemoteRecallTowardCortexAndGeneralization()
    {
        var definition = new SystemsConsolidationExperimentDefinition
        {
            Options = Options(),
            Days = 21,
            ProbeDays = [0, 21],
            SleepReplayEnabled = true
        };
        SystemsConsolidationReport replay = SystemsConsolidationExperiment.Run(definition, Epoch);
        SystemsConsolidationReport noReplay = SystemsConsolidationExperiment.Run(
            definition with { SleepReplayEnabled = false }, Epoch);

        SystemsRecallProbe recent = replay.Observations[0].Probe;
        SystemsRecallProbe remote = replay.Observations[^1].Probe;
        Assert.IsGreaterThan(noReplay.Observations[^1].Probe.CorticalInvolvement, remote.CorticalInvolvement);
        Assert.IsGreaterThan(recent.RelatedContextGeneralization, remote.RelatedContextGeneralization);
        Assert.IsLessThan(recent.HippocampalInvolvement, remote.HippocampalInvolvement);
    }

    [TestMethod]
    public void ReplayQueueIsPriorityOrderedCapacityLimitedAndInterruptible()
    {
        var simulator = new SystemsConsolidationSimulator(Epoch, Options());
        SystemsMemorySnapshot first = simulator.Encode(new("first", Context(2)), Epoch);
        SystemsMemorySnapshot second = simulator.Encode(new("second", Context(3)), Epoch.AddMinutes(1));
        SleepEpochResult result = simulator.RunSleepEpoch(new(
        [
            new(first.MemoryId, 1, ReplayDirection.Forward),
            new(second.MemoryId, 2, ReplayDirection.Reverse)
        ],
        MaximumReplays: 2,
        InterruptionFraction: 0.5), Epoch.AddHours(6));

        Assert.AreEqual(1, result.CompletedReplays);
        Assert.AreEqual(second.MemoryId, result.ReplayedMemories[0]);
        Assert.AreEqual(first.MemoryId, result.SkippedMemories[0]);
    }

    [TestMethod]
    public void NeurogenesisRewiresCellsAndSynapsesWithoutChangingPopulationSize()
    {
        SystemsConsolidationOptions options = Options() with
        {
            ModelFamily = ConsolidationModelFamily.NeurogenesisRewiring,
            NeurogenesisFractionPerDay = 0.20
        };
        var simulator = new SystemsConsolidationSimulator(Epoch, options);
        SystemsMemorySnapshot before = simulator.Encode(new("event", Context(4)), Epoch);
        simulator.AdvanceTo(Epoch.AddDays(10));
        SystemsMemorySnapshot after = simulator.GetMemorySnapshot(before.MemoryId);
        RegionalEngramSnapshot beforeDg = before.Regions.Single(region => region.Region == EngramRegion.DentateGyrus);
        RegionalEngramSnapshot afterDg = after.Regions.Single(region => region.Region == EngramRegion.DentateGyrus);

        Assert.HasCount(beforeDg.NeuronIds.Count, afterDg.NeuronIds);
        Assert.IsLessThan(1d, afterDg.PopulationIdentity);
        Assert.IsLessThan(1d, after.SynapticIdentity);
    }

    [TestMethod]
    public void ModelComparisonRunsEveryFamilyWithFiniteOutputs()
    {
        IReadOnlyList<SystemsConsolidationReport> reports = SystemsConsolidationExperiment.CompareModelFamilies(
            new()
            {
                Options = Options(),
                Days = 7,
                ProbeDays = [0, 7]
            }, Epoch);

        Assert.HasCount(Enum.GetValues<ConsolidationModelFamily>().Length, reports);
        Assert.HasCount(reports.Count, reports.Select(report => report.ModelFamily).Distinct());
        Assert.IsTrue(reports.SelectMany(report => report.Observations)
            .All(observation => double.IsFinite(observation.Probe.ExactContextRecall)));
    }

    private static SystemsConsolidationOptions Options() => new()
    {
        Regions =
        [
            new(EngramRegion.DentateGyrus, 64, 0.12, 0.65, 21),
            new(EngramRegion.CA3, 48, 0.15, 0.60, 30),
            new(EngramRegion.CA1, 48, 0.15, 0.55, 45),
            new(EngramRegion.Cortex, 64, 0.18, 0.25, 120)
        ],
        SynapticFanOut = 2,
        RandomSeed = 11
    };

    private static SystemsMemoryContext Context(ulong seed)
    {
        var values = new double[16];
        var random = new Random(unchecked((int)seed));
        for (int index = 0; index < values.Length; index++)
            values[index] = random.NextDouble();
        return new(values);
    }
}
