using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DynamicEngramSimulator;

/// <summary>Numerical model inputs supported by global sensitivity experiments.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EngramParameter>))]
public enum EngramParameter
{
    /// <summary>External stimulus contribution during allocation.</summary>
    StimulusWeight,
    /// <summary>Intrinsic excitability contribution during allocation.</summary>
    ExcitabilityWeight,
    /// <summary>Encoding-induced excitability increase.</summary>
    EncodingExcitabilityBoost,
    /// <summary>Daily relaxation rate toward baseline excitability.</summary>
    ExcitabilityRelaxationRatePerDay,
    /// <summary>Random allocation-score amplitude.</summary>
    AllocationNoise,
    /// <summary>Initial membership strength.</summary>
    HebbianStrength,
    /// <summary>Recurrent completion contribution.</summary>
    RecurrentGain,
    /// <summary>Activation-function slope.</summary>
    ActivationGain,
    /// <summary>Fraction of activation retained between iterations.</summary>
    ActivationLeak,
    /// <summary>Population-wide inhibitory contribution.</summary>
    GlobalInhibition,
    /// <summary>Learned pair-separation contribution.</summary>
    PairInhibitionRate,
    /// <summary>Expected daily membership turnover.</summary>
    DriftFractionPerDay,
    /// <summary>Unconsolidated membership half-life.</summary>
    MembershipHalfLifeDays,
    /// <summary>Daily consolidation rate.</summary>
    ConsolidationRatePerDay
}

/// <summary>Output selected as the sensitivity target.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EngramSensitivityMetric>))]
public enum EngramSensitivityMetric
{
    /// <summary>Mean final target-versus-competitor activation margin.</summary>
    FinalSelectivity,
    /// <summary>Mean Jaccard identity of initial and final populations.</summary>
    PopulationIdentity,
    /// <summary>Mean preservation of expected recall activation.</summary>
    BehavioralStability,
    /// <summary>Fraction of initially recalled memories still recalled after elapsed time.</summary>
    RecallSurvivalRate
}

/// <summary>Supported model-independent sensitivity designs.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EngramSensitivityMethod>))]
public enum EngramSensitivityMethod
{
    /// <summary>Elementary-effects screening for influential inputs.</summary>
    Morris,
    /// <summary>Variance decomposition with first-order and total-order indices.</summary>
    Sobol
}

/// <summary>Uniform uncertainty range for one model input.</summary>
public sealed record EngramParameterRange(EngramParameter Parameter, double Minimum, double Maximum);

/// <summary>Complete reproducible sensitivity design.</summary>
public sealed record EngramSensitivityDefinition(
    EngramExperimentDefinition Experiment,
    IReadOnlyList<EngramParameterRange> Parameters,
    EngramSensitivityMethod Method,
    EngramSensitivityMetric Metric,
    int SampleCount = 128,
    ulong SamplingSeed = 0x534F424F4CUL);

/// <summary>Influence estimates for one uncertain input.</summary>
public sealed record EngramSensitivityIndex(
    EngramParameter Parameter,
    double? FirstOrder,
    double? TotalOrder,
    double? MorrisMeanAbsoluteEffect,
    double? MorrisEffectStandardDeviation);

/// <summary>Raw output from one sampled model configuration.</summary>
public sealed record EngramSensitivityEvaluation(int Index, string Design, double[] UnitInputs, double Output);

/// <summary>Sensitivity indices plus every sampled output needed for auditing.</summary>
public sealed record EngramSensitivityReport(
    EngramExperimentManifest Manifest,
    EngramSensitivityDefinition Definition,
    double OutputMean,
    double OutputVariance,
    IReadOnlyList<EngramSensitivityIndex> Indices,
    IReadOnlyList<EngramSensitivityEvaluation> Evaluations);

