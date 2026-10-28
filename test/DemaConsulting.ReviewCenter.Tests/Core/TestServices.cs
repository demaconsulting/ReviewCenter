using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Builds the Core services over a review store for tests whose subject is not access control: every actor is
///     allowed everything (see <see cref="AllowAllProjectAccess"/>), but projects and memberships are real data of
///     the store.
/// </summary>
internal static class TestServices
{
    /// <summary>
    ///     Builds a project service.
    /// </summary>
    /// <param name="store">The review store.</param>
    /// <param name="clock">The clock.</param>
    /// <returns>The service.</returns>
    public static ProjectService Projects(IReviewStore store, TimeProvider clock)
    {
        return new ProjectService(store, new FakeUserStore(), new AllowAllProjectAccess(store), clock);
    }

    /// <summary>
    ///     Builds a review service.
    /// </summary>
    /// <param name="store">The review store.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="repositories">The repository service, for reviews from a repository.</param>
    /// <param name="git">The git provider, for reviews from a repository.</param>
    /// <returns>The service.</returns>
    public static ReviewService Reviews(
        IReviewStore store,
        TimeProvider clock,
        RepositoryService? repositories = null,
        IGitRepositoryProvider? git = null)
    {
        return new ReviewService(store, new AllowAllProjectAccess(store), clock, repositories, git);
    }

    /// <summary>
    ///     Builds a thread service.
    /// </summary>
    /// <param name="store">The review store.</param>
    /// <param name="clock">The clock.</param>
    /// <returns>The service.</returns>
    public static ThreadService Threads(IReviewStore store, TimeProvider clock)
    {
        return new ThreadService(store, new AllowAllProjectAccess(store), clock);
    }

    /// <summary>
    ///     Builds a checklist service.
    /// </summary>
    /// <param name="store">The review store.</param>
    /// <param name="clock">The clock.</param>
    /// <returns>The service.</returns>
    public static ChecklistService Checklists(IReviewStore store, TimeProvider clock)
    {
        return new ChecklistService(store, new AllowAllProjectAccess(store), clock);
    }

    /// <summary>
    ///     Builds a signing service.
    /// </summary>
    /// <param name="store">The review store.</param>
    /// <param name="users">The user service.</param>
    /// <param name="clock">The clock.</param>
    /// <returns>The service.</returns>
    public static SigningService Signing(IReviewStore store, UserService users, TimeProvider clock)
    {
        return new SigningService(store, users, new AllowAllProjectAccess(store), clock);
    }

    /// <summary>
    ///     Builds a repository service over a fake repository store.
    /// </summary>
    /// <param name="reviewStore">The review store that knows the projects.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="repositoryStore">The repository store; a new one when <c>null</c>.</param>
    /// <param name="protector">The credential protector; a new fake when <c>null</c>.</param>
    /// <returns>The service.</returns>
    public static RepositoryService Repositories(
        IReviewStore reviewStore,
        TimeProvider clock,
        IRepositoryStore? repositoryStore = null,
        ICredentialProtector? protector = null)
    {
        return new RepositoryService(
            repositoryStore ?? new FakeRepositoryStore(),
            protector ?? new FakeCredentialProtector(),
            new AllowAllProjectAccess(reviewStore),
            clock);
    }

    /// <summary>
    ///     Builds a review planner.
    /// </summary>
    /// <param name="store">The review store.</param>
    /// <param name="reviews">The review service.</param>
    /// <param name="repositories">The repository service.</param>
    /// <param name="git">The git provider.</param>
    /// <param name="signing">The signing service.</param>
    /// <param name="parser">The review-set parser.</param>
    /// <returns>The planner.</returns>
    public static ReviewPlanner Planner(
        IReviewStore store,
        ReviewService reviews,
        RepositoryService repositories,
        IGitRepositoryProvider git,
        SigningService signing,
        IReviewSetParser parser)
    {
        return new ReviewPlanner(store, reviews, repositories, git, signing, parser, new AllowAllProjectAccess(store));
    }
}
