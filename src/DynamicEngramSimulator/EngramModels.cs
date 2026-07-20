namespace DynamicEngramSimulator;

/// <summary>Immutable external drive used to allocate a sparse engram population.</summary>
public sealed class MemoryStimulus
{
    private readonly double[] drive;

    /// <summary>Creates a bounded neural-drive vector.</summary>
    public MemoryStimulus(IEnumerable<double> drive)
    {
        ArgumentNullException.ThrowIfNull(drive);
        this.drive = drive.ToArray();
        if (this.drive.Length == 0 || this.drive.Any(value => !double.IsFinite(value) || value is < 0 or > 1))
            throw new ArgumentException("Stimulus values must be finite and in [0,1].", nameof(drive));
    }

    /// <summary>Number of simulated input channels.</summary>
    public int Count => drive.Length;

    /// <summary>Gets one channel's drive.</summary>
    public double this[int index] => drive[index];

    /// <summary>Returns a defensive copy.</summary>
    public double[] ToArray() => (double[])drive.Clone();
}

/// <summary>Parameters for encoding one memory.</summary>
/// <param name="Label">Human-readable memory label.</param>
/// <param name="Stimulus">External drive across the simulated population.</param>
/// <param name="EngramSize">Optional sparse population size.</param>
/// <param name="Importance">Encoding-strength multiplier in (0,1].</param>
/// <param name="MemoryId">Optional stable identifier.</param>
public sealed record EngramEncodingRequest(
    string Label,
    MemoryStimulus Stimulus,
    int? EngramSize = null,
    double Importance = 1,
    Guid? MemoryId = null);

/// <summary>A partial neural cue supplied to the recurrent recall process.</summary>
public sealed class RecallCue
{
    private readonly IReadOnlyDictionary<int, double> neuronDrive;

    /// <summary>Creates a cue for an expected memory.</summary>
    public RecallCue(Guid expectedMemoryId, IReadOnlyDictionary<int, double> neuronDrive)
    {
        if (expectedMemoryId == Guid.Empty) throw new ArgumentOutOfRangeException(nameof(expectedMemoryId));
        ArgumentNullException.ThrowIfNull(neuronDrive);
        if (neuronDrive.Count == 0) throw new ArgumentException("A cue requires at least one neuron.", nameof(neuronDrive));
        if (neuronDrive.Any(pair => pair.Key < 0 || !double.IsFinite(pair.Value) || pair.Value is <= 0 or > 1))
            throw new ArgumentException("Cue drives must be in (0,1] and use non-negative neuron identifiers.", nameof(neuronDrive));
        ExpectedMemoryId = expectedMemoryId;
        this.neuronDrive = new Dictionary<int, double>(neuronDrive);
    }

    /// <summary>Memory whose recall should be evaluated.</summary>
    public Guid ExpectedMemoryId { get; }

    /// <summary>Defensive cue-drive view.</summary>
    public IReadOnlyDictionary<int, double> NeuronDrive => neuronDrive;
}

/// <summary>A captured engram population at one simulation time.</summary>
public sealed record EngramSnapshot(
    Guid MemoryId,
    string Label,
    DateTimeOffset CapturedAt,
    IReadOnlyList<int> NeuronIds,
    double MeanMembership,
    double Consolidation);

/// <summary>Observable state of one simulated neuron.</summary>
public sealed record EngramNeuronSnapshot(
    int Id,
    double Activation,
    double Excitability,
    double Inhibition,
    double Consolidation,
    IReadOnlyDictionary<Guid, double> Memberships);

/// <summary>Pattern-completion outcome and selectivity diagnostics.</summary>
public sealed record RecallResult(
    Guid ExpectedMemoryId,
    Guid? RecalledMemoryId,
    bool Success,
    double ExpectedActivation,
    double StrongestCompetitorActivation,
    double SelectivityMargin,
    double CompletionRatio,
    double DistortionRatio,
    double ActivationCost,
    int Iterations,
    DateTimeOffset RecalledAt);

/// <summary>Population identity and behavioral stability between two observations.</summary>
public sealed record EngramComparison(
    double PopulationIdentity,
    double BehavioralStability,
    int RetainedNeurons,
    int AddedNeurons,
    int RemovedNeurons);

/// <summary>Outcome of an explicit offline co-replay intervention.</summary>
public sealed record EngramReplayResult(
    Guid FirstMemoryId,
    Guid SecondMemoryId,
    double PopulationOverlapBefore,
    double PopulationOverlapAfter,
    int RequestedReplacementsPerMemory,
    DateTimeOffset ReplayedAt);

/// <summary>Identity and information-preservation metrics between two checkpoints.</summary>
public sealed record EngramRepresentationComparison(
    double PopulationIdentity,
    double MembershipCosineSimilarity,
    double StimulusAlignmentBefore,
    double StimulusAlignmentAfter,
    double StimulusAlignmentChange);

/// <summary>Serializable state for one neuron.</summary>
public sealed record NeuronState(
    int Id,
    double Activation,
    double Excitability,
    double Inhibition,
    double Consolidation);

/// <summary>Serializable strength of one neuron in one engram.</summary>
public sealed record EngramMemberState(int NeuronId, double Strength);

/// <summary>Serializable state for one memory population.</summary>
public sealed record MemoryEngramState(
    Guid MemoryId,
    string Label,
    DateTimeOffset EncodedAt,
    double[] Stimulus,
    EngramMemberState[] Members,
    double Consolidation);

/// <summary>Serializable directed inhibitory separation between two memories.</summary>
public sealed record PairInhibitionState(Guid CueMemoryId, Guid CompetitorMemoryId, double Strength);

/// <summary>Complete deterministic simulation checkpoint.</summary>
public sealed record DynamicEngramState(
    int SchemaVersion,
    DateTimeOffset CurrentTime,
    ulong RandomState,
    long Revision,
    NeuronState[] Neurons,
    MemoryEngramState[] Memories,
    PairInhibitionState[] PairInhibitions)
{
    /// <summary>Mechanism profile active when this checkpoint was produced.</summary>
    public string MechanismProfileId { get; init; } = EngramMechanismProfile.Default.Id;

    /// <summary>Complete mechanism switches required for deterministic continuation.</summary>
    public EngramMechanismProfile Mechanisms { get; init; } = EngramMechanismProfile.Default;

    /// <summary>
    /// Complete numerical and mechanism configuration. This is absent only on
    /// historical version-one and version-two checkpoints.
    /// </summary>
    public DynamicEngramOptions? Options { get; init; }
}