/// <summary>Runs dependency-free Morris screening and variance-based Sobol analysis.</summary>
public static class EngramSensitivityRunner
{
    /// <summary>Executes the requested global design with common random seeds inside every model evaluation.</summary>
    public static EngramSensitivityReport Run(
        EngramSensitivityDefinition definition,
        EngramExperimentContext? context = null)
    {
        Validate(definition);
        DateTimeOffset created = (context?.CreatedAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        return definition.Method switch
        {
            EngramSensitivityMethod.Morris => RunMorris(definition, created, context?.CommitSha),
            EngramSensitivityMethod.Sobol => RunSobol(definition, created, context?.CommitSha),
            _ => throw new ArgumentOutOfRangeException(nameof(definition))
        };
    }

    /// <summary>Serializes indices, sampled inputs, outputs, and the reproducibility manifest.</summary>
    public static string ToJson(EngramSensitivityReport report) =>
        JsonSerializer.Serialize(report, EngramReportJson.Options);

    /// <summary>Exports one row per input parameter.</summary>
    public static string ToCsv(EngramSensitivityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var output = new StringBuilder(
            "parameter,first_order,total_order,morris_mean_absolute,morris_standard_deviation\n");
        foreach (EngramSensitivityIndex index in report.Indices)
            output.AppendLine(string.Join(',', index.Parameter, Format(index.FirstOrder), Format(index.TotalOrder),
                Format(index.MorrisMeanAbsoluteEffect), Format(index.MorrisEffectStandardDeviation)));
        return output.ToString();
    }

    private static EngramSensitivityReport RunSobol(
        EngramSensitivityDefinition definition,
        DateTimeOffset created,
        string? commit)
    {
        int dimension = definition.Parameters.Count;
        ulong random = definition.SamplingSeed;
        var a = Matrix(definition.SampleCount, dimension, ref random);
        var b = Matrix(definition.SampleCount, dimension, ref random);
        var evaluations = new List<EngramSensitivityEvaluation>(definition.SampleCount * (dimension + 2));
        var yA = new double[definition.SampleCount];
        var yB = new double[definition.SampleCount];
        for (int row = 0; row < definition.SampleCount; row++)
        {
            yA[row] = Evaluate(definition, a[row]);
            yB[row] = Evaluate(definition, b[row]);
            evaluations.Add(new(row, "A", a[row], yA[row]));
            evaluations.Add(new(row, "B", b[row], yB[row]));
        }

        double mean = yA.Concat(yB).Average();
        double variance = yA.Concat(yB).Average(value => Math.Pow(value - mean, 2));
        var indices = new List<EngramSensitivityIndex>(dimension);
        for (int parameter = 0; parameter < dimension; parameter++)
        {
            var mixedOutputs = new double[definition.SampleCount];
            for (int row = 0; row < definition.SampleCount; row++)
            {
                double[] mixed = (double[])a[row].Clone();
                mixed[parameter] = b[row][parameter];
                mixedOutputs[row] = Evaluate(definition, mixed);
                evaluations.Add(new(row, $"AB{parameter}", mixed, mixedOutputs[row]));
            }

            double first = variance <= 1e-15 ? 0 : Enumerable.Range(0, definition.SampleCount)
                .Average(row => yB[row] * (mixedOutputs[row] - yA[row])) / variance;
            double total = variance <= 1e-15 ? 0 : 0.5 * Enumerable.Range(0, definition.SampleCount)
                .Average(row => Math.Pow(yA[row] - mixedOutputs[row], 2)) / variance;
            indices.Add(new EngramSensitivityIndex(
                definition.Parameters[parameter].Parameter,
                first,
                total,
                null,
                null));
        }

        return Report(definition, created, commit, mean, variance, indices, evaluations);
    }

    private static EngramSensitivityReport RunMorris(
        EngramSensitivityDefinition definition,
        DateTimeOffset created,
        string? commit)
    {
        const double delta = 0.1;
        int dimension = definition.Parameters.Count;
        ulong random = definition.SamplingSeed;
        var effects = Enumerable.Range(0, dimension).Select(_ => new List<double>()).ToArray();
        var evaluations = new List<EngramSensitivityEvaluation>(definition.SampleCount * (dimension + 1));
        var outputs = new List<double>(evaluations.Capacity);
        for (int sample = 0; sample < definition.SampleCount; sample++)
        {
            double[] baseline = Enumerable.Range(0, dimension)
                .Select(_ => NextDouble(ref random) * (1 - delta)).ToArray();
            double y0 = Evaluate(definition, baseline);
            outputs.Add(y0);
            evaluations.Add(new(sample, "M0", baseline, y0));
            for (int parameter = 0; parameter < dimension; parameter++)
            {
                double[] changed = (double[])baseline.Clone();
                changed[parameter] += delta;
                double y1 = Evaluate(definition, changed);
                effects[parameter].Add((y1 - y0) / delta);
                outputs.Add(y1);
                evaluations.Add(new(sample, $"M{parameter + 1}", changed, y1));
            }
        }

        double mean = outputs.Average();
        double variance = outputs.Average(value => Math.Pow(value - mean, 2));
        var indices = effects.Select((values, index) =>
        {
            double effectMean = values.Average();
            double deviation = values.Count < 2 ? 0 :
                Math.Sqrt(values.Sum(value => Math.Pow(value - effectMean, 2)) / (values.Count - 1));
            return new EngramSensitivityIndex(
                definition.Parameters[index].Parameter,
                null,
                null,
                values.Average(Math.Abs),
                deviation);
        }).ToArray();
        return Report(definition, created, commit, mean, variance, indices, evaluations);
    }

    private static EngramSensitivityReport Report(
        EngramSensitivityDefinition definition,
        DateTimeOffset created,
        string? commit,
        double mean,
        double variance,
        IReadOnlyList<EngramSensitivityIndex> indices,
        IReadOnlyList<EngramSensitivityEvaluation> evaluations) =>
        new(
            EngramExperimentRunner.CreateManifest(definition, created, commit),
            definition,
            mean,
            variance,
            indices,
            evaluations);

    private static double Evaluate(EngramSensitivityDefinition definition, double[] unitInputs)
    {
        DynamicEngramOptions options = definition.Experiment.BaseOptions;
        for (int index = 0; index < unitInputs.Length; index++)
        {
            EngramParameterRange range = definition.Parameters[index];
            options = Set(options, range.Parameter,
                range.Minimum + (unitInputs[index] * (range.Maximum - range.Minimum)));
        }

        EngramExperimentReport report = EngramExperimentRunner.Run(definition.Experiment with { BaseOptions = options },
            new EngramExperimentContext(new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero), "sensitivity"));
        return definition.Metric switch
        {
            EngramSensitivityMetric.FinalSelectivity => report.Runs.Average(run => run.FinalSelectivity),
            EngramSensitivityMetric.PopulationIdentity => report.MeanPopulationIdentity,
            EngramSensitivityMetric.BehavioralStability => report.MeanBehavioralStability,
            EngramSensitivityMetric.RecallSurvivalRate => report.RecallSurvivalRate,
            _ => throw new ArgumentOutOfRangeException(nameof(definition))
        };
    }

