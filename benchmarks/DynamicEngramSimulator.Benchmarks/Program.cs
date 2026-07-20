using System.Diagnostics;
using System.Text.Json;
using DynamicEngramSimulator;

var results = new List<object>();
foreach (int neuronCount in new[] { 128, 512, 2_048, 10_000 })
{
    int repetitions = neuronCount >= 10_000 ? 10 : 50;
    int engramSize = Math.Max(16, neuronCount / 16);
    var options = new DynamicEngramOptions
    {
        NeuronCount = neuronCount,
        DefaultEngramSize = engramSize,
        RecallIterations = 8,
        RandomSeed = 20260717
    };
    DateTimeOffset epoch = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    var simulation = new EngramSimulation(epoch, options);
    var memories = new List<EngramSnapshot>();
    for (int index = 0; index < 10; index++)
    {
        MemoryStimulus stimulus = StimulusFactory.Sparse(neuronCount, engramSize * 2, (ulong)(100 + index));
        memories.Add(simulation.Encode(new($"memory-{index}", stimulus), epoch));
    }

    RecallCue cue = simulation.CreatePartialCue(memories[0].MemoryId, 0.20, 99);
    GC.Collect();
    long before = GC.GetAllocatedBytesForCurrentThread();
    var stopwatch = Stopwatch.StartNew();
    for (int index = 0; index < repetitions; index++)
        simulation.Recall(cue, epoch);
    stopwatch.Stop();
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    results.Add(new
    {
        neuronCount,
        memoryCount = memories.Count,
        engramSize,
        repetitions,
        meanRecallMilliseconds = stopwatch.Elapsed.TotalMilliseconds / repetitions,
        allocatedBytesPerRecall = allocated / (double)repetitions
    });
}

Console.WriteLine(JsonSerializer.Serialize(new
{
    runtime = Environment.Version.ToString(),
    operatingSystem = Environment.OSVersion.ToString(),
    results
}, new JsonSerializerOptions { WriteIndented = true }));
