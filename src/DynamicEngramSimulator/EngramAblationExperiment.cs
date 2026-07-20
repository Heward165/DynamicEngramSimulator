using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DynamicEngramSimulator;

/// <summary>Definition of paired baseline-versus-ablation seed sweeps.</summary>
public sealed record EngramAblationDefinition(
    EngramExperimentDefinition Baseline,
    IReadOnlyList<EngramMechanism> Ablations);

/// <summary>Paired change produced by disabling one mechanism.</summary>
public sealed record EngramPairedEffect(
    EngramMechanism Ablation,
    int PairCount,
    double MeanSelectivityChange,
    double SelectivityEffectSize,
    double MeanPopulationIdentityChange,
    double MeanBehavioralStabilityChange,
    double RecallSurvivalRateChange);

/// <summary>One baseline or ablated condition and its full raw report.</summary>
public sealed record EngramAblationCondition(
    string Name,
    EngramMechanism? DisabledMechanism,
    EngramExperimentReport Experiment);

/// <summary>Complete paired ablation report.</summary>
public sealed record EngramAblationReport(
    EngramExperimentManifest Manifest,
    EngramAblationDefinition Definition,
    IReadOnlyList<EngramAblationCondition> Conditions,
    IReadOnlyList<EngramPairedEffect> Effects);

/// <summary>Runs controlled mechanism ablations with identical seeds and stimuli.</summary>
public static class EngramAblationRunner
{
    /// <summary>Runs the baseline and every requested single-mechanism ablation.</summary>
    public static EngramAblationReport Run(
        EngramAblationDefinition definition,
        EngramExperimentContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(definition.Baseline);
        ArgumentNullException.ThrowIfNull(definition.Ablations);
        if (definition.Ablations.Count == 0 || definition.Ablations.Distinct().Count() != definition.Ablations.Count)
            throw new ArgumentException("Ablations must be non-empty and unique.", nameof(definition));

        DateTimeOffset createdAt = (context?.CreatedAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        EngramExperimentDefinition baselineDefinition = definition.Baseline with
        {
            BaseOptions = definition.Baseline.BaseOptions with
            {
                Mechanisms = EngramMechanismProfile.Default
            }
        };
        EngramExperimentReport baseline = EngramExperimentRunner.Run(
            baselineDefinition,
            new(createdAt, context?.CommitSha));
        var conditions = new List<EngramAblationCondition>
        {
            new("baseline", null, baseline)
        };
        var effects = new List<EngramPairedEffect>();

        foreach (EngramMechanism mechanism in definition.Ablations)
        {
            EngramMechanismProfile profile = EngramMechanismProfile.Default.Without(mechanism);
            EngramExperimentDefinition ablatedDefinition = baselineDefinition with
            {
                BaseOptions = baselineDefinition.BaseOptions with { Mechanisms = profile }
            };
            EngramExperimentReport ablated = EngramExperimentRunner.Run(
                ablatedDefinition,
                new(createdAt, context?.CommitSha));
            conditions.Add(new(profile.Id, mechanism, ablated));
            effects.Add(Compare(mechanism, baseline, ablated));
        }

        return new(
            EngramExperimentRunner.CreateManifest(definition, createdAt, context?.CommitSha),
            definition,
            conditions,
            effects);
    }

    /// <summary>Serializes the complete ablation report.</summary>
    public static string ToJson(EngramAblationReport report) =>
        JsonSerializer.Serialize(report, EngramReportJson.Options);

    /// <summary>Exports one summary row per ablation.</summary>
    public static string ToCsv(EngramAblationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var output = new StringBuilder(
            "ablation,pairs,mean_selectivity_change,selectivity_effect_size,mean_identity_change,mean_behavior_change,recall_survival_change\n");
        foreach (EngramPairedEffect effect in report.Effects)
        {
            output.AppendLine(string.Join(',', effect.Ablation, effect.PairCount,
                Format(effect.MeanSelectivityChange),
                Format(effect.SelectivityEffectSize),
                Format(effect.MeanPopulationIdentityChange),
                Format(effect.MeanBehavioralStabilityChange),
                Format(effect.RecallSurvivalRateChange)));
        }

        return output.ToString();
    }

    private static EngramPairedEffect Compare(
        EngramMechanism mechanism,
        EngramExperimentReport baseline,
        EngramExperimentReport ablated)
    {
        Dictionary<ulong, EngramExperimentRun> bySeed = ablated.Runs.ToDictionary(run => run.Seed);
        var selectivity = new List<double>(baseline.Runs.Count);
        var identity = new List<double>(baseline.Runs.Count);
        var behavior = new List<double>(baseline.Runs.Count);
        var survival = new List<double>(baseline.Runs.Count);
        foreach (EngramExperimentRun control in baseline.Runs)
        {
            if (!bySeed.TryGetValue(control.Seed, out EngramExperimentRun? treatment))
                throw new ArgumentException("Ablation reports must contain identical seeds.");
            selectivity.Add(treatment.FinalSelectivity - control.FinalSelectivity);
            identity.Add(treatment.PopulationIdentity - control.PopulationIdentity);
            behavior.Add(treatment.BehavioralStability - control.BehavioralStability);
            survival.Add((treatment.FinalRecallSucceeded ? 1 : 0) - (control.FinalRecallSucceeded ? 1 : 0));
        }

        double mean = selectivity.Average();
        double sampleDeviation = selectivity.Count < 2
            ? 0
            : Math.Sqrt(selectivity.Sum(value => Math.Pow(value - mean, 2)) / (selectivity.Count - 1));
        double effectSize = sampleDeviation == 0 ? 0 : mean / sampleDeviation;
        return new(mechanism, selectivity.Count, mean, effectSize,
            identity.Average(), behavior.Average(), survival.Average());
    }

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
