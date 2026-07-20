using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DynamicEngramSimulator;

/// <summary>Configuration for a repeatable multi-seed stability experiment.</summary>
public sealed record EngramExperimentDefinition(
    DynamicEngramOptions BaseOptions,
    IReadOnlyList<ulong> Seeds,
    int ActiveStimulusChannels = 24,
    double RelatedStimulusRetention = 0.75,
    double CueFraction = 0.20,
    double ElapsedDays = 30);

/// <summary>One independently seeded outcome.</summary>
public sealed record EngramExperimentRun(
    ulong Seed,
    double InitialSelectivity,
    double FinalSelectivity,
    double PopulationIdentity,
    double BehavioralStability,
    bool InitialRecallSucceeded,
    bool FinalRecallSucceeded);

/// <summary>Reproducibility metadata attached to every experiment report.</summary>
public sealed record EngramExperimentManifest(
    string LibraryVersion,
    string CommitSha,
    string DefinitionSha256,
    string Runtime,
    string OperatingSystem,
    DateTimeOffset CreatedAt);

/// <summary>Supplies externally controlled report metadata.</summary>
public sealed record EngramExperimentContext(
    DateTimeOffset? CreatedAt = null,
    string? CommitSha = null);

/// <summary>Distribution summary with a deterministic bootstrap confidence interval.</summary>
public sealed record EngramStatisticalSummary(
    int Count,
    double Mean,
    double StandardDeviation,
    double Median,
    double Minimum,
    double Maximum,
    double ConfidenceLower,
    double ConfidenceUpper);

/// <summary>Aggregate statistics and raw outcomes for an experiment.</summary>
public sealed record EngramExperimentReport(
    string LibraryVersion,
    DateTimeOffset CreatedAt,
    EngramExperimentDefinition Definition,
    IReadOnlyList<EngramExperimentRun> Runs,
    double MeanPopulationIdentity,
    double PopulationIdentityStandardDeviation,
    double MeanBehavioralStability,
    double RecallSurvivalRate)
{
    /// <summary>Environment and content hashes needed to reproduce this report.</summary>
    public EngramExperimentManifest? Manifest { get; init; }

    /// <summary>Population-identity distribution across seeds.</summary>
    public EngramStatisticalSummary? PopulationIdentitySummary { get; init; }

    /// <summary>Behavioral-stability distribution across seeds.</summary>
    public EngramStatisticalSummary? BehavioralStabilitySummary { get; init; }
}

