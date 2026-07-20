using System.Globalization;
using System.Text;
using System.Text.Json;
using DynamicEngramSimulator;

DateTimeOffset epoch = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
var definition = new SystemsConsolidationExperimentDefinition();
IReadOnlyList<SystemsConsolidationReport> reports =
    SystemsConsolidationExperiment.CompareModelFamilies(definition, epoch);
string outputDirectory = args.Length == 0
    ? Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "artifacts", "systems-consolidation"))
    : Path.GetFullPath(args[0]);
Directory.CreateDirectory(outputDirectory);

var csv = new StringBuilder("model,sleep,day,exact,related,false_positive,identity,synaptic_identity,hippocampus,cortex").AppendLine();
foreach (SystemsConsolidationReport report in reports)
{
    foreach (SystemsConsolidationObservation observation in report.Observations)
    {
        SystemsRecallProbe probe = observation.Probe;
        csv.Append(report.ModelFamily).Append(',')
            .Append(report.SleepReplayEnabled).Append(',')
            .Append(observation.Day.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(probe.ExactContextRecall.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(probe.RelatedContextGeneralization.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(probe.FalsePositiveRecall.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(probe.MemoryIdentity.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(probe.SynapticIdentity.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(probe.HippocampalInvolvement.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(probe.CorticalInvolvement.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
    }
}

File.WriteAllText(Path.Combine(outputDirectory, "systems-consolidation.json"),
    JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
File.WriteAllText(Path.Combine(outputDirectory, "systems-consolidation.csv"), csv.ToString());
foreach (SystemsConsolidationReport report in reports)
{
    SystemsRecallProbe final = report.Observations[^1].Probe;
    Console.WriteLine($"{report.ModelFamily,-24} exact={final.ExactContextRecall:F3} " +
                      $"generalization={final.RelatedContextGeneralization:F3} " +
                      $"hippocampus={final.HippocampalInvolvement:F3} cortex={final.CorticalInvolvement:F3}");
}
Console.WriteLine($"Artifacts: {outputDirectory}");
