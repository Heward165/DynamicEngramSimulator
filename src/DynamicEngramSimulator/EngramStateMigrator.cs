namespace DynamicEngramSimulator;

/// <summary>Upgrades historical checkpoint contracts to the current schema.</summary>
public static class EngramStateMigrator
{
    /// <summary>Current checkpoint schema emitted by the simulator.</summary>
    public const int CurrentSchemaVersion = 3;

    /// <summary>
    /// Upgrades historical checkpoints. Version one receives the original
    /// default mechanism profile. Versions one and two cannot recover their
    /// omitted numerical options, so callers must supply those options when restoring.
    /// </summary>
    public static DynamicEngramState Upgrade(DynamicEngramState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.SchemaVersion switch
        {
            1 => state with
            {
                SchemaVersion = CurrentSchemaVersion,
                MechanismProfileId = EngramMechanismProfile.Default.Id,
                Mechanisms = EngramMechanismProfile.Default,
                Options = null
            },
            2 => state with { SchemaVersion = CurrentSchemaVersion, Options = null },
            CurrentSchemaVersion => state,
            _ => throw new ArgumentException(
                $"Checkpoint schema {state.SchemaVersion} is not supported.",
                nameof(state))
        };
    }
}
