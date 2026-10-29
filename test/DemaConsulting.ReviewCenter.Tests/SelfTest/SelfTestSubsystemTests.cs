// Copyright (c) DEMA Consulting
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Text.RegularExpressions;
using DemaConsulting.ReviewCenter.Cli;
using DemaConsulting.ReviewCenter.SelfTest;

namespace DemaConsulting.ReviewCenter.Tests.SelfTest;

/// <summary>
///     Subsystem tests for the SelfTest subsystem covering Validation workflows. The workflows that must see the
///     real checks read the one shared real run; the others use fast stand-in checks.
/// </summary>
[Collection("Sequential")]
public class SelfTestSubsystemTests
{
    /// <summary>
    ///     Test that self-test subsystem can run the real validation workflow without result files.
    /// </summary>
    [Fact]
    public void SelfTestSubsystem_ValidationWorkflow_NoResultFiles_CompletesSuccessfully()
    {
        // Arrange: the shared real run of the default checks (see RealValidationRun)

        // Act: read the outcome of the real run
        var run = RealValidationRun.Result;

        // Assert: validation completes successfully, all eight checks pass and each line shows its elapsed time
        Assert.True(run.Validate, "Context should have validate flag set");
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Total Tests: 8", run.Log);
        Assert.Contains("Passed: 8", run.Log);
        Assert.Contains("Skipped: 0", run.Log);
        Assert.Contains("Failed: 0", run.Log);
        foreach (var name in RealValidationRun.CheckNames)
        {
            Assert.Matches($@"✓ {name} - Passed \(\d+ ms\)", run.Log);
        }
    }

    /// <summary>
    ///     Test that self-test subsystem records every real check in the TRX result file.
    /// </summary>
    [Fact]
    public void SelfTestSubsystem_ValidationWorkflow_WithTrxFile_GeneratesResults()
    {
        // Arrange: the shared real run, which asked for a TRX file

        // Act: read the TRX text of the real run
        var run = RealValidationRun.Result;

        // Assert: the TRX has the standard format and each of the eight checks, passed, with a duration
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("<TestRun", run.Trx);
        Assert.Equal(8, Regex.Matches(run.Trx, "outcome=\"Passed\"").Count);
        Assert.DoesNotContain("Failed", run.Trx);
        Assert.DoesNotContain("NotExecuted", run.Trx);
        foreach (var name in RealValidationRun.CheckNames)
        {
            Assert.Contains(name, run.Trx);
        }
    }

    /// <summary>
    ///     Test that the default real run never touches the configured database and never runs the opt-in check.
    /// </summary>
    [Fact]
    public void SelfTestSubsystem_ValidationWorkflow_DefaultRun_IsHermetic()
    {
        // Arrange: the shared real run; the test process configures a database file that must stay absent

        // Act: read the outcome of the real run
        var run = RealValidationRun.Result;

        // Assert: the configured database was not created and the opt-in check did not run
        Assert.False(run.ConfiguredDatabaseCreated, "The default run must not create the configured database");
        Assert.DoesNotContain("ReviewCenter_ConfiguredDatabase", run.Log);
        Assert.DoesNotContain("ReviewCenter_ConfiguredDatabase", run.Trx);
    }

    /// <summary>
    ///     Test that the real run is fast enough to run on every build.
    /// </summary>
    [Fact]
    public void SelfTestSubsystem_ValidationWorkflow_RealRun_FinishesWithinBound()
    {
        // Arrange: the shared real run

        // Act: read how long it took
        var run = RealValidationRun.Result;

        // Assert: well inside the per-check time limit; a hung check would take a minute
        Assert.True(run.Elapsed < TimeSpan.FromSeconds(45), $"The real run took {run.Elapsed.TotalSeconds:0.0} s");
    }

