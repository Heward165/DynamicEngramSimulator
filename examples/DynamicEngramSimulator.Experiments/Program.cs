using DynamicEngramSimulator;

string planDirectory = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.Combine(AppContext.BaseDirectory, "plans");
string outputDirectory = args.Length > 1
    ? Path.GetFullPath(args[1])
    : Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "artifacts", "experiments"));

if (!Directory.Exists(planDirectory))
    throw new DirectoryNotFoundException($"Experiment plan directory was not found: {planDirectory}");

Directory.CreateDirectory(outputDirectory);
string[] plans = Directory.GetFiles(planDirectory, "*.json").Order().ToArray();
if (plans.Length == 0)
    throw new InvalidOperationException("No JSON experiment plans were found.");

foreach (string path in plans)
{
    EngramExperimentPlan plan = EngramExperimentPlanRunner.Parse(File.ReadAllText(path));
    EngramExperimentArtifact artifact = EngramExperimentPlanRunner.Run(plan);
    string safeName = string.Concat(artifact.Name.Select(character =>
        char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-'));
    File.WriteAllText(Path.Combine(outputDirectory, $"{safeName}.json"), artifact.Json);
    File.WriteAllText(Path.Combine(outputDirectory, $"{safeName}.csv"), artifact.Csv);
    File.WriteAllText(Path.Combine(outputDirectory, $"{safeName}.svg"), artifact.Svg);
    Console.WriteLine($"{artifact.Kind}: {artifact.Name}");
}
