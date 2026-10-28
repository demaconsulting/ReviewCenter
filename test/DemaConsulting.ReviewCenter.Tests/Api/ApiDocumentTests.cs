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

using System.Net;
using System.Text;
using static DemaConsulting.ReviewCenter.Tests.Api.ApiTestSupport;

namespace DemaConsulting.ReviewCenter.Tests.Api;

/// <summary>
///     Tests for the OpenAPI document of the API: that it is generated, what it says about authentication, that
///     it is the same on every request, and that it matches the snapshot committed in <c>docs/api/openapi.json</c>.
/// </summary>
public class ApiDocumentTests
{
    /// <summary>
    ///     The environment variable that makes the snapshot test write the snapshot instead of comparing it.
    /// </summary>
    private const string UpdateVariable = "REVIEWCENTER_UPDATE_OPENAPI";

    /// <summary>
    ///     The complete command that regenerates the snapshot, for the PowerShell and the bash shell. It runs only the
    ///     snapshot test with the update variable set, and is repeated in the failure messages of that test.
    /// </summary>
    private const string RegenerateCommand =
        "PowerShell: $env:REVIEWCENTER_UPDATE_OPENAPI = \"1\"; dotnet test --project test/DemaConsulting.ReviewCenter.Tests "
        + "--filter-method \"*ApiDocument_Get_MatchesCommittedSnapshot\"; Remove-Item Env:REVIEWCENTER_UPDATE_OPENAPI"
        + "  |  bash: REVIEWCENTER_UPDATE_OPENAPI=1 dotnet test --project test/DemaConsulting.ReviewCenter.Tests "
        + "--filter-method \"*ApiDocument_Get_MatchesCommittedSnapshot\"";

    /// <summary>
    ///     Test that the document is served without a token, describes the API operations with a fixed version,
    ///     no server address and the bearer and Basic schemes, and requires one of them for every operation.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task ApiDocument_Get_NoToken_DescribesTheApiWithBearerScheme()
    {
        // Arrange: an application and a client without a token
        await using var app = await StartAsync();
        using var client = ClientFor(app, null);

        // Act: fetch the document
        var response = await client.GetAsync("/api/openapi.json", TestContext.Current.CancellationToken);
        using var json = await JsonAsync(response);
        var root = json.RootElement;

        // Assert: served openly, with the fixed parts
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1", root.GetProperty("info").GetProperty("version").GetString());
        Assert.False(root.TryGetProperty("servers", out var servers) && servers.GetArrayLength() > 0);
        var scheme = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("bearer");
        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
        var basicScheme = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("basic");
        Assert.Equal("http", basicScheme.GetProperty("type").GetString());
        Assert.Equal("basic", basicScheme.GetProperty("scheme").GetString());

        // Assert: only the API, every operation secured with either scheme
        var paths = root.GetProperty("paths").EnumerateObject().ToList();
        Assert.Contains(paths, p => p.Name == "/api/projects");
        Assert.Contains(paths, p => p.Name == "/api/projects/{projectId}/reviews/{reviewId}/attestation.pdf");
        Assert.Contains(paths, p => p.Name == "/api/projects/{projectId}/evidence/index.json");
        Assert.All(paths, p => Assert.StartsWith("/api/", p.Name, StringComparison.Ordinal));
        Assert.DoesNotContain(paths, p => p.Name.Contains("openapi", StringComparison.Ordinal));
        var operations = paths.SelectMany(p => p.Value.EnumerateObject()).Where(o => o.Value.ValueKind == System.Text.Json.JsonValueKind.Object).ToList();
        Assert.NotEmpty(operations);
        Assert.All(
            operations,
            o =>
            {
                // Bearer and Basic; the two routes a project can open also allow no credentials (an empty alternative)
                var open = o.Value.GetProperty("operationId").GetString() is "GetEvidenceIndex" or "GetAttestation";
                Assert.True(o.Value.TryGetProperty("security", out var security));
                Assert.Equal(open ? 3 : 2, security.GetArrayLength());
                Assert.Equal(open, security.EnumerateArray().Any(r => !r.EnumerateObject().Any()));
            });
    }

    /// <summary>
    ///     Test that two applications generate byte-for-byte the same document, so the snapshot does not depend on
    ///     the machine, the host address or the run.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task ApiDocument_Get_TwoApplications_AreIdentical()
    {
        // Arrange and Act: generate the document twice, in two applications
        var first = await GenerateAsync(TestContext.Current.CancellationToken);
        var second = await GenerateAsync(TestContext.Current.CancellationToken);

        // Assert: the same text
        Assert.Equal(first, second);
    }

    /// <summary>
    ///     Test that the generated document is the one committed in <c>docs/api/openapi.json</c>. When the API
    ///     changes on purpose, run only this test with the environment variable <c>REVIEWCENTER_UPDATE_OPENAPI</c>
    ///     set to <c>1</c> to write the new snapshot, and review the change. In PowerShell:
    ///     <c>$env:REVIEWCENTER_UPDATE_OPENAPI = "1"; dotnet test --project test/DemaConsulting.ReviewCenter.Tests
    ///     --filter-method "*ApiDocument_Get_MatchesCommittedSnapshot"</c>; in bash the same command is prefixed with
    ///     <c>REVIEWCENTER_UPDATE_OPENAPI=1</c>.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task ApiDocument_Get_MatchesCommittedSnapshot()
    {
        // Arrange: the generated document and the place of the snapshot
        var generated = await GenerateAsync(TestContext.Current.CancellationToken);
        var path = Path.Combine(FindRepositoryRoot(), "docs", "api", "openapi.json");

        // Act and Assert: write it when asked to, otherwise compare it
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, generated, new UTF8Encoding(false), TestContext.Current.CancellationToken);
            return;
        }

        Assert.True(File.Exists(path), $"The snapshot '{path}' is missing. Create it with: {RegenerateCommand}");
        var committed = Normalize(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.True(
            committed == generated,
            "The OpenAPI document differs from docs/api/openapi.json. If the change of the API is intended, write the new "
            + $"snapshot with: {RegenerateCommand}  Then review the change and commit it.");
    }

    /// <summary>
    ///     Starts an application and returns its OpenAPI document with normalized line endings and a final newline.
    /// </summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The document text.</returns>
    private static async Task<string> GenerateAsync(CancellationToken cancellationToken)
    {
        await using var app = await StartAsync();
        using var client = ClientFor(app, null);
        var text = await client.GetStringAsync("/api/openapi.json", cancellationToken);
        return Normalize(text);
    }

    /// <summary>
    ///     Makes the line endings LF and ends the text with one newline, so a checkout setting cannot make a
    ///     difference.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The normalized text.</returns>
    private static string Normalize(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n') + "\n";
    }

    /// <summary>
    ///     Finds the root of the repository: the folder above the test binaries that has the solution file.
    /// </summary>
    /// <returns>The folder.</returns>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DemaConsulting.ReviewCenter.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found above the test binaries.");
    }
}
