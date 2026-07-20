using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DynamicEngramSimulator.Tests;

[TestClass]
public sealed class PortfolioExperimentTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 17, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void AblationsUsePairedSeedsAndProduceAuditableEffects()
    {
        var baseline = new EngramExperimentDefinition(Options(), [1, 2, 3], 18, 0.65, 0.25, 10);
        var definition = new EngramAblationDefinition(baseline, Enum.GetValues<EngramMechanism>());
        EngramAblationReport report = EngramAblationRunner.Run(definition, new(CreatedAt, "test-commit"));

        Assert.HasCount(7, report.Conditions);
        Assert.HasCount(6, report.Effects);
        Assert.AreEqual("test-commit", report.Manifest.CommitSha);
        foreach (EngramAblationCondition condition in report.Conditions)
            CollectionAssert.AreEqual(new ulong[] { 1, 2, 3 }, condition.Experiment.Runs.Select(run => run.Seed).ToArray());
        StringAssert.Contains(EngramAblationRunner.ToCsv(report), "RecallInhibition");
    }

    [TestMethod]
    public void JsonPlanRoundTripsAndCreatesThreeArtifactFormats()
    {
        var plan = new EngramExperimentPlan(
            "ablation-test",
            EngramExperimentKind.Ablation,
            Options(),
            [4, 5, 6],
            CreatedAt,
            ActiveStimulusChannels: 18,
            Ablations: [EngramMechanism.RecallInhibition, EngramMechanism.PopulationDrift]);

        EngramExperimentPlan parsed = EngramExperimentPlanRunner.Parse(EngramExperimentPlanRunner.ToJson(plan));
        EngramExperimentArtifact artifact = EngramExperimentPlanRunner.Run(parsed);

        Assert.AreEqual(plan.Name, parsed.Name);
        StringAssert.Contains(artifact.Json, "definitionSha256");
        StringAssert.StartsWith(artifact.Csv, "ablation,");
        StringAssert.StartsWith(artifact.Svg, "<svg");
    }

    [TestMethod]
    public void TemporalLinkingCoversEverySeedDelayPair()
    {
        var definition = new TemporalLinkingDefinition(Options(), [10, 11], [0, 6, 24, 72], 18, 0.25, 0.25);
        TemporalLinkingReport report = TemporalLinkingRunner.Run(definition, new(CreatedAt));

        Assert.HasCount(8, report.Runs);
        Assert.HasCount(4, report.Delays);
        Assert.IsTrue(report.Delays.All(summary => summary.PopulationOverlap.Count == 2));
    }

    [TestMethod]
    public void HistoricalCheckpointUpgradesAndCurrentCheckpointRestoresAllOptions()
    {
        DynamicEngramOptions options = Options() with
        {
            Mechanisms = EngramMechanismProfile.Default.Without(EngramMechanism.PopulationDrift)
        };
        var simulation = new EngramSimulation(CreatedAt, options);
        simulation.Encode(new("memory", StimulusFactory.Sparse(options.NeuronCount, 18, 1)), CreatedAt);
        DynamicEngramState versionThree = simulation.ExportState();
        Assert.AreEqual(options, versionThree.Options);
        EngramSimulation restored = EngramSimulation.FromState(versionThree);
        Assert.AreEqual(simulation.ToJson(), restored.ToJson());

        DynamicEngramState versionOne = versionThree with
        {
            SchemaVersion = 1,
            MechanismProfileId = EngramMechanismProfile.Default.Id,
            Mechanisms = EngramMechanismProfile.Default,
            Options = null
        };
        DynamicEngramState upgraded = EngramStateMigrator.Upgrade(versionOne);
        Assert.AreEqual(EngramStateMigrator.CurrentSchemaVersion, upgraded.SchemaVersion);
        Assert.IsNull(upgraded.Options);
        Assert.ThrowsExactly<ArgumentException>(() => EngramSimulation.FromState(upgraded));
        _ = EngramSimulation.FromState(upgraded, Options());
    }

    [TestMethod]
    public void BootstrapStatisticsAreDeterministic()
    {
        double[] values = [0.1, 0.2, 0.4, 0.8, 0.9];
        EngramStatisticalSummary first = EngramStatistics.Summarize(values, 42, 500);
        EngramStatisticalSummary second = EngramStatistics.Summarize(values, 42, 500);
        Assert.AreEqual(first, second);
        Assert.IsLessThanOrEqualTo(first.Mean, first.ConfidenceLower);
        Assert.IsGreaterThanOrEqualTo(first.Mean, first.ConfidenceUpper);
    }

    private static DynamicEngramOptions Options() => new()
    {
        NeuronCount = 96,
        DefaultEngramSize = 16,
        DriftFractionPerDay = 0.02,
        RandomSeed = 7
    };
}
