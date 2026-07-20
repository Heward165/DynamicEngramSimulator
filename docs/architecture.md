# Architecture

The simulator separates five layers:

1. immutable requests, snapshots, checkpoints, and options;
2. the deterministic population simulation engine;
3. explicit mechanism profiles used for controlled ablations;
4. experiment runners for stability, ablation, and temporal linking;
5. JSON, CSV, and SVG artifact generation.

Every random choice is derived from a recorded seed or checkpointed generator state. UTC-day drift is partition invariant,
and experiment manifests hash the complete definition. A version-three checkpoint stores the complete options and mechanism
profile. The public migrator upgrades version-one and version-two checkpoints; restoring those historical schemas requires
their original options because those versions did not serialize them.

Mechanism profiles are configuration strategies rather than biological labels. They independently control excitability in
allocation, recurrent completion, recall inhibition, inhibitory plasticity, consolidation, and population drift. This keeps
an ablation auditable without pretending that a zero-valued parameter is a separate biological condition.
