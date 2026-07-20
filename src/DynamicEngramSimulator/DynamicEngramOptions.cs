namespace DynamicEngramSimulator;

/// <summary>Controls allocation, recall dynamics, inhibition, consolidation, and population drift.</summary>
public sealed record DynamicEngramOptions
{
    /// <summary>Explicit mechanism switches used by baseline and ablation runs.</summary>
    public EngramMechanismProfile Mechanisms { get; init; } = EngramMechanismProfile.Default;

    /// <summary>
    /// Optional scientific null applied to allocation. Production experiments should use
    /// <see cref="EngramAllocationControl.Mechanistic"/>.
    /// </summary>
    public EngramAllocationControl AllocationControl { get; init; } = EngramAllocationControl.Mechanistic;

    /// <summary>Number of simulated neurons.</summary>
    public int NeuronCount { get; init; } = 256;

    /// <summary>Default number of neurons allocated to one engram.</summary>
    public int DefaultEngramSize { get; init; } = 24;

    /// <summary>Contribution of the external stimulus during engram allocation.</summary>
    public double StimulusWeight { get; init; } = 1.0;

    /// <summary>Contribution of intrinsic neuronal excitability during allocation.</summary>
    public double ExcitabilityWeight { get; init; } = 0.35;

    /// <summary>Encoding-dependent excitability increase applied to selected neurons.</summary>
    public double EncodingExcitabilityBoost { get; init; } = 0.08;

    /// <summary>Daily exponential relaxation rate toward baseline excitability.</summary>
    public double ExcitabilityRelaxationRatePerDay { get; init; } = 0.02;

    /// <summary>Uniform allocation noise amplitude.</summary>
    public double AllocationNoise { get; init; } = 0.03;

    /// <summary>Initial member strength learned during encoding.</summary>
    public double HebbianStrength { get; init; } = 0.85;

    /// <summary>Number of recurrent pattern-completion iterations.</summary>
    public int RecallIterations { get; init; } = 12;

    /// <summary>Strength of recurrent excitation from engram co-membership.</summary>
    public double RecurrentGain { get; init; } = 2.5;

    /// <summary>Slope of the bounded activation function.</summary>
    public double ActivationGain { get; init; } = 6.0;

    /// <summary>Net-input midpoint of the activation function.</summary>
    public double ActivationMidpoint { get; init; } = 0.35;

    /// <summary>Fraction of previous activation retained at each recall iteration.</summary>
    public double ActivationLeak { get; init; } = 0.25;

    /// <summary>Global activity-dependent inhibition.</summary>
    public double GlobalInhibition { get; init; } = 0.25;

    /// <summary>Automatic pairwise inhibitory separation learned from population overlap.</summary>
    public double PairInhibitionRate { get; init; } = 0.45;

    /// <summary>Expected fraction of each engram replaced per simulated day.</summary>
    public double DriftFractionPerDay { get; init; } = 0.005;

    /// <summary>Half-life of unconsolidated membership strength.</summary>
    public double MembershipHalfLifeDays { get; init; } = 180;

    /// <summary>Daily consolidation rate protecting membership strength.</summary>
    public double ConsolidationRatePerDay { get; init; } = 0.04;

    /// <summary>Member activation threshold used by completion and distortion metrics.</summary>
    public double ActivationThreshold { get; init; } = 0.5;

    /// <summary>Minimum expected-engram activation required for successful recall.</summary>
    public double SuccessThreshold { get; init; } = 0.55;

    /// <summary>Deterministic simulation seed.</summary>
    public ulong RandomSeed { get; init; } = 0xD1A6E6A4UL;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Mechanisms);
        Mechanisms.Validate();
        if (!Enum.IsDefined(AllocationControl)) throw new ArgumentOutOfRangeException(nameof(AllocationControl));
        if (NeuronCount < 8) throw new ArgumentOutOfRangeException(nameof(NeuronCount));
        if (DefaultEngramSize is < 2 || DefaultEngramSize > NeuronCount)
            throw new ArgumentOutOfRangeException(nameof(DefaultEngramSize));
        Positive(StimulusWeight, nameof(StimulusWeight));
        NonNegative(ExcitabilityWeight, nameof(ExcitabilityWeight));
        Probability(EncodingExcitabilityBoost, nameof(EncodingExcitabilityBoost));
        NonNegative(ExcitabilityRelaxationRatePerDay, nameof(ExcitabilityRelaxationRatePerDay));
        NonNegative(AllocationNoise, nameof(AllocationNoise));
        Probability(HebbianStrength, nameof(HebbianStrength));
        if (RecallIterations is < 1 or > 10_000) throw new ArgumentOutOfRangeException(nameof(RecallIterations));
        Positive(RecurrentGain, nameof(RecurrentGain));
        Positive(ActivationGain, nameof(ActivationGain));
        Probability(ActivationMidpoint, nameof(ActivationMidpoint));
        Probability(ActivationLeak, nameof(ActivationLeak));
        NonNegative(GlobalInhibition, nameof(GlobalInhibition));
        NonNegative(PairInhibitionRate, nameof(PairInhibitionRate));
        Probability(DriftFractionPerDay, nameof(DriftFractionPerDay));
        Positive(MembershipHalfLifeDays, nameof(MembershipHalfLifeDays));
        NonNegative(ConsolidationRatePerDay, nameof(ConsolidationRatePerDay));
        Probability(ActivationThreshold, nameof(ActivationThreshold));
        Probability(SuccessThreshold, nameof(SuccessThreshold));
    }

    private static void Probability(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1) throw new ArgumentOutOfRangeException(name);
    }

    private static void Positive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(name);
    }

    private static void NonNegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(name);
    }
}

/// <summary>Matched allocation controls used to test whether modeled structure exceeds a null.</summary>
public enum EngramAllocationControl
{
    /// <summary>Stimulus, excitability, inhibition, and noise all participate normally.</summary>
    Mechanistic,

    /// <summary>Allocation ignores stimulus and excitability while preserving population size and noise.</summary>
    Random,

    /// <summary>Stimulus channels are deterministically rotated, breaking their neuron correspondence.</summary>
    PermutedStimulus
}
