namespace DynamicEngramSimulator;

/// <summary>
/// Identifies which computational mechanisms participate in a simulation.
/// Profiles make ablations explicit and prevent a disabled mechanism from being
/// hidden in a collection of zero-valued numeric parameters.
/// </summary>
public sealed record EngramMechanismProfile(
    string Id,
    bool ExcitabilityAllocation = true,
    bool RecurrentCompletion = true,
    bool RecallInhibition = true,
    bool InhibitoryPlasticity = true,
    bool Consolidation = true,
    bool PopulationDrift = true)
{
    /// <summary>All mechanisms enabled.</summary>
    public static EngramMechanismProfile Default { get; } = new("default");

    /// <summary>Creates a copy with one named mechanism disabled.</summary>
    public EngramMechanismProfile Without(EngramMechanism mechanism) => mechanism switch
    {
        EngramMechanism.ExcitabilityAllocation => this with
        {
            Id = "no-excitability-allocation",
            ExcitabilityAllocation = false
        },
        EngramMechanism.RecurrentCompletion => this with
        {
            Id = "no-recurrent-completion",
            RecurrentCompletion = false
        },
        EngramMechanism.RecallInhibition => this with
        {
            Id = "no-recall-inhibition",
            RecallInhibition = false
        },
        EngramMechanism.InhibitoryPlasticity => this with
        {
            Id = "no-inhibitory-plasticity",
            InhibitoryPlasticity = false
        },
        EngramMechanism.Consolidation => this with
        {
            Id = "no-consolidation",
            Consolidation = false
        },
        EngramMechanism.PopulationDrift => this with
        {
            Id = "no-population-drift",
            PopulationDrift = false
        },
        _ => throw new ArgumentOutOfRangeException(nameof(mechanism))
    };

    /// <summary>Creates a profile with every requested mechanism disabled.</summary>
    public EngramMechanismProfile Without(IEnumerable<EngramMechanism> mechanisms)
    {
        ArgumentNullException.ThrowIfNull(mechanisms);
        EngramMechanism[] disabled = mechanisms.Distinct().Order().ToArray();
        if (disabled.Length == 0) return this;
        EngramMechanismProfile profile = this;
        foreach (EngramMechanism mechanism in disabled) profile = profile.Without(mechanism);
        return profile with { Id = "without-" + string.Join('-', disabled.Select(Name)) };
    }

    private static string Name(EngramMechanism mechanism) => mechanism switch
    {
        EngramMechanism.ExcitabilityAllocation => "excitability",
        EngramMechanism.RecurrentCompletion => "completion",
        EngramMechanism.RecallInhibition => "inhibition",
        EngramMechanism.InhibitoryPlasticity => "plasticity",
        EngramMechanism.Consolidation => "consolidation",
        EngramMechanism.PopulationDrift => "drift",
        _ => throw new ArgumentOutOfRangeException(nameof(mechanism))
    };

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        if (Id.Length > 100)
            throw new ArgumentOutOfRangeException(nameof(Id));
    }
}

/// <summary>Independently switchable mechanisms supported by the simulator.</summary>
public enum EngramMechanism
{
    /// <summary>Intrinsic excitability contributes to population allocation.</summary>
    ExcitabilityAllocation,

    /// <summary>Engram co-membership produces recurrent pattern completion.</summary>
    RecurrentCompletion,

    /// <summary>Intrinsic, global, and learned inhibition act during recall.</summary>
    RecallInhibition,

    /// <summary>Related memories learn directed inhibitory separation.</summary>
    InhibitoryPlasticity,

    /// <summary>Time-dependent consolidation protects membership strength.</summary>
    Consolidation,

    /// <summary>UTC-day turnover changes engram membership.</summary>
    PopulationDrift
}
