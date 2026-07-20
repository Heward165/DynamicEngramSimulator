using System.Text.Json;
using System.Text.Json.Serialization;

namespace DynamicEngramSimulator;

/// <summary>Experiment families available to the JSON plan runner.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EngramExperimentKind>))]
public enum EngramExperimentKind
{
    /// <summary>Population drift and behavioral stability across seeds.</summary>
    Stability,

    /// <summary>Paired single-mechanism ablations.</summary>
    Ablation,

    /// <summary>Population overlap across inter-event delays.</summary>
    TemporalLinking,

    /// <summary>Single and interacting mechanism ablations.</summary>
    Factorial,

    /// <summary>Random and permuted-stimulus allocation controls.</summary>
    NullControls,

    /// <summary>Morris or Sobol global sensitivity analysis.</summary>
    Sensitivity
}

/// <summary>A serializable, executable experiment specification.</summary>
public sealed record EngramExperimentPlan(
    string Name,
    EngramExperimentKind Kind,
    DynamicEngramOptions Options,
    ulong[] Seeds,
    DateTimeOffset CreatedAt,
    int ActiveStimulusChannels = 24,
    double RelatedStimulusRetention = 0.75,
    double CueFraction = 0.20,
    double ElapsedDays = 30,
    EngramMechanism[]? Ablations = null,
    double[]? DelaysHours = null,
    int MaximumInteractionOrder = 2,
    EngramAllocationControl[]? NullControls = null,
    EngramParameterRange[]? ParameterRanges = null,
    EngramSensitivityMethod SensitivityMethod = EngramSensitivityMethod.Morris,
    EngramSensitivityMetric SensitivityMetric = EngramSensitivityMetric.FinalSelectivity,
    int SensitivitySamples = 32);

/// <summary>Machine-readable and visual artifacts produced from one plan.</summary>
public sealed record EngramExperimentArtifact(
    string Name,
    EngramExperimentKind Kind,
    string Json,
    string Csv,
    string Svg);

