package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def GitSource {
        doc /* Git implementation of the repository provider: keeps one bare mirror cache per repository with access serialized per repository, resolves revisions to commits, answers whether a commit is an ancestor of another, lists and reads files of a commit without a working tree, skips symbolic links and submodules, and enforces size limits. */

        metadata info : SoftwareInfo {
            description = "Git implementation of the repository provider: keeps one bare mirror cache per repository with access serialized per repository, resolves revisions to commits, answers whether a commit is an ancestor of another, lists and reads files of a commit without a working tree, skips symbolic links and submodules, and enforces size limits.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter/ContentSources/GitSource.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/ContentSources/GitSourceTests.cs");
            designFiles = ("docs/design/review-center/content-sources/git-source.md");
            verificationFiles = ("docs/verification/review-center/content-sources/git-source.md");
            requirementFiles = ("docs/reqstream/review-center/content-sources/git-source.yaml");
        }
    }
}
