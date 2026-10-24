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

using DemaConsulting.ReviewCenter.Core;
using Microsoft.EntityFrameworkCore;

namespace DemaConsulting.ReviewCenter.Storage;

/// <summary>
///     The Entity Framework Core model of the ReviewCenter database: projects, reviews, review files, and the
///     comment threads of a review with their comments.
/// </summary>
/// <remarks>
///     The model maps the Core entities as they are, so Core stays free of any database library.
///     The schema is created with <c>EnsureCreated</c> (see <see cref="DatabaseSetup"/>); there are no
///     migrations yet, so a database created before threads existed does not gain the thread tables and
///     must be recreated. Instances are short-lived and not thread-safe: create one per operation
///     through <see cref="IDbContextFactory{TContext}"/>.
/// </remarks>
internal sealed class ReviewCenterDbContext : DbContext
{
    /// <summary>
    ///     The width of the column that holds a review state by name; wider than the longest state name.
    /// </summary>
    private const int StateColumnLength = 32;

    /// <summary>
    ///     The width of a column that holds a SHA-256 value written as lowercase hexadecimal (64 characters).
    /// </summary>
    private const int Sha256HexColumnLength = 64;
    /// <summary>
    ///     Initializes a new instance of the <see cref="ReviewCenterDbContext"/> class.
    /// </summary>
    /// <param name="options">The context options that select the provider and connection.</param>
    public ReviewCenterDbContext(DbContextOptions<ReviewCenterDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    ///     Gets the projects table.
    /// </summary>
    public DbSet<Project> Projects => Set<Project>();

    /// <summary>
    ///     Gets the reviews table.
    /// </summary>
    public DbSet<Review> Reviews => Set<Review>();

    /// <summary>
    ///     Gets the review files table.
    /// </summary>
    public DbSet<ReviewFile> ReviewFiles => Set<ReviewFile>();

    /// <summary>
    ///     Gets the review threads table.
    /// </summary>
    public DbSet<ReviewThread> Threads => Set<ReviewThread>();

    /// <summary>
    ///     Gets the review comments table.
    /// </summary>
    public DbSet<ReviewComment> Comments => Set<ReviewComment>();

    /// <summary>
    ///     Configures keys, relationships, length limits and conversions.
    /// </summary>
    /// <param name="modelBuilder">The builder used to describe the model.</param>
    /// <remarks>
    ///     A review is keyed by (project, id) and a file by (project, review, path). The composite
    ///     primary key of the review doubles as the uniqueness guarantee that backs up the
    ///     duplicate-id check of the review service when two requests race.
    /// </remarks>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Projects: identity assigned by the database
        modelBuilder.Entity<Project>(project =>
        {
            project.HasKey(p => p.Id);
            project.Property(p => p.Name).IsRequired().HasMaxLength(ReviewLimits.MaxProjectNameLength);
            project.Property(p => p.Description).IsRequired().HasMaxLength(ReviewLimits.MaxProjectDescriptionLength);
        });

        // Reviews: composite key, state stored by name so new states never renumber old rows
        modelBuilder.Entity<Review>(review =>
        {
            review.HasKey(r => new { r.ProjectId, r.Id });
            review.Property(r => r.Id).HasMaxLength(ReviewLimits.MaxReviewIdLength);
            review.Property(r => r.Title).IsRequired().HasMaxLength(ReviewLimits.MaxReviewTitleLength);
            review.Property(r => r.State).HasConversion<string>().HasMaxLength(StateColumnLength);
            review.Property(r => r.ContentFingerprint).IsRequired().HasMaxLength(Sha256HexColumnLength);
            review.HasOne<Project>().WithMany().HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Cascade);
            review.HasMany(r => r.Files)
                .WithOne()
                .HasForeignKey(f => new { f.ProjectId, f.ReviewId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Review files: the bytes are a BLOB, kept verbatim
        modelBuilder.Entity<ReviewFile>(file =>
        {
            file.HasKey(f => new { f.ProjectId, f.ReviewId, f.Path });
            file.Property(f => f.ReviewId).HasMaxLength(ReviewLimits.MaxReviewIdLength);
            file.Property(f => f.Path).HasMaxLength(ReviewLimits.MaxPathLength);
            file.Property(f => f.Content).IsRequired();
            file.Property(f => f.Sha256).IsRequired().HasMaxLength(Sha256HexColumnLength);
        });

        // Threads: identity assigned by the database; tied to a file of the review, so a thread can never
        // name a file that is not there, and it goes away with its file. State is stored by name
        modelBuilder.Entity<ReviewThread>(thread =>
        {
            thread.HasKey(t => t.Id);
            thread.Property(t => t.ReviewId).HasMaxLength(ReviewLimits.MaxReviewIdLength);
            thread.Property(t => t.FilePath).IsRequired().HasMaxLength(ReviewLimits.MaxPathLength);
            thread.Property(t => t.State).HasConversion<string>().HasMaxLength(StateColumnLength);
            thread.HasOne<ReviewFile>()
                .WithMany()
                .HasForeignKey(t => new { t.ProjectId, t.ReviewId, t.FilePath })
                .OnDelete(DeleteBehavior.Cascade);
            thread.HasMany(t => t.Comments)
                .WithOne()
                .HasForeignKey(c => c.ThreadId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Comments: identity assigned by the database, so it grows with posting order
        modelBuilder.Entity<ReviewComment>(comment =>
        {
            comment.HasKey(c => c.Id);
            comment.Property(c => c.Author).IsRequired().HasMaxLength(ReviewLimits.MaxAuthorLength);
            comment.Property(c => c.Text).IsRequired().HasMaxLength(ReviewLimits.MaxCommentLength);
        });
    }
}