/// <summary>Parses and executes complete experiment plans without recompilation.</summary>
public static class EngramExperimentPlanRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Deserializes and validates a JSON plan.</summary>
    public static EngramExperimentPlan Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        EngramExperimentPlan plan = JsonSerializer.Deserialize<EngramExperimentPlan>(json, JsonOptions)
            ?? throw new ArgumentException("Experiment plan is empty.", nameof(json));
        Validate(plan);
        return plan;
    }

    /// <summary>Serializes a plan using readable enum names.</summary>
    public static string ToJson(EngramExperimentPlan plan)
    {
        Validate(plan);
        return JsonSerializer.Serialize(plan, JsonOptions);
    }

    /// <summary>Executes a parsed plan and returns JSON, CSV, and SVG artifacts.</summary>
    public static EngramExperimentArtifact Run(EngramExperimentPlan plan)
    {
        Validate(plan);
        var context = new EngramExperimentContext(plan.CreatedAt);
        return plan.Kind switch
        {
            EngramExperimentKind.Stability => Stability(plan, context),
            EngramExperimentKind.Ablation => Ablation(plan, context),
            EngramExperimentKind.TemporalLinking => TemporalLinking(plan, context),
            EngramExperimentKind.Factorial => Factorial(plan, context),
            EngramExperimentKind.NullControls => NullControls(plan, context),
            EngramExperimentKind.Sensitivity => Sensitivity(plan, context),
            _ => throw new ArgumentOutOfRangeException(nameof(plan))
        };
    }

    private static EngramExperimentArtifact Stability(
        EngramExperimentPlan plan,
        EngramExperimentContext context)
    {
        var definition = new EngramExperimentDefinition(plan.Options, plan.Seeds,
            plan.ActiveStimulusChannels, plan.RelatedStimulusRetention,
            plan.CueFraction, plan.ElapsedDays);
        EngramExperimentReport report = EngramExperimentRunner.Run(definition, context);
        return new(plan.Name, plan.Kind,
            EngramExperimentRunner.ToJson(report),
            EngramExperimentRunner.ToCsv(report),
            EngramSvgReport.Stability(report));
    }

    private static EngramExperimentArtifact Ablation(
        EngramExperimentPlan plan,
        EngramExperimentContext context)
    {
        var baseline = new EngramExperimentDefinition(plan.Options, plan.Seeds,
            plan.ActiveStimulusChannels, plan.RelatedStimulusRetention,
            plan.CueFraction, plan.ElapsedDays);
        var definition = new EngramAblationDefinition(baseline, plan.Ablations!);
        EngramAblationReport report = EngramAblationRunner.Run(definition, context);
        return new(plan.Name, plan.Kind,
            EngramAblationRunner.ToJson(report),
            EngramAblationRunner.ToCsv(report),
            EngramSvgReport.Ablations(report));
    }

    private static EngramExperimentArtifact TemporalLinking(
        EngramExperimentPlan plan,
        EngramExperimentContext context)
    {
        var definition = new TemporalLinkingDefinition(plan.Options, plan.Seeds,
            plan.DelaysHours!, plan.ActiveStimulusChannels,
            plan.RelatedStimulusRetention, plan.CueFraction);
        TemporalLinkingReport report = TemporalLinkingRunner.Run(definition, context);
        return new(plan.Name, plan.Kind,
            TemporalLinkingRunner.ToJson(report),
            TemporalLinkingRunner.ToCsv(report),
            EngramSvgReport.TemporalLinking(report));
    }

    private static EngramExperimentArtifact Factorial(
        EngramExperimentPlan plan,
        EngramExperimentContext context)
    {
        var baseline = Definition(plan);
        EngramFactorialReport report = EngramFactorialRunner.Run(
            new EngramFactorialDefinition(baseline, plan.Ablations!, plan.MaximumInteractionOrder), context);
        return new(plan.Name, plan.Kind,
            EngramFactorialRunner.ToJson(report),
            EngramFactorialRunner.ToCsv(report),
            EngramSvgReport.Factorial(report));
    }

    private static EngramExperimentArtifact NullControls(
        EngramExperimentPlan plan,
        EngramExperimentContext context)
    {
        EngramNullControlReport report = EngramNullControlRunner.Run(
            new EngramNullControlDefinition(Definition(plan), plan.NullControls!), context);
        return new(plan.Name, plan.Kind,
            EngramNullControlRunner.ToJson(report),
            EngramNullControlRunner.ToCsv(report),
            EngramSvgReport.NullControls(report));
    }

    private static EngramExperimentArtifact Sensitivity(
        EngramExperimentPlan plan,
        EngramExperimentContext context)
    {
        EngramSensitivityReport report = EngramSensitivityRunner.Run(
            new EngramSensitivityDefinition(Definition(plan), plan.ParameterRanges!,
                plan.SensitivityMethod, plan.SensitivityMetric, plan.SensitivitySamples), context);
        return new(plan.Name, plan.Kind,
            EngramSensitivityRunner.ToJson(report),
            EngramSensitivityRunner.ToCsv(report),
            EngramSvgReport.Sensitivity(report));
    }

    private static EngramExperimentDefinition Definition(EngramExperimentPlan plan) =>
        new(plan.Options, plan.Seeds, plan.ActiveStimulusChannels,
            plan.RelatedStimulusRetention, plan.CueFraction, plan.ElapsedDays);

    private static void Validate(EngramExperimentPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(plan.Name);
        ArgumentNullException.ThrowIfNull(plan.Options);
        ArgumentNullException.ThrowIfNull(plan.Seeds);
        plan.Options.Validate();
        if (plan.Seeds.Length == 0 || plan.Seeds.Distinct().Count() != plan.Seeds.Length)
            throw new ArgumentException("Plan seeds must be non-empty and unique.", nameof(plan));
        if (plan.CreatedAt == default)
            throw new ArgumentOutOfRangeException(nameof(plan));
        if (plan.Kind == EngramExperimentKind.Ablation && (plan.Ablations is null || plan.Ablations.Length == 0))
            throw new ArgumentException("An ablation plan requires mechanisms.", nameof(plan));
        if (plan.Kind == EngramExperimentKind.TemporalLinking && (plan.DelaysHours is null || plan.DelaysHours.Length == 0))
            throw new ArgumentException("A temporal-linking plan requires delays.", nameof(plan));
        if (plan.Kind == EngramExperimentKind.Factorial && (plan.Ablations is null || plan.Ablations.Length == 0))
            throw new ArgumentException("A factorial plan requires mechanisms.", nameof(plan));
        if (plan.Kind == EngramExperimentKind.NullControls && (plan.NullControls is null || plan.NullControls.Length == 0))
            throw new ArgumentException("A null-control plan requires controls.", nameof(plan));
        if (plan.Kind == EngramExperimentKind.Sensitivity && (plan.ParameterRanges is null || plan.ParameterRanges.Length == 0))
            throw new ArgumentException("A sensitivity plan requires parameter ranges.", nameof(plan));
    }
}
