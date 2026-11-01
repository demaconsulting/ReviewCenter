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

using System.Globalization;
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
///     columns of reviews). The same is true for the second factor tables (<c>UserTotps</c> and
///     <c>TotpRecoveryCodes</c>): a database from before them must be deleted and recreated. The same is true for
///     the <c>ReviewReopens</c> table, for the <c>RequireIndependentReviewers</c>,
///     <c>RequireNonAuthorChecklistAnswers</c> and <c>RequireNonReopenerSigning</c> columns of projects, and for the
///     <c>DueDate</c> column of reviews, and for the follow-up columns (<c>FollowsReviewId</c>, <c>CarriedAnswerCount</c>
///     and <c>CarriedThreadCount</c> of reviews and the <c>CarriedFromReviewId</c> and <c>CarriedFromThreadId</c> columns
///     of answers, section states and threads). The same is true for the <c>ReviewUserStates</c> table and for the
///     indexes of the reviewer slots and of the creator of reviews (and for the case-insensitive collation of the
///     creator column) and of the audit trail (the project, actor and action indexes and the fixed-width UTC text
///     form of the entry timestamp) and for the <c>LastSignInUtc</c> column of users, and for the <c>IncludeInReport</c>
///     and <c>Kind</c> columns of comments and for the <c>ReviewConclusionText</c> column of reviews and for the
///     <c>Outcome</c> column of signatures: a database from before them
///     must be deleted and recreated. Instances are
///     short-lived and not thread-safe: create one per operation through <see cref="IDbContextFactory{TContext}"/>.
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
    ///     The format of the stored audit timestamp: UTC, always seven fraction digits, so text order is time order.
    /// </summary>
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    /// <summary>
    ///     The width of a column that holds a commit identifier in hexadecimal (40 characters, or 64 for SHA-256 repositories).
    /// </summary>
    private const int CommitShaColumnLength = 64;

    /// <summary>
    ///     The width of the column that holds the version label of a checklist.
    /// </summary>
    private const int ChecklistVersionColumnLength = ReviewLimits.MaxChecklistVersionLength;

    /// <summary>
    ///     The width of a column that holds a checklist section or check identifier.
    /// </summary>
    private const int ChecklistIdColumnLength = ReviewLimits.MaxChecklistIdLength;

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
    ///     Gets the table of the ballot status rows, one per reviewer slot that has started a ballot.
    /// </summary>
    public DbSet<ChecklistBallot> ChecklistBallots => Set<ChecklistBallot>();

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
    ///     Gets the table of review reopens (append-only history).
    /// </summary>
    public DbSet<ReviewReopen> ReviewReopens => Set<ReviewReopen>();

    /// <summary>
    ///     Gets the table of review user states (what each user has seen of each review).
    /// </summary>
    public DbSet<ReviewUserState> ReviewUserStates => Set<ReviewUserState>();

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
    ///     Gets the table of second factor enrollments (at most one per user).
    /// </summary>
    public DbSet<UserTotp> UserTotps => Set<UserTotp>();

    /// <summary>
    ///     Gets the table of recovery codes of the second factor.
    /// </summary>
    public DbSet<TotpRecoveryCode> TotpRecoveryCodes => Set<TotpRecoveryCode>();

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

            // The review rules: independent reviewers are required unless an administrator switches the rule off. No
            // database default is declared, so the value always comes from the entity (true unless set otherwise)
            project.Property(p => p.RequireIndependentReviewers);

            // Two further review rules, stored for the enforcement that follows. Same reasoning: no database default
            project.Property(p => p.RequireNonAuthorChecklistAnswers);
            project.Property(p => p.RequireNonReopenerSigning);

            // The project's own checklist definition (JSON). NULL means the built-in default checklist. The text is
            // not length-limited by the column: ChecklistValidator bounds it (ReviewLimits.MaxChecklistBytes) before
            // it is ever stored
            project.Property(p => p.ChecklistDefinition);

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
            // The inbox finds the reviews a user created, so the creator is indexed; the NOCASE collation (like the user
            // name column) makes the match without regard to case and lets the index serve it
            review.Property(r => r.CreatedBy).HasMaxLength(ReviewLimits.MaxUsernameLength).UseCollation("NOCASE");
            review.Property(r => r.CreatedViaApi).HasDefaultValue(false);
            review.HasIndex(r => r.CreatedBy);

            // The optional due date: a calendar day (UTC), stored as text by the SQLite provider; null when none is set
            review.Property(r => r.DueDate);

            // The review conclusion text: the free text of the record (not the approved or rejected conclusion); plain
            // text, null when there is none, so rows from before it read back as no text
            review.Property(r => r.ReviewConclusionText).HasMaxLength(ReviewLimits.MaxConclusionTextLength);

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

            // The review a follow-up follows: a plain value (no foreign key; the source is in the same project by rule),
            // indexed so the reviews that follow a review can be found. The carried rows travel with the review only
            // when it is created, so the property that holds them is not mapped
            review.Property(r => r.FollowsReviewId).HasMaxLength(ReviewLimits.MaxReviewIdLength);
            review.HasIndex(r => new { r.ProjectId, r.FollowsReviewId });
            review.Ignore(r => r.CarryOver);
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

            // A carried thread names the review and the thread it was copied from (plain values, no foreign keys)
            thread.Property(t => t.CarriedFromReviewId).HasMaxLength(ReviewLimits.MaxReviewIdLength);

            // The kind is derived from the flag of the starting comment, never stored
            thread.Ignore(t => t.Kind);
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
            user.Property(u => u.LastSignInUtc);
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

        // Second factor: one enrollment per user (the user id is the key, so a second row cannot exist), kept apart
        // from the user row because the user store rewrites every column of a user and a stale copy must never move
        // the last accepted step backwards. The secret is stored encrypted. Recovery codes are stored as hashes; the
        // unique index per user makes a duplicate hash impossible. Both go away with the user
        modelBuilder.Entity<UserTotp>(totp =>
        {
            totp.HasKey(t => t.UserId);
            totp.Property(t => t.UserId).ValueGeneratedNever();
            totp.Property(t => t.ProtectedSecret).IsRequired().HasMaxLength(EncryptedTokenColumnLength);
            totp.Ignore(t => t.IsActive);
            totp.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TotpRecoveryCode>(code =>
        {
            code.HasKey(c => c.Id);
            code.Property(c => c.CodeHash).IsRequired().HasMaxLength(Sha256HexColumnLength);
            code.HasIndex(c => new { c.UserId, c.CodeHash }).IsUnique();
            code.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
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

            // The report flag and the kind (stored by name) default to a plain chat comment
            comment.Property(c => c.IncludeInReport).HasDefaultValue(false);
            comment.Property(c => c.Kind)
                .HasConversion<string>()
                .HasMaxLength(StateColumnLength)
                .HasDefaultValue(ReviewCommentKind.Comment);
        });

        // Checklists: one frozen snapshot per review, and the answers and section applicability keyed by the
        // reviewer slot (0 for the review-level baseline, which has no slot and so no foreign key to one) and the
        // check or section identifier within it; all go away with the review, and the rows of a removed slot are
        // deleted by the store
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
            section.HasKey(s => new { s.ProjectId, s.ReviewId, s.SlotId, s.SectionId });
            section.Property(s => s.ReviewId).HasMaxLength(ReviewLimits.MaxReviewIdLength);
            section.Property(s => s.SectionId).HasMaxLength(ChecklistIdColumnLength);
            section.Property(s => s.Reason).HasMaxLength(ReviewLimits.MaxJustificationLength);
            section.Property(s => s.SetBy).IsRequired().HasMaxLength(ReviewLimits.MaxAuthorLength);
            section.Property(s => s.SetByUsername).IsRequired().HasMaxLength(ReviewLimits.MaxUsernameLength);
            section.Property(s => s.CarriedFromReviewId).HasMaxLength(ReviewLimits.MaxReviewIdLength);
            section.HasOne<Review>()
                .WithMany()
                .HasForeignKey(s => new { s.ProjectId, s.ReviewId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChecklistAnswer>(answer =>
        {
            answer.HasKey(a => new { a.ProjectId, a.ReviewId, a.SlotId, a.ItemId });
            answer.Property(a => a.ReviewId).HasMaxLength(ReviewLimits.MaxReviewIdLength);
            answer.Property(a => a.ItemId).HasMaxLength(ChecklistIdColumnLength);
            answer.Property(a => a.Outcome).HasConversion<string>().HasMaxLength(StateColumnLength);
            answer.Property(a => a.Justification).HasMaxLength(ReviewLimits.MaxJustificationLength);
            answer.Property(a => a.AnsweredBy).IsRequired().HasMaxLength(ReviewLimits.MaxAuthorLength);
            answer.Property(a => a.AnsweredByUsername).IsRequired().HasMaxLength(ReviewLimits.MaxUsernameLength);
            answer.Property(a => a.CarriedFromReviewId).HasMaxLength(ReviewLimits.MaxReviewIdLength);
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

            // The inbox finds the slots of one user (as primary or as alternate), so both columns are indexed
            slot.HasIndex(s => s.PrimaryUserId);
            slot.HasIndex(s => s.AlternateUserId);
            slot.HasOne<Review>()
                .WithMany()
                .HasForeignKey(s => new { s.ProjectId, s.ReviewId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Ballot status: one row per reviewer slot that started a ballot (no row means not started). It goes away with
        // the review or with the slot; a Submitted row carries who handed it in and when, a Draft row carries neither
        modelBuilder.Entity<ChecklistBallot>(ballot =>
        {
            ballot.HasKey(b => new { b.ProjectId, b.ReviewId, b.SlotId });
            ballot.Property(b => b.ReviewId).HasMaxLength(ReviewLimits.MaxReviewIdLength);
            ballot.Property(b => b.Status).HasConversion<string>().HasMaxLength(StateColumnLength);
            ballot.Property(b => b.SubmittedByUsername).HasMaxLength(ReviewLimits.MaxUsernameLength);
            ballot.HasIndex(b => b.SlotId);
            ballot.HasOne<Review>()
                .WithMany()
                .HasForeignKey(b => new { b.ProjectId, b.ReviewId })
                .OnDelete(DeleteBehavior.Cascade);
            ballot.HasOne<ReviewerSlot>()
                .WithMany()
                .HasForeignKey(b => b.SlotId)
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
            // The outcome is stored as its number, which ReviewOutcome promises never to renumber
            signature.Property(s => s.Outcome).IsRequired().HasConversion<int>();
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

        // Review reopens: append-only history of the times an administrator reopened an approved review. The
        // identity is assigned by the database; a reopen goes away with its review
        modelBuilder.Entity<ReviewReopen>(reopen =>
        {
            reopen.HasKey(r => r.Id);
            reopen.Property(r => r.ReviewId).HasMaxLength(ReviewLimits.MaxReviewIdLength);
            reopen.Property(r => r.ReopenedBy).IsRequired().HasMaxLength(ReviewLimits.MaxUsernameLength);
            reopen.Property(r => r.ReopenedByDisplayName).IsRequired().HasMaxLength(ReviewLimits.MaxDisplayNameLength);
            reopen.Property(r => r.Reason).IsRequired().HasMaxLength(ReviewLimits.MaxReopenReasonLength);
            reopen.Property(r => r.PreviousDigest).IsRequired().HasMaxLength(Sha256HexColumnLength);
            reopen.HasIndex(r => new { r.ProjectId, r.ReviewId });
            reopen.HasOne<Review>()
                .WithMany()
                .HasForeignKey(r => new { r.ProjectId, r.ReviewId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Review user states: what a user has already seen of a review (the inbox and the unread markers). One row
        // per user and review, so the composite key doubles as the guarantee that two parallel writes cannot store
        // two rows. The row goes away with the user and with the review. It is not audited and not part of the
        // signed record
        modelBuilder.Entity<ReviewUserState>(state =>
        {
            state.HasKey(s => new { s.UserId, s.ProjectId, s.ReviewId });
            state.Property(s => s.ReviewId).HasMaxLength(ReviewLimits.MaxReviewIdLength);
            state.HasOne<User>()
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            state.HasOne<Review>()
                .WithMany()
                .HasForeignKey(s => new { s.ProjectId, s.ReviewId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Audit trail: append-only (nothing in the application updates or deletes a row). The sequence is the key,
        // assigned by the appender under the process-wide gate (never by the database), so a duplicate sequence
        // is refused by the primary key. There are no foreign keys: an entry is a snapshot of text and must
        // survive whatever happens to the rows it describes. The indexes serve the history of one review and the
        // filters of the audit pages (project, actor, action; each ends with the sequence, the paging order).
        // The timestamp is stored as fixed-width UTC text (seven fraction digits, "Z"): the SQLite provider cannot
        // compare an unconverted DateTimeOffset, and this text sorts and compares as the instant does. The chain
        // hash is computed from the ticks in code, so the stored form does not change a hash
        modelBuilder.Entity<AuditEntry>(entry =>
        {
            entry.HasKey(a => a.Sequence);
            entry.Property(a => a.Sequence).ValueGeneratedNever();
            entry.Property(a => a.TimestampUtc).HasConversion(
                value => value.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture),
                text => DateTimeOffset.Parse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal));
            entry.Property(a => a.ActorName).IsRequired().HasMaxLength(AuditText.MaxActorNameLength);
            entry.Property(a => a.Action).IsRequired().HasMaxLength(AuditText.MaxActionLength);
            entry.Property(a => a.ReviewId).HasMaxLength(ReviewLimits.MaxReviewIdLength);
            entry.Property(a => a.Detail).IsRequired().HasMaxLength(AuditText.MaxDetailLength);
            entry.Property(a => a.PreviousHash).IsRequired().HasMaxLength(Sha256HexColumnLength);
            entry.Property(a => a.Hash).IsRequired().HasMaxLength(Sha256HexColumnLength);
            entry.HasIndex(a => new { a.ProjectId, a.ReviewId, a.Sequence });
            entry.HasIndex(a => new { a.ProjectId, a.Sequence });
            entry.HasIndex(a => new { a.ActorUserId, a.Sequence });
            entry.HasIndex(a => new { a.Action, a.Sequence });
        });
    }
}
