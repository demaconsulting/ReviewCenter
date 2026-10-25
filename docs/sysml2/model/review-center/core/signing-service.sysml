package ReviewCenter {
    part def SigningService {
        doc /* Adds, changes, and removes the reviewer slots of a review, records a signature after the signer has entered the password again, and computes the approval status of a review from the record and its signatures. Also defines the slot, signature, role, approval, and status types of the Core subsystem. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/SigningService.cs, ReviewerSlot.cs, ReviewSignature.cs, SlotRole.cs, and ReviewApproval.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/SigningServiceTests.cs */
        comment designRef /* Design: docs/design/review-center/core/signing-service.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/signing-service.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/signing-service.yaml */
    }
}
