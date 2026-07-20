using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DynamicEngramSimulator;

/// <summary>Definition for overlap and recall as a function of inter-event delay.</summary>
public sealed record TemporalLinkingDefinition(
    DynamicEngramOptions BaseOptions,
    IReadOnlyList<ulong> Seeds,
    IReadOnlyList<double> DelaysHours,
    int ActiveStimulusChannels = 24,
    double RelatedStimulusRetention = 0.25,
    double CueFraction = 0.20);

/// <summary>One seed and delay outcome.</summary>
public sealed record TemporalLinkingRun(
    ulong Seed,
    double DelayHours,
    double PopulationOverlap,
    double FirstRecallSelectivity,
    double SecondRecallSelectivity,
    bool FirstRecallSucceeded,
    bool SecondRecallSucceeded);

/// <summary>Aggregated outcome for one inter-event delay.</summary>
public sealed record TemporalLinkingDelaySummary(
    double DelayHours,
    EngramStatisticalSummary PopulationOverlap,
    EngramStatisticalSummary FirstRecallSelectivity,
    EngramStatisticalSummary SecondRecallSelectivity,
    double JointRecallSuccessRate);

/// <summary>Complete temporal-linking experiment report.</summary>
public sealed record TemporalLinkingReport(
    EngramExperimentManifest Manifest,
    TemporalLinkingDefinition Definition,
    IReadOnlyList<TemporalLinkingRun> Runs,
    IReadOnlyList<TemporalLinkingDelaySummary> Delays);

/// <summary>Measures memory overlap across controlled inter-event intervals.</summary>
public static class TemporalLinkingRunner
{
    /// <summary>Runs every seed at every delay with paired stimulus generation.</summary>
    public static TemporalLinkingReport Run(
        TemporalLinkingDefinition definition,
        EngramExperimentContext? context = null)
    {
        Validate(definition);
        DateTimeOffset createdAt = (context?.CreatedAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        DateTimeOffset epoch = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var runs = new List<TemporalLinkingRun>();
        foreach (double delay in definition.DelaysHours)
        {
            foreach (ulong seed in definition.Seeds)
            {
                DynamicEngramOptions options = definition.BaseOptions with { RandomSeed = seed };
                var simulation = new EngramSimulation(epoch, options);
                MemoryStimulus firstStimulus = StimulusFactory.Sparse(
                    options.NeuronCount, definition.ActiveStimulusChannels, seed ^ 0x71A3UL);
                MemoryStimulus secondStimulus = StimulusFactory.Related(
                    firstStimulus, definition.RelatedStimulusRetention, seed ^ 0x5EC0ADUL);
                EngramSnapshot first = simulation.Encode(new("first", firstStimulus), epoch);
                DateTimeOffset secondTime = epoch.AddHours(delay);
                simulation.AdvanceTo(secondTime);
                EngramSnapshot second = simulation.Encode(new("second", secondStimulus), secondTime);

                RecallResult firstRecall = simulation.Recall(
                    simulation.CreatePartialCue(first.MemoryId, definition.CueFraction, seed ^ 1), secondTime);
                RecallResult secondRecall = simulation.Recall(
                    simulation.CreatePartialCue(second.MemoryId, definition.CueFraction, seed ^ 2), secondTime);
                double overlap = Jaccard(first.NeuronIds, second.NeuronIds);
                runs.Add(new(seed, delay, overlap,
                    firstRecall.SelectivityMargin, secondRecall.SelectivityMargin,
                    firstRecall.Success, secondRecall.Success));
            }
        }

        TemporalLinkingDelaySummary[] summaries = runs
            .GroupBy(run => run.DelayHours)
            .OrderBy(group => group.Key)
            .Select(group => new TemporalLinkingDelaySummary(
                group.Key,
                EngramStatistics.Summarize(group.Select(run => run.PopulationOverlap).ToArray(), Seed(group.Key, 1)),
                EngramStatistics.Summarize(group.Select(run => run.FirstRecallSelectivity).ToArray(), Seed(group.Key, 2)),
                EngramStatistics.Summarize(group.Select(run => run.SecondRecallSelectivity).ToArray(), Seed(group.Key, 3)),
                group.Count(run => run.FirstRecallSucceeded && run.SecondRecallSucceeded) / (double)group.Count()))
            .ToArray();
        return new(
            EngramExperimentRunner.CreateManifest(definition, createdAt, context?.CommitSha),
            definition,
            runs,
            summaries);
    }

    /// <summary>Serializes a temporal-linking report.</summary>
    public static string ToJson(TemporalLinkingReport report) =>
        JsonSerializer.Serialize(report, EngramReportJson.Options);

    /// <summary>Exports every raw seed-delay outcome.</summary>
    public static string ToCsv(TemporalLinkingReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var output = new StringBuilder(
            "seed,delay_hours,population_overlap,first_selectivity,second_selectivity,first_success,second_success\n");
        foreach (TemporalLinkingRun run in report.Runs)
            output.AppendLine(string.Join(',', run.Seed, Format(run.DelayHours), Format(run.PopulationOverlap),
                Format(run.FirstRecallSelectivity), Format(run.SecondRecallSelectivity),
                run.FirstRecallSucceeded, run.SecondRecallSucceeded));
        return output.ToString();
    }

    private static void Validate(TemporalLinkingDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(definition.BaseOptions);
        ArgumentNullException.ThrowIfNull(definition.Seeds);
        ArgumentNullException.ThrowIfNull(definition.DelaysHours);
        definition.BaseOptions.Validate();
        if (definition.Seeds.Count == 0 || definition.Seeds.Distinct().Count() != definition.Seeds.Count)
            throw new ArgumentException("Seeds must be non-empty and unique.", nameof(definition));
        if (definition.DelaysHours.Count == 0 || definition.DelaysHours.Any(delay => !double.IsFinite(delay) || delay < 0))
            throw new ArgumentException("Delays must be finite and non-negative.", nameof(definition));
        if (definition.DelaysHours.Distinct().Count() != definition.DelaysHours.Count)
            throw new ArgumentException("Delays must be unique.", nameof(definition));
        if (definition.ActiveStimulusChannels is < 1 || definition.ActiveStimulusChannels > definition.BaseOptions.NeuronCount)
            throw new ArgumentOutOfRangeException(nameof(definition));
        if (!double.IsFinite(definition.RelatedStimulusRetention) || definition.RelatedStimulusRetention is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(definition));
        if (!double.IsFinite(definition.CueFraction) || definition.CueFraction is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(definition));
    }

    private static double Jaccard(IReadOnlyList<int> first, IReadOnlyList<int> second)
    {
        var left = first.ToHashSet();
        var right = second.ToHashSet();
        return left.Intersect(right).Count() / (double)left.Union(right).Count();
    }

    private static ulong Seed(double delay, ulong discriminator) =>
        unchecked((ulong)BitConverter.DoubleToInt64Bits(delay)) ^ (discriminator * 0x9E3779B97F4A7C15UL);

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
