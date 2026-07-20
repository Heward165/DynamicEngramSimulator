using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DynamicEngramSimulator.Tests;

[TestClass]
public sealed class AdvancedResearchTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void MorrisSensitivityPreservesEverySampledEvaluation()
    {
        var definition = new EngramSensitivityDefinition(
            Experiment(),
            [
                new(EngramParameter.DriftFractionPerDay, 0.001, 0.04),
                new(EngramParameter.GlobalInhibition, 0.05, 0.50)
            ],
            EngramSensitivityMethod.Morris,
            EngramSensitivityMetric.FinalSelectivity,
            SampleCount: 4,
            SamplingSeed: 7);

        EngramSensitivityReport report = EngramSensitivityRunner.Run(definition, new(Epoch, "test"));

        Assert.HasCount(2, report.Indices);
        Assert.HasCount(12, report.Evaluations);
        Assert.IsTrue(report.Indices.All(index => index.MorrisMeanAbsoluteEffect >= 0));
        StringAssert.StartsWith(EngramSensitivityRunner.ToCsv(report), "parameter,");
    }

    [TestMethod]
    public void SobolSensitivityReportsFirstAndTotalOrderIndices()
    {
        var definition = new EngramSensitivityDefinition(
            Experiment() with { Seeds = [1, 2] },
            [new(EngramParameter.GlobalInhibition, 0.05, 0.50)],
            EngramSensitivityMethod.Sobol,
            EngramSensitivityMetric.BehavioralStability,
            SampleCount: 4,
            SamplingSeed: 9);

        EngramSensitivityReport report = EngramSensitivityRunner.Run(definition, new(Epoch));

        Assert.HasCount(1, report.Indices);
        Assert.IsNotNull(report.Indices[0].FirstOrder);
        Assert.IsNotNull(report.Indices[0].TotalOrder);
        Assert.HasCount(12, report.Evaluations);
    }

    [TestMethod]
    public void FactorialRunnerIncludesPairwiseInteractionAndCorrectedTests()
    {
        EngramFactorialReport report = EngramFactorialRunner.Run(
            new EngramFactorialDefinition(
                Experiment(),
                [EngramMechanism.RecallInhibition, EngramMechanism.PopulationDrift],
                2),
            new(Epoch));

        Assert.HasCount(3, report.Conditions);
        Assert.HasCount(3, report.AdjustedSelectivityTests);
        Assert.IsTrue(report.Conditions.Any(condition => condition.DisabledMechanisms.Count == 2));
    }

    [TestMethod]
    public void AllocationNullsKeepSeedsAndTurnoverMatched()
    {
        EngramNullControlReport report = EngramNullControlRunner.Run(
            new EngramNullControlDefinition(
                Experiment(),
                [EngramAllocationControl.Random, EngramAllocationControl.PermutedStimulus]),
            new(Epoch));

        Assert.HasCount(2, report.Conditions);
        foreach (EngramNullControlCondition condition in report.Conditions)
            CollectionAssert.AreEqual(report.Baseline.Runs.Select(run => run.Seed).ToArray(),
                condition.Experiment.Runs.Select(run => run.Seed).ToArray());
    }

    [TestMethod]
    public void OfflineReplayLinksPopulationsWithoutChangingEngramSize()
    {
        DynamicEngramOptions options = Options();
        var simulation = new EngramSimulation(Epoch, options);
        EngramSnapshot first = simulation.Encode(new("first",
            StimulusFactory.Sparse(options.NeuronCount, 16, 1)), Epoch);
        EngramSnapshot second = simulation.Encode(new("second",
            StimulusFactory.Sparse(options.NeuronCount, 16, 99)), Epoch.AddHours(1));

        EngramReplayResult replay = simulation.ReplayTogether(
            first.MemoryId, second.MemoryId, 0.25, Epoch.AddHours(6));

        Assert.IsGreaterThanOrEqualTo(replay.PopulationOverlapBefore, replay.PopulationOverlapAfter);
        Assert.HasCount(first.NeuronIds.Count, simulation.GetEngram(first.MemoryId).NeuronIds);
        Assert.HasCount(second.NeuronIds.Count, simulation.GetEngram(second.MemoryId).NeuronIds);
    }

    [TestMethod]
    public void ExperiencePhasesRemainDistinctAndRepresentationMetricsTrackDrift()
    {
        DynamicEngramOptions options = Options();
        var simulation = new EngramSimulation(Epoch, options);
        MemoryStimulus context = StimulusFactory.Sparse(options.NeuronCount, 16, 5);
        MemoryStimulus salient = StimulusFactory.Related(context, 0.5, 6);
        EngramExperienceResult experience = EngramExperienceEncoder.Encode(simulation, "acquisition", Epoch,
        [
            new("context", context, TimeSpan.Zero),
            new("salient", salient, TimeSpan.FromMinutes(5)),
            new("post-event", context, TimeSpan.FromMinutes(10), 0.5)
        ]);
        DynamicEngramState before = simulation.ExportState();
        simulation.AdvanceTo(Epoch.AddDays(20));
        DynamicEngramState after = simulation.ExportState();
        EngramRepresentationComparison comparison = EngramMetrics.CompareRepresentations(
            before, after, experience.Phases[0].Engram.MemoryId);

        Assert.HasCount(3, experience.Phases);
        Assert.AreEqual(3, experience.Phases.Select(phase => phase.Engram.MemoryId).Distinct().Count());
        Assert.IsInRange(0d, 1d, comparison.MembershipCosineSimilarity);
        Assert.IsInRange(0d, 1d, comparison.StimulusAlignmentAfter);
    }

    [TestMethod]
    public void InferenceProvidesIntervalsCorrectionAndPowerPlanning()
    {
        EngramPairedInference inference = EngramInference.Paired(
            [0.1, 0.2, 0.3, 0.4],
            [0.2, 0.3, 0.5, 0.6],
            resamples: 500);
        IReadOnlyList<EngramAdjustedPValue> adjusted = EngramInference.AdjustFalseDiscoveryRate(
        [
            new("a", 0.01),
            new("b", 0.04),
            new("c", 0.20)
        ]);

        Assert.IsLessThanOrEqualTo(inference.MeanDifference, inference.ConfidenceLower);
        Assert.IsGreaterThanOrEqualTo(inference.MeanDifference, inference.ConfidenceUpper);
        Assert.IsTrue(adjusted.All(test => test.AdjustedPValue >= test.RawPValue));
        Assert.IsGreaterThan(2, EngramInference.EstimatePairedSampleSize(0.5));
    }

    private static EngramExperimentDefinition Experiment() =>
        new(Options(), [1, 2, 3, 4], 16, 0.6, 0.25, 10);

    private static DynamicEngramOptions Options() => new()
    {
        NeuronCount = 80,
        DefaultEngramSize = 14,
        DriftFractionPerDay = 0.03,
        RecallIterations = 6,
        RandomSeed = 11
    };
}
