package ReviewCenter {
    part def ReviewPage {
        doc /* Page that shows one review with the reader's work first: a next-step line, a checklist progress summary with a link to the checklist, the files, a thread index across all files (filtered by state; choosing a row selects the file and scrolls to and highlights the anchor), and the numbered lines of a selected file with its comment threads, which readers can start, answer, resolve, and reopen (resolved threads collapse to one line). The review's identifier, state, fingerprint, and file hashes are in a collapsed Details section. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Web/ReviewPage.razor and ReviewPage.razor.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Web/ReviewPageTests.cs */
        comment designRef /* Design: docs/design/review-center/web/review-page.md */
        comment verificationRef /* Verification: docs/verification/review-center/web/review-page.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/web/review-page.yaml */
    }
}
