namespace DemaConsulting.ReviewCenter.Tests;

/// <summary>
///     Test helper that forces a full, blocking garbage collection. A few tests verify
///     behavior that is only observable after the collector has run (weak references being
///     cleared and finalizers raising unobserved exceptions), so they need a deterministic collection.
/// </summary>
internal static class ForcedGarbageCollection
{
    /// <summary>
    ///     Runs a blocking collection and waits for all pending finalizers to complete.
    /// </summary>
    public static void CollectAndFinalize()
    {
        // The collection is the behavior under test, not an accidental performance hazard.
        // codeql[cs/call-to-gc] Deliberate: tests observe weak references and finalizers.
#pragma warning disable S1215 // A deterministic collection is the only way to observe finalizer and weak-reference behavior
        GC.Collect();
#pragma warning restore S1215
        GC.WaitForPendingFinalizers();
    }
}
