## xUnit

This document describes the integration and usage design for the `xUnit` OTS software item.

### Purpose

xUnit is chosen as the unit-testing framework for the project. It provides test
discovery and execution on the Microsoft Testing Platform. The TRX result files that serve as the
traceability evidence consumed by ReqStream are written by the Microsoft Testing Platform TRX report
extension (`Microsoft.Testing.Extensions.TrxReport`), not by xUnit itself.

### Features Used

- `[Fact]` and `[Theory]` test discovery and execution
- Execution as a Microsoft Testing Platform test application through `dotnet test`
- Test results handed to the TRX report extension, which writes the TRX result file when it is
  requested with `--report-trx`

### Integration Pattern

xUnit is consumed as a NuGet package reference in the test projects (the ReviewCenter tests and the
Puppet tests). Each test project is an executable test application, so `dotnet test` runs it on the
Microsoft Testing Platform, which discovers and executes the `[Fact]` and `[Theory]` tests. The
TRX report extension is a package reference of the test projects as well; it is inactive until a
run passes `--report-trx`, which the CI build does so that ReqStream receives TRX files for
requirements traceability. A local `build.ps1` run does not request TRX files. xUnit does not provide
a self-validation mode; the project's own passing test suite is the integration evidence, and no dedicated test is
written for the framework. Every test runs under xUnit, so passing tests show that it discovers and executes them;
the TRX files are produced by the test-run configuration (`--report-trx` in the CI build), and the ReqStream trace
matrix being satisfied by the results they contain is the evidence for the TRX report. No
additional initialization or disposal is required beyond the standard test runner lifecycle.