    private static DynamicEngramOptions Set(DynamicEngramOptions options, EngramParameter parameter, double value) =>
        parameter switch
        {
            EngramParameter.StimulusWeight => options with { StimulusWeight = value },
            EngramParameter.ExcitabilityWeight => options with { ExcitabilityWeight = value },
            EngramParameter.EncodingExcitabilityBoost => options with { EncodingExcitabilityBoost = value },
            EngramParameter.ExcitabilityRelaxationRatePerDay => options with { ExcitabilityRelaxationRatePerDay = value },
            EngramParameter.AllocationNoise => options with { AllocationNoise = value },
            EngramParameter.HebbianStrength => options with { HebbianStrength = value },
            EngramParameter.RecurrentGain => options with { RecurrentGain = value },
            EngramParameter.ActivationGain => options with { ActivationGain = value },
            EngramParameter.ActivationLeak => options with { ActivationLeak = value },
            EngramParameter.GlobalInhibition => options with { GlobalInhibition = value },
            EngramParameter.PairInhibitionRate => options with { PairInhibitionRate = value },
            EngramParameter.DriftFractionPerDay => options with { DriftFractionPerDay = value },
            EngramParameter.MembershipHalfLifeDays => options with { MembershipHalfLifeDays = value },
            EngramParameter.ConsolidationRatePerDay => options with { ConsolidationRatePerDay = value },
            _ => throw new ArgumentOutOfRangeException(nameof(parameter))
        };

    private static void Validate(EngramSensitivityDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(definition.Experiment);
        ArgumentNullException.ThrowIfNull(definition.Parameters);
        if (definition.SampleCount is < 4 or > 100_000 || definition.Parameters.Count == 0 ||
            definition.Parameters.Select(range => range.Parameter).Distinct().Count() != definition.Parameters.Count ||
            definition.Parameters.Any(range => !double.IsFinite(range.Minimum) || !double.IsFinite(range.Maximum) ||
                                               range.Minimum < 0 || range.Maximum <= range.Minimum))
            throw new ArgumentException("Sensitivity ranges and sample count are invalid.", nameof(definition));
        // Validate both ends through the same model contract used by execution.
        foreach (EngramParameterRange range in definition.Parameters)
        {
            Set(definition.Experiment.BaseOptions, range.Parameter, range.Minimum).Validate();
            Set(definition.Experiment.BaseOptions, range.Parameter, range.Maximum).Validate();
        }
    }

    private static double[][] Matrix(int rows, int columns, ref ulong random)
    {
        var matrix = new double[rows][];
        for (int row = 0; row < rows; row++)
        {
            matrix[row] = new double[columns];
            for (int column = 0; column < columns; column++)
                matrix[row][column] = NextDouble(ref random);
        }
        return matrix;
    }

    private static string Format(double? value) =>
        value?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty;

    private static double NextDouble(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        ulong value = state;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        value ^= value >> 31;
        return (value >> 11) * (1.0 / (1UL << 53));
    }
}
