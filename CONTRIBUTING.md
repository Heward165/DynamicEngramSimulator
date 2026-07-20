# Contributing

Open an issue describing the hypothesis, invariant, or empirical protocol before adding a mechanism. Changes should include a deterministic example, boundary tests, documented units, and evidence that results do not depend on an attractive single seed.

Before submitting a pull request, run:

```powershell
dotnet format DynamicEngramSimulator.slnx --verify-no-changes
dotnet build DynamicEngramSimulator.slnx -c Release
dotnet test tests/DynamicEngramSimulator.Tests -c Release --no-build
dotnet run --project tests/DynamicEngramSimulator.Conformance -c Release --no-build
dotnet run --project examples/DynamicEngramSimulator.Experiments -c Release --no-build -- examples/DynamicEngramSimulator.Experiments/plans artifacts/experiments
dotnet run --project benchmarks/DynamicEngramSimulator.Benchmarks -c Release --no-build
dotnet pack src/DynamicEngramSimulator -c Release --no-build -o artifacts/packages
```
