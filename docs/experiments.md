# Experiment guide

## Stability

The stability experiment measures whether recall behavior survives population turnover. It reports raw seed outcomes,
population identity, behavioral stability, recall survival, distribution summaries, and deterministic 95% bootstrap
intervals.

## Mechanism ablations

`EngramAblationRunner` repeats identical seeds and stimuli while disabling one mechanism at a time. Paired reports include
changes in final selectivity, population identity, behavioral stability, recall survival, and the paired selectivity effect
size. Supported ablations are excitability allocation, recurrent completion, recall inhibition, inhibitory plasticity,
consolidation, and population drift.

## Factorial interactions

`EngramFactorialRunner` enumerates every mechanism combination through a requested order. A two-factor design includes all
single ablations and all pairs. Every condition reuses the baseline seed set, reports the raw experiment, estimates a
paired selectivity interval, performs a sign-flip test, and controls false discovery rate across the family.

Factorial output is evidence about interactions inside this model. It is not a substitute for an independently replicated
biological factorial experiment.

## Allocation null controls

`EngramNullControlRunner` preserves seeds, population size, stimulus sparsity, elapsed time, cue fraction, and turnover.
It changes only the allocation relationship:

- `Random` removes stimulus and excitability contributions;
- `PermutedStimulus` rotates stimulus channels before allocation.

Reports contain paired effects for final selectivity and behavioral stability.

## Global sensitivity

`EngramSensitivityRunner` accepts explicit uniform ranges and targets final selectivity, population identity, behavioral
stability, or recall survival. Morris reports mean absolute elementary effects and their dispersion. Sobol reports
first-order and total-order indices. All normalized sampled inputs and outputs are retained in JSON.

Use Morris to reduce a large parameter set before running the more expensive Sobol design. Do not interpret negative
finite-sample Sobol estimates as negative physical variance; increase the sample count and inspect estimator uncertainty.

## Inference and power

`EngramInference` provides deterministic paired bootstrap intervals, exact or Monte Carlo sign-flip tests,
Benjamini-Hochberg correction, and approximate paired sample-size planning. Seeds are the unit of replication.

## Offline replay and experience phases

`ReplayTogether` is an intervention that replaces weak nonshared members with strong members of the co-replayed memory
while preserving each engram's size. `EngramExperienceEncoder` encodes ordered phases as separate memories and reports
their pairwise population overlaps.

## Temporal linking

`TemporalLinkingRunner` encodes two controlled stimuli at several inter-event delays. It reports population overlap and the
recall selectivity of both memories for every seed-delay pair. This is a computational test surface inspired by evidence
that excitability-dependent allocation can link temporally close memories and separate distant memories.

## JSON plans

The `DynamicEngramSimulator.Experiments` example executes every JSON file in its `plans` directory:

```bash
dotnet run --project examples/DynamicEngramSimulator.Experiments -c Release -- \
  examples/DynamicEngramSimulator.Experiments/plans artifacts/experiments
```

Each plan records its name, experiment kind, complete options, seeds, fixed report timestamp, and experiment-specific
settings. Each execution produces JSON, raw or summary CSV, and a dependency-free SVG chart.

Reports include library version, commit SHA when available, definition SHA-256, runtime, operating system, and creation
instant. Fixing the plan timestamp and source revision makes artifacts independently auditable.
