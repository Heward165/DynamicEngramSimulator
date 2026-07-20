# Algorithm notebook

## Systems-consolidation model

The optional systems model uses an explicit directed topology:

```text
dentate gyrus → CA3 → CA1 → cortex
```

Each region independently declares population size, sparsity, plasticity, and consolidation time constant. Encoding ranks
neurons from context drive plus seeded heterogeneity, then creates a bounded number of directed synapses per source member.
Population identity is the mean regional Jaccard similarity to encoding; synaptic identity is a separate Jaccard measure
over edge endpoints.

Passive time reduces hippocampal strength and slowly raises cortical strength. Replay traverses regions forward or reverse,
strengthens cells and edges, and favors cortical transfer. The queue exposes priority, capacity, and interruption. In the
neurogenesis family, dentate-gyrus replacements rewire every incident edge while preserving population size.

Exact recall weights hippocampal support more strongly; the related-context probe weights cortex more strongly. The
resulting precision-to-generalization curve is therefore an explicit consequence of declared regional weights and must not
be interpreted as a fitted biological measurement.

## Scope

The simulator is a sparse recurrent population model designed for controlled computational experiments. It intentionally
keeps every state variable bounded and every random choice reproducible. The implementation is not fitted to biological
units.

## Allocation

For neuron `i` and stimulus `x`, allocation score is:

```text
score_i =
    stimulusWeight * x_i
  + excitabilityWeight * excitability_i
  - 0.25 * inhibition_i
  + seededUniformNoise_i
```

The top `engramSize` neurons become members. Stable identifier ordering resolves exact ties. Initial membership strength
combines encoding importance, stimulus drive, and excitability and is clamped to `(0,1]`.

Encoding raises member excitability by `encodingExcitabilityBoost`. Between events, excitability returns exponentially
toward its neuron-specific baseline:

```text
excitability_i(t + d) =
    baseline_i + (excitability_i(t) - baseline_i) *
    exp(-excitabilityRelaxationRatePerDay * d)
```

That explicit transient is what makes the temporal-linking experiment testable. Both the boost and relaxation rate are
plan parameters; they are dimensionless model controls rather than fitted biological constants.

Related stimuli are constructed by retaining a requested fraction of strongly driven source channels and replacing the
remaining active channels. Similarity therefore enters allocation through controlled input overlap rather than a special
“related memory” flag.

## Recurrent recall

A partial cue clamps a subset of neurons. At each iteration, memory support is the membership-weighted mean activation of
its population. Neuronal recurrent input aggregates the supports of all engrams containing that neuron:

```text
recurrent_i =
    recurrentGain * mean_over_memberships(
        membership(i,m) * support(m))
```

Global inhibition is proportional to mean population activation. Intrinsic inhibition is fixed per neuron. For an
evaluated target, learned directed pair inhibition suppresses competitor-dominant neurons:

```text
net_i = recurrent_i - globalInhibition - intrinsicInhibition_i - pairSeparation_i
candidate_i = logistic(activationGain * (net_i - activationMidpoint))
activation_i' = leak * activation_i + (1 - leak) * candidate_i
```

Cue activation is clamped from below on every iteration. The same equations are used for every memory; the expected
identifier is used only for directed separation and evaluation metrics.

## Inhibitory separation

When a new memory is encoded, active-channel Jaccard similarity determines initial directed separation in both directions:

```text
pairInhibition(a,b) = pairInhibitionRate * stimulusJaccard(a,b)
```

`TrainInhibitorySeparation` can increase one direction independently. This is an explicit model intervention for comparing
selectivity with and without inhibitory plasticity.

## Time, consolidation, and drift

Memory consolidation follows a bounded exponential approach:

```text
consolidation' =
    consolidation + (1 - consolidation) *
    (1 - exp(-consolidationRatePerDay * elapsedDays))
```

Membership decay is slower for consolidated memories:

```text
retention =
    exp(-ln(2) * elapsedDays / membershipHalfLifeDays *
        (1 - 0.8 * consolidation))
```

Expected population turnover is:

```text
engramSize * driftFractionPerDay * elapsedDays
```

The integer portion is applied and the fractional portion is sampled once. Weak members are removed; nonmembers with high
original stimulus drive and current excitability replace them. Engram size is invariant.

## Allocation nulls

Mechanistic allocation uses the score above. The random control removes both stimulus and excitability terms while
preserving inhibition, seeded noise, population size, and stimulus sparsity; it independently shuffles the stored
stimulus so the original correspondence cannot re-enter through drift. The permuted-stimulus control rotates the stimulus vector by a
fixed half-population offset before scoring and stores that rotated vector for subsequent drift and similarity. This
prevents the original correspondence from re-entering later dynamics.

## Offline co-replay

For a requested coupling fraction, co-replay selects the strongest nonshared source members and replaces the weakest
nonshared target members. The operation is symmetric, deterministic, and preserves each engram's cardinality. New
membership strength is the bounded mean of the inherited target strength and source strength. The simulator exposes this
as an intervention rather than applying it silently during time advancement.

## Representation comparison

Checkpoint comparison reports three distinct quantities:

```text
population identity = Jaccard(member identifiers)
representation similarity = cosine(weighted membership vectors)
stimulus alignment = cosine(weighted membership, encoding stimulus)
```

None of these is treated as behavioral recall success.

## Reported recall measures

- **expected activation:** membership-weighted target activation;
- **strongest competitor activation:** maximum corresponding value for another memory;
- **selectivity margin:** target minus strongest competitor;
- **completion ratio:** fraction of target members above activation threshold;
- **distortion ratio:** active, memory-bearing neurons outside the target divided by all active neurons;
- **activation cost:** mean neuronal activation across iterations;
- **success:** target wins and exceeds the configured success threshold.

## Identity and behavioral stability

```text
populationIdentity = intersection(beforeMembers, afterMembers) / union(beforeMembers, afterMembers)
behavioralStability = clamp(1 - abs(beforeTargetActivation - afterTargetActivation), 0, 1)
```

Keeping these metrics separate is a central experimental invariant.

## Numerical and experimental cautions

- Saturating activation can conceal differences between memories; inspect margins and component metrics.
- Drift, consolidation, and decay are coupled engineering rules, so ablations are required.
- Temporal-linking results depend on the configured encoding boost and relaxation timescale; report both with each run.
- Recall is evaluated against a declared target and is not a free-running cognitive decoder.
- Parameter sweeps should preserve seeds and report failures, not only successful configurations.
- Version-three checkpoints serialize options and random continuation. Legacy schemas require their original options.
- Biological interpretation requires independent empirical validation.
