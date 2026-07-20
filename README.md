# Dynamic Engram Simulator

[![CI](https://github.com/Heward165/DynamicEngramSimulator/actions/workflows/ci.yml/badge.svg)](https://github.com/Heward165/DynamicEngramSimulator/actions/workflows/ci.yml)
![.NET](https://img.shields.io/badge/.NET-8%20%7C%2010-512BD4)
![C#](https://img.shields.io/badge/C%23-14-239120)
![License](https://img.shields.io/badge/license-MIT-green)

**Can a memory remain behaviorally stable while the neurons representing it change?**

Dynamic Engram Simulator is a dependency-free C# 14 research library for sparse, overlapping neuronal population models.
It provides a reproducible laboratory for engram allocation, partial-cue completion, interference, inhibitory separation,
consolidation, decay, and population turnover.

This is a computational hypothesis platform. Its neurons, time constants, activation values, and plasticity parameters are
dimensionless abstractions—not biological measurements and not a clinical model.

## The experiment this repository makes easy

1. Generate two stimuli with controlled similarity.
2. Allocate a sparse engram for each stimulus.
3. Recall one memory from only part of its population.
4. strengthen separation between overlapping memories;
5. advance simulated time so membership consolidates, decays, and drifts;
6. repeat recall and compare population identity with behavioral stability.

```powershell
dotnet run --project examples/DynamicEngramSimulator.Example
```

A deterministic run prints results like:

```text
Initial recall: success=True, margin=0.2947
Day 30 recall: success=True, margin=0.2232
Population identity: 76.5%
Behavioral stability: 99.3%
Members retained/added/removed: 26/4/4
```

Those values are an experiment outcome for one parameter set and seed, not a neuroscience claim.
See [the runnable experiment](examples/DynamicEngramSimulator.Example/Program.cs).

## Mechanism ledger

| Mechanism | Implementation | Inspectable result |
|---|---|---|
| Sparse allocation | Stimulus drive + intrinsic excitability + inhibition + seeded noise | Exact member identifiers |
| Overlap | Related stimuli retain controlled active channels | Population intersection and Jaccard identity |
| Pattern completion | Iterative recurrent activation from engram co-membership | Completion ratio and activation cost |
| Interference | Overlapping populations co-activate | Strongest competitor activation |
| Inhibitory separation | Directed memory-pair inhibition | Selectivity margin |
| Consolidation | Time-dependent protection from membership decay | Engram consolidation value |
| Dynamic membership | Resource-neutral replacement of members | Added, removed, and retained counts |
| Reproducibility | SplitMix64 state, fixed ordering, full checkpoints | Deterministic continuation |
| Controlled ablations | Explicit mechanism profiles | Paired seed-level effects |
| Factorial interactions | Multi-mechanism disable combinations | Interaction-order effects and FDR-adjusted tests |
| Scientific nulls | Random and permuted-stimulus allocation | Matched seed and turnover null distributions |
| Global sensitivity | Morris and Sobol designs | Elementary, first-order, and total-order influence |
| Offline replay | Resource-neutral symmetric membership replacement | Retrospective overlap before and after replay |
| Experience phases | Separate context, salient, and post-event encoding | Phase-specific populations and overlaps |
| Representation drift | Weighted membership and stimulus geometry | Identity, cosine similarity, and alignment change |
| Experiment manifests | Definition hash + source/runtime metadata | Auditable JSON reports |
| Multi-region consolidation | DG → CA3 → CA1 → cortex with region-specific plasticity | Regional populations and involvement |
| Explicit synaptic graph | Directed sparse connections between adjacent regions | Cell and synapse identity tracked separately |
| Sleep replay engine | Prioritized forward/reverse queues, capacity, and interruption | Replayed and skipped memory identifiers |
| Precision-to-gist protocol | Exact, related, competitor, and regional probes across weeks | Longitudinal precision/generalization curves |
| Model-family comparison | Five matched mechanism families under one seed/protocol | Direct alternative-model reports |

## Controlled mechanism ablations

`EngramMechanismProfile` independently controls excitability allocation, recurrent completion, recall inhibition,
inhibitory plasticity, consolidation, and population drift. `EngramAblationRunner` repeats identical seeds and stimuli while
disabling one mechanism at a time, then reports paired selectivity, identity, behavior, and recall-survival changes.

This makes claims falsifiable inside the model: a mechanism is useful only when its removal produces a repeatable effect,
not because one attractive baseline run was selected.

`EngramFactorialRunner` extends this design to combinations of mechanisms. It reports deterministic paired bootstrap
intervals, sign-flip permutation tests, standardized effects, and Benjamini-Hochberg adjusted p-values. Interaction-order
results make it possible to distinguish a mechanism's isolated effect from a joint failure mode.

## Sensitivity and scientific nulls

`EngramSensitivityRunner` implements two complementary global designs:

- Morris elementary-effects screening for locating influential parameters cheaply;
- Sobol first-order and total-order indices for separating direct influence from interactions.

Every model evaluation uses the same experiment seed set, and reports retain all normalized inputs and raw outputs.
`EngramNullControlRunner` compares mechanistic allocation with random allocation and a deterministic stimulus permutation
while preserving seed sets, engram size, elapsed time, and turnover.

These controls answer a narrower and more useful question than whether one run looks plausible: does the configured
mechanism produce structure beyond an explicitly matched null?

## Temporal memory linking

`TemporalLinkingRunner` varies the interval between two related encoding events and reports population overlap and recall
selectivity for both memories. It provides a direct experiment surface for testing whether transient excitability links
nearby memories while longer intervals separate their populations.

`ReplayTogether` is a separate retrospective-linking intervention. It co-replays already encoded memories, increases
their shared membership by deterministic resource-neutral replacement, and reports overlap before and after replay.
`EngramExperienceEncoder` keeps context, salient-event, and post-event phases as distinct ensembles instead of collapsing
an acquisition experience into one stimulus.

## Multi-region systems consolidation

`SystemsConsolidationSimulator` is an explicit second model surface for questions the original homogeneous population
cannot represent. It allocates separate dentate-gyrus, CA3, CA1, and cortical ensembles, then creates inspectable directed
synapses along the circuit. Cell identity and synaptic identity are reported independently.

The sleep engine accepts prioritized forward or reverse replay requests, limits capacity, and can interrupt a fraction of
the queue. Passive time advancement weakens hippocampal support, slowly strengthens cortex, and optionally replaces and
rewires dentate-gyrus members. These are declared computational mechanisms, not hidden biological claims.

The flagship protocol encodes one precise event and a related distractor, advances six weeks, and records exact-context
recall, related-context generalization, false-positive activation, population identity, synaptic identity, and relative
hippocampal/cortical involvement. It repeats the same protocol for Hebbian, homeostatic, synaptic-tagging,
replay-dependent, and neurogenesis-rewiring families:

```powershell
dotnet run --project examples/DynamicEngramSimulator.Systems -c Release -- `
  artifacts/systems-consolidation
```

The command writes complete JSON and invariant-culture CSV instead of a graphical console simulation.

## Executable JSON experiment plans

The experiment example executes complete plans without recompilation:

```powershell
dotnet run --project examples/DynamicEngramSimulator.Experiments -c Release -- `
  examples/DynamicEngramSimulator.Experiments/plans artifacts/experiments
```

Every plan produces JSON, CSV, and a simple SVG chart. Reports include raw seeds, deterministic bootstrap intervals,
paired effects where applicable, package version, commit SHA, definition SHA-256, runtime, operating system, and timestamp.

## Encode, cue, recall

```csharp
var options = new DynamicEngramOptions
{
    NeuronCount = 300,
    DefaultEngramSize = 30,
    RandomSeed = 20260716,
};
var simulation = new EngramSimulation(DateTimeOffset.UtcNow, options);

MemoryStimulus stimulus =
    StimulusFactory.Sparse(options.NeuronCount, activeChannels: 45, seed: 10);

EngramSnapshot memory = simulation.Encode(
    new EngramEncodingRequest("morning train", stimulus),
    simulation.CurrentTime);

RecallCue cue = simulation.CreatePartialCue(
    memory.MemoryId,
    memberFraction: 0.20,
    seed: 12);

RecallResult result = simulation.Recall(cue, simulation.CurrentTime);
```

`RecallResult` reports the winning memory, target and competitor activation, selectivity margin, completion, distortion,
normalized activation cost, iteration count, and success under the configured threshold.

## Identity is not behavior

Population identity is the Jaccard similarity of two member sets:

```text
identity = |E(t1) intersect E(t2)| / |E(t1) union E(t2)|
```

`EngramMetrics.Compare` keeps that separate from behavioral stability, defined here as the bounded similarity of expected
engram activation across two recall trials. A low identity and high behavioral stability can coexist; neither metric is
silently substituted for the other.

`EngramMetrics.CompareRepresentations` additionally reports weighted-membership cosine similarity and stimulus alignment.
This prevents cell turnover, representation geometry, and behavioral preservation from being treated as synonyms.

## What is deterministic?

Given the same options, timestamps, stimuli, calls, and seed, the simulator reproduces:

- initial neuronal heterogeneity;
- memory identifiers;
- allocation choices;
- drift replacements;
- partial-cue selection;
- post-checkpoint random continuation.

`ExportState`, `ToJson`, `FromState`, and `FromJson` preserve neurons, memories, member strengths, directed inhibition,
random state, simulation time, and revision. Restore rejects malformed dimensions, duplicate members, invalid references,
future encodings, and out-of-range values.

Version-three checkpoints preserve the complete numerical options and mechanism profile, so current checkpoints restore
without reconstructing configuration. `EngramStateMigrator` upgrades version-one and version-two data; callers must supply
the original options for those historical schemas because the omitted values cannot be inferred safely.

## Model boundaries

- The simulator does not reproduce a named brain region or cell type.
- Engram membership is a model assignment, not an experimentally tagged cell.
- Recall receives the expected memory identifier because this is an evaluation laboratory.
- Pair inhibition is an explicit computational separation rule, not a claim about one biological circuit.
- Population turnover preserves engram size; biological neurogenesis and cell death are outside this version.
- Parameters must be swept rather than tuned until one attractive example appears.
- P-values describe repeated simulator seeds, not biological subjects or experimental replication.
- Replay and allocation controls are explicit computational interventions, not inferred neural processes.
- Results can generate hypotheses but cannot establish human or animal memory mechanisms.

## Repository notebook

- `src/DynamicEngramSimulator` — simulation engine, state, metrics, and stimulus generators
- `examples/DynamicEngramSimulator.Example` — stable behavior under population drift
- `examples/DynamicEngramSimulator.Experiments` — JSON-driven experiment gallery
- `examples/DynamicEngramSimulator.Systems` — multi-region precision-to-generalization comparison
- `tests/DynamicEngramSimulator.Conformance` — allocation, recall, overlap, inhibition, drift, and checkpoint boundaries
- [docs/algorithm.md](docs/algorithm.md) — equations, invariants, and experiment cautions
- [docs/experiments.md](docs/experiments.md) — ablations, temporal linking, statistics, and artifacts
- [docs/architecture.md](docs/architecture.md) — deterministic layers and checkpoint design
- [docs/benchmarks.md](docs/benchmarks.md) — scaling methodology

## Reproduce every check

```powershell
dotnet restore DynamicEngramSimulator.slnx
dotnet format DynamicEngramSimulator.slnx --no-restore --verify-no-changes
dotnet build DynamicEngramSimulator.slnx -c Release --no-restore
dotnet run --project tests/DynamicEngramSimulator.Conformance -c Release --no-build
dotnet test tests/DynamicEngramSimulator.Tests -c Release --no-build
dotnet run --project examples/DynamicEngramSimulator.Example -c Release --no-build
dotnet run --project examples/DynamicEngramSimulator.Experiments -c Release --no-build
dotnet run --project examples/DynamicEngramSimulator.Systems -c Release --no-build
dotnet run --project benchmarks/DynamicEngramSimulator.Benchmarks -c Release --no-build
dotnet pack src/DynamicEngramSimulator/DynamicEngramSimulator.csproj -c Release --no-build -o artifacts/packages
```

The runtime library has no third-party runtime dependencies. Version `0.5.0-preview.1` is intentionally prerelease while the public experiment and checkpoint APIs are validated. Packages target .NET 8 and .NET 10 and include symbols, Source Link, and package validation.

## Multi-seed research runs

`EngramExperimentRunner` runs the stability experiment across unique seeds and reports the mean and standard deviation of population identity, mean behavioral stability, recall-survival rate, and every raw outcome. `ToJson` preserves the complete definition and manifest; `ToCsv` produces invariant-culture rows for analysis. Time evolution now uses closed-form consolidation/decay plus deterministic UTC-day drift, so one 30-day advance and thirty one-day advances produce the same physical state.

Distribution reports also include median, range, and a deterministic 95% bootstrap interval. See the committed
[experiment gallery](docs/gallery/README.md) for plans and reproducible artifacts.

## Releases

Tagged versions create a GitHub prerelease containing the NuGet package, symbols, and experiment artifacts. The workflow
also publishes to NuGet when `NUGET_API_KEY` is configured. Public contracts follow the [roadmap](docs/roadmap.md), and the
project is licensed under the [MIT License](LICENSE).

## Research basis

The repository translates findings and questions into deliberately simplified mechanisms:

- Han et al., [Neuronal competition and selection during memory formation](https://pubmed.ncbi.nlm.nih.gov/17446403/) — experimental evidence that neuronal competition and CREB-related excitability affect allocation.
- Liu et al., [Optogenetic stimulation of a hippocampal engram activates fear memory recall](https://doi.org/10.1038/nature11028) — causal manipulation of tagged encoding-active populations and recall.
- Neunuebel and Knierim, [CA3 retrieves coherent representations from degraded input](https://pubmed.ncbi.nlm.nih.gov/24462102/) — evidence relevant to partial-cue completion and separation.
- Lavi et al., [Dynamic and selective engrams emerge with memory consolidation](https://doi.org/10.1038/s41593-023-01551-w) — dynamic composition, selectivity, and inhibitory plasticity.
- Rashid et al., [Competition between engrams influences fear memory formation and recall](https://pubmed.ncbi.nlm.nih.gov/27463673/) — excitability-dependent coallocation of temporally close memories.
- Pouget et al., [Deconstruction of a memory engram reveals distinct ensembles recruited at learning](https://doi.org/10.1038/s41593-026-02230-2) — acquisition-phase-specific ensemble recruitment.
- Hennequin et al., [Co-dependent excitatory and inhibitory plasticity accounts for quick, stable and long-lasting memories](https://doi.org/10.1038/s41593-024-01597-4) — computational and network evidence motivating coupled excitation/inhibition.
- Zheng et al., [Perpetual step-like restructuring of hippocampal circuit dynamics](https://pmc.ncbi.nlm.nih.gov/articles/PMC11485410/) — population drift arising from step-like changes in individual representations.
- Guskjolen et al., [Systems consolidation reorganizes hippocampal engram circuitry](https://doi.org/10.1038/s41586-025-08993-1) — time-dependent rewiring and the precision-to-generalization question.
- Kim et al., [Parallel processing of past and future memories during sleep](https://doi.org/10.1038/s41467-025-58860-w) — motivation for competing replay queues and preservation of memory identity.
- Káli and Dayan, [Multi-region hippocampus-thalamus-cortex consolidation model](https://doi.org/10.1038/s41467-022-28339-z) — motivation for region-specific plasticity timescales.
