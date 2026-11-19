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

using BenchmarkDotNet.Attributes;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Benchmarks;

/// <summary>
///     Measures the time to read one filtered page of the audit trail from SQLite for trails of different lengths:
///     the design of <c>AuditStore</c> walks an index in keyset order and stops when the page is full, so the time of
///     a page should not grow with the length of the trail.
/// </summary>
[MemoryDiagnoser]
public class AuditStoreBenchmarks
{
    /// <summary>
    ///     The actions of the entries, in turn.
    /// </summary>
    private static readonly string[] Actions = ["review.created", "review.signed", "project.created", "user.created"];

    /// <summary>
    ///     The folder of the database file.
    /// </summary>
    private string _folder = string.Empty;

    /// <summary>
    ///     The service provider that owns the context factory.
    /// </summary>
    private ServiceProvider? _provider;

    /// <summary>
    ///     The store under measurement.
    /// </summary>
    private AuditStore? _store;

    /// <summary>
    ///     The filter of the page: one project, one actor, one group of actions.
    /// </summary>
    private readonly AuditFilter _filter = new(ProjectId: 2, ActorText: "actor 1", ActionPrefix: "review.");

    /// <summary>
    ///     Gets or sets the number of entries in the trail.
    /// </summary>
    [Params(1_000, 10_000, 100_000)]
    public int TrailLength { get; set; }

    /// <summary>
    ///     Creates a database with a trail of the current length, spread over three projects, five actors, and four
    ///     actions.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _folder = Directory.CreateTempSubdirectory("reviewcenter-benchmark-").FullName;
        var options = new DatabaseOptions
        {
            ConnectionString = $"Data Source={Path.Combine(_folder, "benchmark.db")};Pooling=False"
        };
        _provider = new ServiceCollection()
            .AddDbContextFactory<ReviewCenterDbContext>(builder => DatabaseSetup.Configure(builder, options))
            .BuildServiceProvider();
        var factory = _provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>();
        DatabaseSetup.EnsureCreated(factory, options);
        _store = new AuditStore(factory, new AuditAppender(TimeProvider.System));

        var start = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var context = factory.CreateDbContext();
        var previous = AuditChain.GenesisHash;
        for (var i = 0; i < TrailLength; i++)
        {
            var pending = new AuditEntry
            {
                Sequence = i + 1,
                TimestampUtc = start.AddMinutes(i),
                ActorName = "Actor " + (i % 5),
                Action = Actions[i % Actions.Length],
                ProjectId = (i % 3) + 1,
                ReviewId = "r" + (i % 7),
                Detail = "detail " + (i + 1),
                PreviousHash = previous
            };
            var entry = new AuditEntry
            {
                Sequence = pending.Sequence,
                TimestampUtc = pending.TimestampUtc,
                ActorName = pending.ActorName,
                Action = pending.Action,
                ProjectId = pending.ProjectId,
                ReviewId = pending.ReviewId,
                Detail = pending.Detail,
                PreviousHash = pending.PreviousHash,
                Hash = AuditChain.ComputeHash(pending)
            };
            previous = entry.Hash;
            context.AuditEntries.Add(entry);
        }

        context.SaveChanges();
    }

    /// <summary>
    ///     Removes the database.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _provider?.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    /// <summary>
    ///     Reads the newest filtered page of 50 entries.
    /// </summary>
    /// <returns>The page.</returns>
    [Benchmark]
    public async Task<IReadOnlyList<AuditEntry>> NewestFilteredPage()
    {
        return await _store!.ListAsync(_filter, null, null, 50);
    }
}