    /// <summary>
    ///     Test that self-test subsystem can run validation workflow with JUnit result file.
    /// </summary>
    [Fact]
    public void SelfTestSubsystem_ValidationWorkflow_WithJUnitFile_GeneratesResults()
    {
        // Arrange: temporary JUnit XML file path and fast checks
        var junitFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.xml");
        var args = new[] { "--validate", "--silent", "--results", junitFile };

        try
        {
            // Act: create context and run validation with JUnit XML output
            using var context = Context.Create(args);
            Validation.Run(context, [FastValidationChecks.Passing("ReviewCenter_One"), FastValidationChecks.Passing("ReviewCenter_Two")]);

            // Assert: validation completes and generates JUnit XML file with standard format
            Assert.True(context.Validate, "Context should have validate flag set");
            Assert.Equal(0, context.ExitCode);
            Assert.True(File.Exists(junitFile), "JUnit file should be generated");
            var junitContent = File.ReadAllText(junitFile);
            Assert.Contains("<testsuites", junitContent);
            Assert.Contains("ReviewCenter_Two", junitContent);
        }
        finally
        {
            // Cleanup
            if (File.Exists(junitFile))
            {
                File.Delete(junitFile);
            }
        }
    }

    /// <summary>
    ///     Test that self-test subsystem can run validation workflow with both result file formats.
    /// </summary>
    [Fact]
    public void SelfTestSubsystem_ValidationWorkflow_WithBothResultFiles_GeneratesBothResults()
    {
        // Arrange: setup validation arguments for both TRX and JUnit result file outputs
        var tempDir = Path.GetTempPath();
        var trxFile = Path.Combine(tempDir, $"test_{Guid.NewGuid()}.trx");
        var junitFile = Path.Combine(tempDir, $"test_{Guid.NewGuid()}.xml");
        var trxArgs = new[] { "--validate", "--silent", "--results", trxFile };
        var junitArgs = new[] { "--validate", "--silent", "--results", junitFile };
        var checks = new[] { FastValidationChecks.Passing("ReviewCenter_One"), FastValidationChecks.Skipped() };

        try
        {
            // Act: run validation with TRX output
            using (var trxContext = Context.Create(trxArgs))
            {
                Validation.Run(trxContext, checks);

                // Assert: verify validation completed and TRX result file was generated with standard format
                Assert.True(trxContext.Validate, "Context should have validate flag set for TRX run");
                Assert.Equal(0, trxContext.ExitCode);
                Assert.True(File.Exists(trxFile), "TRX file should be generated");
                var trxContent = File.ReadAllText(trxFile);
                Assert.Contains("<TestRun", trxContent);
                Assert.Contains("NotExecuted", trxContent);
            }

            // Act: run validation with JUnit XML output
            using (var junitContext = Context.Create(junitArgs))
            {
                Validation.Run(junitContext, checks);

                // Assert: verify validation completed and JUnit XML result file was generated with standard format
                Assert.True(junitContext.Validate, "Context should have validate flag set for JUnit run");
                Assert.Equal(0, junitContext.ExitCode);
                Assert.True(File.Exists(junitFile), "JUnit file should be generated");
                var junitContent = File.ReadAllText(junitFile);
                Assert.Contains("<testsuites", junitContent);
                Assert.Contains("<skipped", junitContent);
            }
        }
        finally
        {
            // Cleanup
            if (File.Exists(trxFile))
            {
                File.Delete(trxFile);
            }

            if (File.Exists(junitFile))
            {
                File.Delete(junitFile);
            }
        }
    }

    /// <summary>
    ///     Test that self-test subsystem with unsupported results extension emits an error and does not create the file.
    /// </summary>
    [Fact]
    public void SelfTestSubsystem_ValidationWorkflow_WithUnsupportedExtension_EmitsErrorAndNoFile()
    {
        // Arrange: unsupported results file extension; capture stderr for error message assertion
        var tempDir = Path.GetTempPath();
        var badFile = Path.Combine(tempDir, $"test_{Guid.NewGuid()}.bad");
        var args = new[] { "--validate", "--results", badFile };
        var originalError = Console.Error;
        var originalOut = Console.Out;

        try
        {
            using var errWriter = new StringWriter();
            using var outWriter = new StringWriter();
            Console.SetError(errWriter);
            Console.SetOut(outWriter);

            // Act: create context and run validation with unsupported extension
            using var context = Context.Create(args);
            Validation.Run(context, [FastValidationChecks.Passing()]);

            // Assert: error is reported, message identifies the format, and no file is created
            Assert.Equal(1, context.ExitCode);
            Assert.False(File.Exists(badFile), "No file should be created for unsupported extension");
            Assert.Contains(".bad", errWriter.ToString());
        }
        finally
        {
            Console.SetError(originalError);
            Console.SetOut(originalOut);

            // Cleanup
            if (File.Exists(badFile))
            {
                File.Delete(badFile);
            }
        }
    }
}
