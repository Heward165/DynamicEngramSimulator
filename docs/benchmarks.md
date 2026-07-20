# Scaling benchmarks

Run the benchmark in Release mode:

```bash
dotnet run --project benchmarks/DynamicEngramSimulator.Benchmarks -c Release
```

It constructs ten overlapping memories and measures repeated partial-cue recall at 128, 512, 2,048, and 10,000 neurons. The JSON
output records mean recall latency and managed allocation per recall along with runtime and operating system. Results are
regression baselines, not biological measurements or cross-hardware performance claims.

Recall uses neuron-to-memory dictionaries as sparse adjacency and rents its iteration buffer from `ArrayPool<double>`.
The 10,000-neuron case uses fewer repetitions so it remains a practical smoke benchmark.

## Local reference run

Recorded on .NET 10.0.7, Windows 10.0.26200, with ten stored memories and 50 measured recalls per size:

| Neurons | Engram size | Mean recall | Allocated per recall |
|---:|---:|---:|---:|
| 128 | 16 | 1.324 ms | 7,458 bytes |
| 512 | 32 | 1.200 ms | 7,457 bytes |
| 2,048 | 128 | 3.518 ms | 7,703 bytes |
| 10,000 | 625 | 8.290 ms | 20,510 bytes |

These figures are a smoke-test baseline from one machine, not a stable performance guarantee. Compare changes on the same
hardware and runtime, and retain the benchmark JSON in performance investigations.
