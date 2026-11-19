# ReviewCenter Benchmarks

Opt-in [BenchmarkDotNet][bdn] benchmarks of design properties of ReviewCenter: how the time of an operation grows
with its input. They measure what the requirement-linked tests deliberately do not assert, because a wall-clock time
depends on the machine that runs it.

They are developer tooling, not requirement evidence: the project is not a test project, `dotnet test` and the CI
build never run it, and it is excluded from ReviewMark. The solution build compiles it so that it cannot rot.

## Benchmarks

| Benchmark | Design property | Design document |
| :--- | :--- | :--- |
| `AuditStoreBenchmarks` | The time of a filtered page read does not grow with the length of the audit trail | AuditStore |
| `SelectionMatcherBenchmarks` | Matching many `**` segments grows with the product of the segment counts | SelectionMatcher |

## Running

Run all benchmarks in Release mode from the repository root:

```pwsh
dotnet run -c Release --project benchmarks/DemaConsulting.ReviewCenter.Benchmarks -- --filter '*'
```

Run one class, or check quickly that the benchmarks still work (one iteration, no statistics):

```pwsh
dotnet run -c Release --project benchmarks/DemaConsulting.ReviewCenter.Benchmarks -- --filter '*AuditStore*'
dotnet run -c Release --project benchmarks/DemaConsulting.ReviewCenter.Benchmarks -- --filter '*' --job Dry
```

BenchmarkDotNet writes its reports to `BenchmarkDotNet.Artifacts/` in the current folder; pass `--artifacts <folder>`
to choose another one. Compare the rows of each benchmark across its parameters (`TrailLength`, `DoubleStars`): the
property holds when the mean time stays flat (audit pages) or grows modestly (matching), not with the absolute numbers,
which differ between machines.

[bdn]: https://benchmarkdotnet.org/
