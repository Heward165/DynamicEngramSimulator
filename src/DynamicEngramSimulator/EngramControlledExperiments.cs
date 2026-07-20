using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DynamicEngramSimulator;

/// <summary>Mechanism combinations evaluated against one paired baseline.</summary>
public sealed record EngramFactorialDefinition(
    EngramExperimentDefinition Baseline,
    IReadOnlyList<EngramMechanism> Mechanisms,
    int MaximumInteractionOrder = 2);

/// <summary>One factorial condition with complete seed-level outcomes.</summary>
public sealed record EngramFactorialCondition(
    string Name,
    IReadOnlyList<EngramMechanism> DisabledMechanisms,
    EngramExperimentReport Experiment,
    EngramPairedInference FinalSelectivityInference);

/// <summary>Factorial ablation report capable of exposing mechanism interactions.</summary>
public sealed record EngramFactorialReport(
    EngramExperimentManifest Manifest,
    EngramFactorialDefinition Definition,
    EngramExperimentReport Baseline,
    IReadOnlyList<EngramFactorialCondition> Conditions,
    IReadOnlyList<EngramAdjustedPValue> AdjustedSelectivityTests);

/// <summary>Runs paired single and multi-mechanism ablations.</summary>
public static class EngramFactorialRunner
{
    /// <summary>Runs every combination through the requested interaction order.</summary>
    public static EngramFactorialReport Run(
        EngramFactorialDefinition definition,
        EngramExperimentContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(definition.Baseline);
        ArgumentNullException.ThrowIfNull(definition.Mechanisms);
        EngramMechanism[] mechanisms = definition.Mechanisms.Distinct().Order().ToArray();
        if (mechanisms.Length == 0 || definition.MaximumInteractionOrder is < 1 ||
            definition.MaximumInteractionOrder > mechanisms.Length)
            throw new ArgumentOutOfRangeException(nameof(definition));

        DateTimeOffset created = (context?.CreatedAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        EngramExperimentDefinition baselineDefinition = definition.Baseline with
        {
            BaseOptions = definition.Baseline.BaseOptions with { Mechanisms = EngramMechanismProfile.Default }
        };
        EngramExperimentReport baseline = EngramExperimentRunner.Run(baselineDefinition,
            new(created, context?.CommitSha));
        double[] controls = baseline.Runs.Select(run => run.FinalSelectivity).ToArray();
        var conditions = new List<EngramFactorialCondition>();
        foreach (EngramMechanism[] disabled in Combinations(mechanisms, definition.MaximumInteractionOrder))
        {
            EngramMechanismProfile profile = EngramMechanismProfile.Default.Without(disabled);
            EngramExperimentReport experiment = EngramExperimentRunner.Run(
                baselineDefinition with
                {
                    BaseOptions = baselineDefinition.BaseOptions with { Mechanisms = profile }
                },
                new(created, context?.CommitSha));
            EngramPairedInference inference = EngramInference.Paired(
                controls,
                experiment.Runs.Select(run => run.FinalSelectivity).ToArray(),
                seed: 0xFAC70UL,
                resamples: 2_000);
            conditions.Add(new EngramFactorialCondition(profile.Id, disabled, experiment, inference));
        }

        IReadOnlyList<EngramAdjustedPValue> adjusted = EngramInference.AdjustFalseDiscoveryRate(
            conditions.Select(condition =>
                new KeyValuePair<string, double>(condition.Name, condition.FinalSelectivityInference.TwoSidedPValue)));
        return new EngramFactorialReport(
            EngramExperimentRunner.CreateManifest(definition, created, context?.CommitSha),
            definition,
            baseline,
            conditions,
            adjusted);
    }

    /// <summary>Serializes raw runs, inferences, and adjusted tests.</summary>
    public static string ToJson(EngramFactorialReport report) =>
        JsonSerializer.Serialize(report, EngramReportJson.Options);

    /// <summary>Exports one row per mechanism combination.</summary>
    public static string ToCsv(EngramFactorialReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var adjusted = report.AdjustedSelectivityTests.ToDictionary(test => test.Name);
        var output = new StringBuilder(
            "condition,order,pairs,mean_selectivity_change,effect_size,ci_lower,ci_upper,p_value,adjusted_p_value\n");
        foreach (EngramFactorialCondition condition in report.Conditions)
        {
            EngramPairedInference value = condition.FinalSelectivityInference;
            output.AppendLine(string.Join(',', condition.Name, condition.DisabledMechanisms.Count,
                value.PairCount, Format(value.MeanDifference), Format(value.StandardizedEffect),
                Format(value.ConfidenceLower), Format(value.ConfidenceUpper),
                Format(value.TwoSidedPValue), Format(adjusted[condition.Name].AdjustedPValue)));
        }

        return output.ToString();
    }

    private static IEnumerable<EngramMechanism[]> Combinations(EngramMechanism[] values, int maximumOrder)
    {
        for (int order = 1; order <= maximumOrder; order++)
            foreach (EngramMechanism[] combination in Choose(values, order, 0, []))
                yield return combination;
    }

    private static IEnumerable<EngramMechanism[]> Choose(
        EngramMechanism[] values,
        int remaining,
        int start,
        List<EngramMechanism> chosen)
    {
        if (remaining == 0)
        {
            yield return chosen.ToArray();
            yield break;
        }

        for (int index = start; index <= values.Length - remaining; index++)
        {
            chosen.Add(values[index]);
            foreach (EngramMechanism[] result in Choose(values, remaining - 1, index + 1, chosen))
                yield return result;
            chosen.RemoveAt(chosen.Count - 1);
        }
    }

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}

/// <summary>Matched allocation-null experiment definition.</summary>
public sealed record EngramNullControlDefinition(
    EngramExperimentDefinition Baseline,
    IReadOnlyList<EngramAllocationControl> Controls);

/// <summary>One null distribution compared seed-for-seed with mechanistic allocation.</summary>
public sealed record EngramNullControlCondition(
    EngramAllocationControl Control,
    EngramExperimentReport Experiment,
    EngramPairedInference FinalSelectivityInference,
    EngramPairedInference BehavioralStabilityInference);

/// <summary>Complete null-control report.</summary>
public sealed record EngramNullControlReport(
    EngramExperimentManifest Manifest,
    EngramNullControlDefinition Definition,
    EngramExperimentReport Baseline,
    IReadOnlyList<EngramNullControlCondition> Conditions);

/// <summary>Runs random and permuted-stimulus controls with identical seeds and turnover.</summary>
public static class EngramNullControlRunner
{
    /// <summary>Runs every requested null against normal mechanistic allocation.</summary>
    public static EngramNullControlReport Run(
        EngramNullControlDefinition definition,
        EngramExperimentContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(definition.Baseline);
        ArgumentNullException.ThrowIfNull(definition.Controls);
        EngramAllocationControl[] controls = definition.Controls.Distinct().ToArray();
        if (controls.Length == 0 || controls.Contains(EngramAllocationControl.Mechanistic))
            throw new ArgumentException("Null controls must be non-empty and non-mechanistic.", nameof(definition));

        DateTimeOffset created = (context?.CreatedAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        EngramExperimentDefinition baselineDefinition = definition.Baseline with
        {
            BaseOptions = definition.Baseline.BaseOptions with
            {
                AllocationControl = EngramAllocationControl.Mechanistic
            }
        };
        EngramExperimentReport baseline = EngramExperimentRunner.Run(baselineDefinition,
            new(created, context?.CommitSha));
        var conditions = controls.Select(control =>
        {
            EngramExperimentReport experiment = EngramExperimentRunner.Run(
                baselineDefinition with
                {
                    BaseOptions = baselineDefinition.BaseOptions with { AllocationControl = control }
                },
                new(created, context?.CommitSha));
            return new EngramNullControlCondition(
                control,
                experiment,
                EngramInference.Paired(
                    baseline.Runs.Select(run => run.FinalSelectivity).ToArray(),
                    experiment.Runs.Select(run => run.FinalSelectivity).ToArray(),
                    0x4E554C4CUL,
                    2_000),
                EngramInference.Paired(
                    baseline.Runs.Select(run => run.BehavioralStability).ToArray(),
                    experiment.Runs.Select(run => run.BehavioralStability).ToArray(),
                    0x4E554C4DUL,
                    2_000));
        }).ToArray();
        return new EngramNullControlReport(
            EngramExperimentRunner.CreateManifest(definition, created, context?.CommitSha),
            definition,
            baseline,
            conditions);
    }

    /// <summary>Serializes complete raw null distributions.</summary>
    public static string ToJson(EngramNullControlReport report) =>
        JsonSerializer.Serialize(report, EngramReportJson.Options);

    /// <summary>Exports paired selectivity and behavioral null effects.</summary>
    public static string ToCsv(EngramNullControlReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var output = new StringBuilder(
            "control,pairs,mean_selectivity_change,selectivity_p_value,mean_behavior_change,behavior_p_value\n");
        foreach (EngramNullControlCondition condition in report.Conditions)
            output.AppendLine(string.Join(',', condition.Control,
                condition.FinalSelectivityInference.PairCount,
                condition.FinalSelectivityInference.MeanDifference.ToString("R", CultureInfo.InvariantCulture),
                condition.FinalSelectivityInference.TwoSidedPValue.ToString("R", CultureInfo.InvariantCulture),
                condition.BehavioralStabilityInference.MeanDifference.ToString("R", CultureInfo.InvariantCulture),
                condition.BehavioralStabilityInference.TwoSidedPValue.ToString("R", CultureInfo.InvariantCulture)));
        return output.ToString();
    }
}
