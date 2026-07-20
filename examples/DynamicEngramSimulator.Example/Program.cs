using DynamicEngramSimulator;

var started = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
var options = new DynamicEngramOptions
{
    NeuronCount = 300,
    DefaultEngramSize = 30,
    DriftFractionPerDay = 0.05,
    RecallIterations = 6,
    RecurrentGain = 1.5,
    PairInhibitionRate = 1,
    RandomSeed = 20260716,
};
var simulation = new EngramSimulation(started, options);

MemoryStimulus station = StimulusFactory.Sparse(options.NeuronCount, activeChannels: 120, seed: 10);
MemoryStimulus similarStation = StimulusFactory.Related(station, retainedFraction: 0.70, seed: 11);

EngramSnapshot original = simulation.Encode(new("morning train", station), started);
EngramSnapshot competitor = simulation.Encode(new("evening train", similarStation), started);
simulation.TrainInhibitorySeparation(original.MemoryId, competitor.MemoryId, amount: 1);

RecallCue firstCue = simulation.CreatePartialCue(original.MemoryId, memberFraction: 0.20, seed: 12);
RecallResult firstRecall = simulation.Recall(firstCue, started);

simulation.AdvanceTo(started.AddDays(30));
EngramSnapshot drifted = simulation.GetEngram(original.MemoryId);
RecallCue laterCue = simulation.CreatePartialCue(original.MemoryId, memberFraction: 0.20, seed: 12);
RecallResult laterRecall = simulation.Recall(laterCue, started.AddDays(30));
EngramComparison comparison = EngramMetrics.Compare(original, drifted, firstRecall, laterRecall);

Console.WriteLine($"Initial recall: success={firstRecall.Success}, margin={firstRecall.SelectivityMargin:F4}");
Console.WriteLine($"Day 30 recall: success={laterRecall.Success}, margin={laterRecall.SelectivityMargin:F4}");
Console.WriteLine($"Population identity: {comparison.PopulationIdentity:P1}");
Console.WriteLine($"Behavioral stability: {comparison.BehavioralStability:P1}");
Console.WriteLine($"Members retained/added/removed: {comparison.RetainedNeurons}/{comparison.AddedNeurons}/{comparison.RemovedNeurons}");
