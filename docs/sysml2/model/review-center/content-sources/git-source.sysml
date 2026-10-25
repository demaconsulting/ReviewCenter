package ReviewCenter {
    part def GitSource {
        doc /* Git implementation of the repository provider: keeps one bare mirror cache per repository with access serialized per repository, resolves revisions to commits, lists and reads files of a commit without a working tree, skips symbolic links and submodules, and enforces size limits. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/ContentSources/GitSource.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/ContentSources/GitSourceTests.cs */
        comment designRef /* Design: docs/design/review-center/content-sources/git-source.md */
        comment verificationRef /* Verification: docs/verification/review-center/content-sources/git-source.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/content-sources/git-source.yaml */
    }
}
