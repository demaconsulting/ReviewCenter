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
///     The Entity Framework Core model of the ReviewCenter database: users, the registry of git repositories, projects, reviews, review files, the
///     comment threads of a review with their comments, the review checklist (frozen snapshot, section
///     applicability and answers), the reviewer slots with their signatures, and the personal access tokens.
/// </summary>
/// <remarks>
///     The model maps the Core entities as they are, so Core stays free of any database library.
///     The schema is created with <c>EnsureCreated</c> (see <see cref="DatabaseSetup"/>); there are no
///     migrations yet, so a database created before users, threads, checklists, slots or signatures existed does
///     not gain their tables or columns and must be deleted and recreated (this includes the repository registry and
///     the repository source columns of reviews and review files, the personal access token table and the creator
///     columns of reviews). Instances are short-lived and not thread-safe: create one per operation
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
    ///     The width of a column that holds a commit identifier in hexadecimal (40 characters, or 64 for SHA-256 repositories).
    /// </summary>
    private const int CommitShaColumnLength = 64;

    /// <summary>
    ///     The width of the column that holds the version label of a checklist.
    /// </summary>
    private const int ChecklistVersionColumnLength = 64;

    /// <summary>
    ///     The width of a column that holds a checklist section or check identifier.
    /// </summary>
    private const int ChecklistIdColumnLength = 32;

    /// <summary>
    ///     The width of the column that holds the meaning text of a signature.
    /// </summary>
    private const int SignatureMeaningColumnLength = 256;

    /// <summary>
    ///     The width of the column that holds an encrypted repository token; far wider than the encryption of a
    ///     token of the longest allowed length.
    /// </summary>
    private const int EncryptedTokenColumnLength = 4000;

    /// <summary>
    ///     The width of the column that holds the short display id of a personal access token; wider than the id itself.
    /// </summary>
    private const int TokenDisplayIdColumnLength = 16;

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
    ///     Gets the table of frozen review checklists.
    /// </summary>
    public DbSet<ReviewChecklist> ReviewChecklists => Set<ReviewChecklist>();

    /// <summary>
    ///     Gets the table of checklist section applicability.
    /// </summary>
    public DbSet<ChecklistSectionState> ChecklistSectionStates => Set<ChecklistSectionState>();

    /// <summary>
    ///     Gets the table of checklist answers.
    /// </summary>
    public DbSet<ChecklistAnswer> ChecklistAnswers => Set<ChecklistAnswer>();

    /// <summary>
    ///     Gets the users table.
    /// </summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>
    ///     Gets the table of reviewer slots.
    /// </summary>
    public DbSet<ReviewerSlot> ReviewerSlots => Set<ReviewerSlot>();

    /// <summary>
    ///     Gets the table of signatures.
    /// </summary>
    public DbSet<ReviewSignature> Signatures => Set<ReviewSignature>();

    /// <summary>
    ///     Gets the table of audit entries.
    /// </summary>
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    /// <summary>
    ///     Gets the table of registered git repositories.
    /// </summary>
    public DbSet<RepositoryRecord> Repositories => Set<RepositoryRecord>();

    /// <summary>
    ///     Gets the table of project memberships.
    /// </summary>
    public DbSet<ProjectMembership> ProjectMemberships => Set<ProjectMembership>();

    /// <summary>
    ///     Gets the table of personal access tokens.
    /// </summary>
    public DbSet<PersonalAccessToken> PersonalAccessTokens => Set<PersonalAccessToken>();

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

            // Evidence settings: seven boolean columns; the defaults match EvidenceSettings.Default. There is no
            // migration: the database is created from this model and recreated while the product is pre-release
            project.ComplexProperty(
                p => p.Evidence,
                evidence =>
                {
                    evidence.Property(e => e.AnonymousIndex).HasColumnName("EvidenceAnonymousIndex").HasDefaultValue(false);
                    evidence.Property(e => e.AnonymousAttestations).HasColumnName("EvidenceAnonymousAttestations").HasDefaultValue(false);
                    evidence.Property(e => e.IncludeThreads).HasColumnName("EvidenceIncludeThreads").HasDefaultValue(true);
                    evidence.Property(e => e.IncludeReviewerNames).HasColumnName("EvidenceIncludeReviewerNames").HasDefaultValue(true);
                    evidence.Property(e => e.IncludeChecklistWording).HasColumnName("EvidenceIncludeChecklistWording").HasDefaultValue(true);
                    evidence.Property(e => e.IncludeFiles).HasColumnName("EvidenceIncludeFiles").HasDefaultValue(true);
                    evidence.Property(e => e.IncludeGitContext).HasColumnName("EvidenceIncludeGitContext").HasDefaultValue(true);
                });
        });

        // Reviews: composite key, state stored by name so new states never renumber old rows
        modelBuilder.Entity<Review>(review =>
        {
            review.HasKey(r => new { r.ProjectId, r.Id });
            review.Property(r => r.Id).HasMaxLength(ReviewLimits.MaxReviewIdLength);
            review.Property(r => r.Title).IsRequired().HasMaxLength(ReviewLimits.MaxReviewTitleLength);
            review.Property(r => r.State).HasConversion<string>().HasMaxLength(StateColumnLength);
            review.Property(r => r.ContentFingerprint).IsRequired().HasMaxLength(Sha256HexColumnLength);

            // Who created the review and whether through the API (provenance only; null and false for older rows)
            review.Property(r => r.CreatedBy).HasMaxLength(ReviewLimits.MaxUsernameLength);
            review.Property(r => r.CreatedViaApi).HasDefaultValue(false);
            // Source of a repository review (all null for an upload): the repository identity is a plain number,
            // not a foreign key, so the review keeps its reference whatever happens to the registration
            review.Property(r => r.RepositoryName).HasMaxLength(RepositoryService.MaxNameLength);
            review.Property(r => r.BaselineSha).HasMaxLength(CommitShaColumnLength);
            review.Property(r => r.HeadSha).HasMaxLength(CommitShaColumnLength);
            review.Property(r => r.Selection).HasMaxLength(ReviewLimits.MaxSelectionLength);

            // The review-set a review was planned from (null otherwise). The index supports the planner's lookup
            // of the earlier reviews of a set; it is not unique because a set is reviewed again and again
            review.Property(r => r.ReviewSetId).HasMaxLength(ReviewLimits.MaxReviewSetIdLength);
            review.HasIndex(r => new { r.ProjectId, r.RepositoryId, r.ReviewSetId });
            review.HasOne<Project>().WithMany().HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Cascade);
            review.HasMany(r => r.Files)
                .WithOne()
                .HasForeignKey(f => new { f.ProjectId, f.ReviewId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Review files: the bytes are BLOBs, kept verbatim. The head columns describe the file at the head (empty for
        // a deleted file); the baseline columns are null when the file had no baseline
        modelBuilder.Entity<ReviewFile>(file =>
        {
            file.HasKey(f => new { f.ProjectId, f.ReviewId, f.Path });
            file.Property(f => f.ReviewId).HasMaxLength(ReviewLimits.MaxReviewIdLength);
            file.Property(f => f.Path).HasMaxLength(ReviewLimits.MaxPathLength);
            file.Property(f => f.Content).IsRequired();
            file.Property(f => f.Sha256).IsRequired().HasMaxLength(Sha256HexColumnLength);
            file.Property(f => f.ChangeKind).HasConversion<string>().HasMaxLength(StateColumnLength);
            file.Property(f => f.BaselineSha256).HasMaxLength(Sha256HexColumnLength);
        });

        // Threads: identity assigned by the database; tied to a file of the review, so a thread can never
        // name a file that is not there, and it goes away with its file. State is stored by name
        modelBuilder.Entity<ReviewThread>(thread =>
        {
            thread.HasKey(t => t.Id);
            thread.Property(t => t.ReviewId).HasMaxLength(ReviewLimits.MaxReviewIdLength);
            thread.Property(t => t.FilePath).IsRequired().HasMaxLength(ReviewLimits.MaxPathLength);
            thread.Property(t => t.State).HasConversion<string>().HasMaxLength(StateColumnLength);
            thread.Property(t => t.Side).HasConversion<string>().HasMaxLength(StateColumnLength).HasDefaultValue(ThreadSide.Head);
            thread.HasOne<ReviewFile>()
                .WithMany()
                .HasForeignKey(t => new { t.ProjectId, t.ReviewId, t.FilePath })
                .OnDelete(DeleteBehavior.Cascade);
            thread.HasMany(t => t.Comments)
                .WithOne()
                .HasForeignKey(c => c.ThreadId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Users: identity assigned by the database; the user name is unique without regard to case (the NOCASE
        // collation makes the unique index and every lookup case-insensitive); the role is stored by name
        modelBuilder.Entity<User>(user =>
        {
            user.HasKey(u => u.Id);
            user.Property(u => u.Username).IsRequired().HasMaxLength(ReviewLimits.MaxUsernameLength)
                .UseCollation("NOCASE");
            user.HasIndex(u => u.Username).IsUnique();
            user.Property(u => u.DisplayName).IsRequired().HasMaxLength(ReviewLimits.MaxDisplayNameLength);
            user.Property(u => u.IsSystemAdmin);
            user.Property(u => u.MustChangePassword);
            user.Property(u => u.PasswordHash).IsRequired();

            // The identity at an external provider (both null for a local account): compared exactly, unique as a
            // pair; SQLite treats nulls as distinct, so any number of local accounts is fine
            user.Property(u => u.ExternalIssuer).HasMaxLength(ReviewLimits.MaxExternalIssuerLength);
            user.Property(u => u.ExternalSubject).HasMaxLength(ReviewLimits.MaxExternalSubjectLength);
            user.HasIndex(u => new { u.ExternalIssuer, u.ExternalSubject }).IsUnique();
        });

        // Personal access tokens: only the SHA-256 hash of the value is stored (unique, and the key of the lookup when
        // a request presents a token); the display id is a random, non-secret label for lists and the audit trail.
        // A token goes away with its user
        modelBuilder.Entity<PersonalAccessToken>(token =>
        {
            token.HasKey(t => t.Id);
            token.Property(t => t.Name).IsRequired().HasMaxLength(ReviewLimits.MaxTokenNameLength);
            token.Property(t => t.DisplayId).IsRequired().HasMaxLength(TokenDisplayIdColumnLength);
            token.Property(t => t.TokenHash).IsRequired().HasMaxLength(Sha256HexColumnLength);
            token.HasIndex(t => t.TokenHash).IsUnique();
            token.HasIndex(t => t.UserId);
            token.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // Repositories: identity assigned by the database; every repository belongs to exactly one project (and goes
        // with it); the name is unique within the project without regard to case (NOCASE); the token is kept only in
        // encrypted form
        modelBuilder.Entity<RepositoryRecord>(repository =>
        {
            repository.ToTable("Repositories");
            repository.HasKey(r => r.Id);
            repository.Property(r => r.Name).IsRequired().HasMaxLength(RepositoryService.MaxNameLength)
                .UseCollation("NOCASE");
            repository.HasIndex(r => new { r.ProjectId, r.Name }).IsUnique();
            repository.HasOne<Project>().WithMany().HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Cascade);
            repository.Property(r => r.Location).IsRequired().HasMaxLength(RepositoryService.MaxLocationLength);
            repository.Property(r => r.Username).HasMaxLength(RepositoryService.MaxUsernameLength);
            repository.Property(r => r.EncryptedToken).HasMaxLength(EncryptedTokenColumnLength);
        });

        // Project memberships: one row per user and project (the composite key is the uniqueness guarantee); the role
        // is stored by name; a membership goes away with its user or its project
        modelBuilder.Entity<ProjectMembership>(membership =>
        {
            membership.ToTable("ProjectMemberships");
            membership.HasKey(m => new { m.UserId, m.ProjectId });
            membership.Property(m => m.Role).HasConversion<string>().HasMaxLength(StateColumnLength);
            membership.HasIndex(m => m.ProjectId);
            membership.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
            membership.HasOne<Project>().WithMany().HasForeignKey(m => m.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        // Comments: identity assigned by the database, so it grows with posting order
        modelBuilder.Entity<ReviewComment>(comment =>
        {
            comment.HasKey(c => c.Id);
            comment.Property(c => c.Author).IsRequired().HasMaxLength(ReviewLimits.MaxAuthorLength);
            comment.Property(c => c.AuthorUsername).IsRequired().HasMaxLength(ReviewLimits.MaxUsernameLength);
            comment.Property(c => c.Text).IsRequired().HasMaxLength(ReviewLimits.MaxCommentLength);
        });

        // Checklists: one frozen snapshot per review, and the answers and section applicability keyed by the
        // check or section identifier within it; all go away with the review
        modelBuilder.Entity<ReviewChecklist>(checklist =>
        {
            checklist.HasKey(c => new { c.ProjectId, c.ReviewId });
            checklist.Property(c => c.ReviewId).HasMaxLength(ReviewLimits.MaxReviewIdLength);
            checklist.Property(c => c.Version).IsRequired().HasMaxLength(ChecklistVersionColumnLength);
            checklist.Property(c => c.Definition).IsRequired();
        });

        modelBuilder.Entity<Review>()
            .HasOne(r => r.Checklist)
            .WithOne()
            .HasForeignKey<ReviewChecklist>(c => new { c.ProjectId, c.ReviewId })
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ChecklistSectionState>(section =>
        {
            section.HasKey(s => new { s.ProjectId, s.ReviewId, s.SectionId });
            section.Property(s => s.ReviewId).HasMaxLength(ReviewLimits.MaxReviewIdLength);
            section.Property(s => s.SectionId).HasMaxLength(ChecklistIdColumnLength);
            section.Property(s => s.Reason).HasMaxLength(ReviewLimits.MaxJustificationLength);
            section.Property(s => s.SetBy).IsRequired().HasMaxLength(ReviewLimits.MaxAuthorLength);
            section.Property(s => s.SetByUsername).IsRequired().HasMaxLength(ReviewLimits.MaxUsernameLength);
            section.HasOne<Review>()
                .WithMany()
                .HasForeignKey(s => new { s.ProjectId, s.ReviewId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChecklistAnswer>(answer =>
        {
            answer.HasKey(a => new { a.ProjectId, a.ReviewId, a.ItemId });
            answer.Property(a => a.ReviewId).HasMaxLength(ReviewLimits.MaxReviewIdLength);
            answer.Property(a => a.ItemId).HasMaxLength(ChecklistIdColumnLength);
            answer.Property(a => a.Outcome).HasConversion<string>().HasMaxLength(StateColumnLength);
            answer.Property(a => a.Justification).HasMaxLength(ReviewLimits.MaxJustificationLength);
            answer.Property(a => a.AnsweredBy).IsRequired().HasMaxLength(ReviewLimits.MaxAuthorLength);
            answer.Property(a => a.AnsweredByUsername).IsRequired().HasMaxLength(ReviewLimits.MaxUsernameLength);
            answer.HasOne<Review>()
                .WithMany()
                .HasForeignKey(a => new { a.ProjectId, a.ReviewId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Reviewer slots: identity assigned by the database; they hold user identities only and go away with
        // the review
        modelBuilder.Entity<ReviewerSlot>(slot =>
        {
            slot.HasKey(s => s.Id);
            slot.Property(s => s.ReviewId).HasMaxLength(ReviewLimits.MaxReviewIdLength);
            slot.HasOne<Review>()
                .WithMany()
                .HasForeignKey(s => new { s.ProjectId, s.ReviewId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Signatures: append-only (nothing in the application updates or deletes a row). A slot with a signature
        // cannot be deleted (the foreign key to the slot is checked at the end of the statement, so deleting a whole
        // review still cascades), and the unique index stops a double submit from storing the same signature twice
        modelBuilder.Entity<ReviewSignature>(signature =>
        {
            signature.HasKey(s => s.Id);
            signature.Property(s => s.ReviewId).HasMaxLength(ReviewLimits.MaxReviewIdLength);
            signature.Property(s => s.DisplayName).IsRequired().HasMaxLength(ReviewLimits.MaxDisplayNameLength);
            signature.Property(s => s.Role).HasConversion<string>().HasMaxLength(StateColumnLength);
            signature.Property(s => s.RecordDigest).IsRequired().HasMaxLength(Sha256HexColumnLength);
            signature.Property(s => s.Meaning).IsRequired().HasMaxLength(SignatureMeaningColumnLength);
            signature.HasIndex(s => new { s.SlotId, s.RecordDigest }).IsUnique();
            signature.HasOne<Review>()
                .WithMany()
                .HasForeignKey(s => new { s.ProjectId, s.ReviewId })
                .OnDelete(DeleteBehavior.Cascade);
            signature.HasOne<ReviewerSlot>()
                .WithMany()
                .HasForeignKey(s => s.SlotId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // Audit trail: append-only (nothing in the application updates or deletes a row). The sequence is the key,
        // assigned by the appender under the process-wide gate (never by the database), so a duplicate sequence
        // is refused by the primary key. There are no foreign keys: an entry is a snapshot of text and must
        // survive whatever happens to the rows it describes. The index serves the history of one review
        modelBuilder.Entity<AuditEntry>(entry =>
        {
            entry.HasKey(a => a.Sequence);
            entry.Property(a => a.Sequence).ValueGeneratedNever();
            entry.Property(a => a.ActorName).IsRequired().HasMaxLength(AuditText.MaxActorNameLength);
            entry.Property(a => a.Action).IsRequired().HasMaxLength(AuditText.MaxActionLength);
            entry.Property(a => a.ReviewId).HasMaxLength(ReviewLimits.MaxReviewIdLength);
            entry.Property(a => a.Detail).IsRequired().HasMaxLength(AuditText.MaxDetailLength);
            entry.Property(a => a.PreviousHash).IsRequired().HasMaxLength(Sha256HexColumnLength);
            entry.Property(a => a.Hash).IsRequired().HasMaxLength(Sha256HexColumnLength);
            entry.HasIndex(a => new { a.ProjectId, a.ReviewId, a.Sequence });
        });
    }
}
