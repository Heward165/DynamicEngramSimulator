using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DynamicEngramSimulator.Tests;

[TestClass]
public sealed class ResearchHardeningTests
{
    [TestMethod]
    public void TemporalIntegrationIsIndependentOfCallPartitioning()
    {
        DynamicEngramOptions options = Options();
        DateTimeOffset start = Epoch();
        MemoryStimulus stimulus = StimulusFactory.Sparse(options.NeuronCount, 20, 9);
        var single = new EngramSimulation(start, options);
        var partitioned = new EngramSimulation(start, options);
        EngramSnapshot left = single.Encode(new("m", stimulus), start);
        EngramSnapshot right = partitioned.Encode(new("m", stimulus), start);
        single.AdvanceTo(start.AddDays(20));
        for (int day = 1; day <= 20; day++) partitioned.AdvanceTo(start.AddDays(day));

        CollectionAssert.AreEqual(single.GetEngram(left.MemoryId).NeuronIds.ToArray(),
            partitioned.GetEngram(right.MemoryId).NeuronIds.ToArray());
        Assert.AreEqual(single.GetEngram(left.MemoryId).MeanMembership,
            partitioned.GetEngram(right.MemoryId).MeanMembership, 1e-12);
    }

    [TestMethod]
    public void SeedSweepIsReproducibleExceptForManifestTimestamp()
    {
        var definition = new EngramExperimentDefinition(Options(), [1, 2, 3], 20);
        EngramExperimentReport first = EngramExperimentRunner.Run(definition);
        EngramExperimentReport second = EngramExperimentRunner.Run(definition);
        CollectionAssert.AreEqual(first.Runs.ToArray(), second.Runs.ToArray());
    }

    [TestMethod]
    public void ExperimentExportsIncludeRawSeeds()
    {
        EngramExperimentReport report = EngramExperimentRunner.Run(
            new EngramExperimentDefinition(Options(), [41, 42], 20));
        StringAssert.Contains(EngramExperimentRunner.ToJson(report), "\"seeds\"");
        StringAssert.Contains(EngramExperimentRunner.ToCsv(report), "41,");
    }

    private static DynamicEngramOptions Options() => new()
    {
        NeuronCount = 96,
        DefaultEngramSize = 16,
        DriftFractionPerDay = 0.02,
        RandomSeed = 10,
    };

    private static DateTimeOffset Epoch() => new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
}