/// <summary>Runs seed sweeps and exports auditable machine-readable results.</summary>
public static class EngramExperimentRunner
{
    /// <summary>Runs the standard related-memory drift experiment once per seed.</summary>
    public static EngramExperimentReport Run(
        EngramExperimentDefinition definition,
        EngramExperimentContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(definition.BaseOptions);
        ArgumentNullException.ThrowIfNull(definition.Seeds);
        if (definition.Seeds.Count == 0 || definition.Seeds.Distinct().Count() != definition.Seeds.Count)
            throw new ArgumentException("Seeds must be non-empty and unique.", nameof(definition));
        if (definition.ElapsedDays < 0 || !double.IsFinite(definition.ElapsedDays))
            throw new ArgumentOutOfRangeException(nameof(definition));

        DateTimeOffset epoch = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var runs = new List<EngramExperimentRun>(definition.Seeds.Count);
        foreach (ulong seed in definition.Seeds)
        {
            DynamicEngramOptions options = definition.BaseOptions with { RandomSeed = seed };
            var simulation = new EngramSimulation(epoch, options);
            MemoryStimulus targetStimulus = StimulusFactory.Sparse(
                options.NeuronCount, definition.ActiveStimulusChannels, seed ^ 0xA11CEUL);
            MemoryStimulus related = StimulusFactory.Related(
                targetStimulus, definition.RelatedStimulusRetention, seed ^ 0xBEEFUL);
            EngramSnapshot before = simulation.Encode(new("target", targetStimulus), epoch);
            simulation.Encode(new("competitor", related), epoch);
            RecallResult initial = simulation.Recall(
                simulation.CreatePartialCue(before.MemoryId, definition.CueFraction, seed), epoch);
            DateTimeOffset finalTime = epoch.AddDays(definition.ElapsedDays);
            simulation.AdvanceTo(finalTime);
            EngramSnapshot after = simulation.GetEngram(before.MemoryId);
            RecallResult final = simulation.Recall(
                simulation.CreatePartialCue(before.MemoryId, definition.CueFraction, seed), finalTime);
            EngramComparison comparison = EngramMetrics.Compare(before, after, initial, final);
            runs.Add(new(seed, initial.SelectivityMargin, final.SelectivityMargin,
                comparison.PopulationIdentity, comparison.BehavioralStability, initial.Success, final.Success));
        }

        double meanIdentity = runs.Average(run => run.PopulationIdentity);
        double standardDeviation = Math.Sqrt(runs.Average(run => Math.Pow(run.PopulationIdentity - meanIdentity, 2)));
        DateTimeOffset createdAt = (context?.CreatedAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        string libraryVersion = typeof(EngramExperimentRunner).Assembly.GetName().Version?.ToString() ?? "unknown";
        var report = new EngramExperimentReport(libraryVersion,
            createdAt, definition, runs, meanIdentity, standardDeviation,
            runs.Average(run => run.BehavioralStability), runs.Count(run => run.FinalRecallSucceeded) / (double)runs.Count)
        {
            Manifest = CreateManifest(definition, createdAt, context?.CommitSha),
            PopulationIdentitySummary = EngramStatistics.Summarize(
                runs.Select(run => run.PopulationIdentity).ToArray(), 0x1D3A71UL),
            BehavioralStabilitySummary = EngramStatistics.Summarize(
                runs.Select(run => run.BehavioralStability).ToArray(), 0xB3A4A10FUL)
        };
        return report;
    }

    /// <summary>Serializes the report, including parameters and seeds, as indented JSON.</summary>
    public static string ToJson(EngramExperimentReport report) =>
        JsonSerializer.Serialize(report, EngramReportJson.Options);

    /// <summary>Exports raw runs as invariant-culture CSV.</summary>
    public static string ToCsv(EngramExperimentReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var output = new StringBuilder("seed,initial_selectivity,final_selectivity,population_identity,behavioral_stability,initial_success,final_success\n");
        foreach (EngramExperimentRun run in report.Runs)
            output.AppendLine(string.Join(',', run.Seed,
                run.InitialSelectivity.ToString("R", CultureInfo.InvariantCulture),
                run.FinalSelectivity.ToString("R", CultureInfo.InvariantCulture),
                run.PopulationIdentity.ToString("R", CultureInfo.InvariantCulture),
                run.BehavioralStability.ToString("R", CultureInfo.InvariantCulture),
                run.InitialRecallSucceeded, run.FinalRecallSucceeded));
        return output.ToString();
    }

    internal static EngramExperimentManifest CreateManifest(
        object definition,
        DateTimeOffset createdAt,
        string? suppliedCommit = null)
    {
        byte[] definitionBytes = JsonSerializer.SerializeToUtf8Bytes(definition);
        string hash = Convert.ToHexString(SHA256.HashData(definitionBytes)).ToLowerInvariant();
        string informationalVersion = typeof(EngramExperimentRunner).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        string commit = suppliedCommit
            ?? informationalVersion.Split('+', 2).ElementAtOrDefault(1)
            ?? "unknown";
        return new(
            typeof(EngramExperimentRunner).Assembly.GetName().Version?.ToString() ?? "unknown",
            commit,
            hash,
            Environment.Version.ToString(),
            Environment.OSVersion.ToString(),
            createdAt);
    }
}

/// <summary>Dependency-free descriptive and bootstrap statistics.</summary>
public static class EngramStatistics
{
    /// <summary>Summarizes finite values with a deterministic 95% bootstrap interval for the mean.</summary>
    public static EngramStatisticalSummary Summarize(
        IReadOnlyList<double> values,
        ulong bootstrapSeed = 0x51A71571C5UL,
        int bootstrapSamples = 2_000)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0 || values.Any(value => !double.IsFinite(value)))
            throw new ArgumentException("Statistics require at least one finite value.", nameof(values));
        if (bootstrapSamples < 100)
            throw new ArgumentOutOfRangeException(nameof(bootstrapSamples));

        double[] ordered = values.Order().ToArray();
        double mean = values.Average();
        double standardDeviation = Math.Sqrt(values.Average(value => Math.Pow(value - mean, 2)));
        double median = Quantile(ordered, 0.5);
        var means = new double[bootstrapSamples];
        ulong state = bootstrapSeed;
        for (int sample = 0; sample < means.Length; sample++)
        {
            double sum = 0;
            for (int index = 0; index < values.Count; index++)
            {
                state = Next(state);
                sum += values[(int)(state % (ulong)values.Count)];
            }

            means[sample] = sum / values.Count;
        }

        Array.Sort(means);
        return new(values.Count, mean, standardDeviation, median, ordered[0], ordered[^1],
            Quantile(means, 0.025), Quantile(means, 0.975));
    }

    private static double Quantile(IReadOnlyList<double> ordered, double probability)
    {
        double position = (ordered.Count - 1) * probability;
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        if (lower == upper)
            return ordered[lower];
        double fraction = position - lower;
        return ordered[lower] + ((ordered[upper] - ordered[lower]) * fraction);
    }

    private static ulong Next(ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        state = (state ^ (state >> 30)) * 0xBF58476D1CE4E5B9UL;
        state = (state ^ (state >> 27)) * 0x94D049BB133111EBUL;
        return state ^ (state >> 31);
    }
}
