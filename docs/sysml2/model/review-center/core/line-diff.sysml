package ReviewCenter {
    part def LineDiff {
        doc /* Compares the lines of a baseline and a head file with a shortest-edit-script algorithm, numbers the rows on both sides like the lines that comment anchors count, refuses inputs above a line and an edit bound, and divides the rows into changed regions with context and collapsible gaps. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/LineDiff.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/LineDiffTests.cs */
        comment designRef /* Design: docs/design/review-center/core/line-diff.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/line-diff.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/line-diff.yaml */
    }
}
