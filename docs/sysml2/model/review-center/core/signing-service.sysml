package ReviewCenter {
    part def SigningService {
        doc /* Adds, changes, and removes the reviewer slots of a review (only for a member or system administrator who can see the project; refused for an actor that came through a personal access token), records a signature after the signer, who must be a member of the project, has entered the password again (a failed re-authentication is audited; an actor that came through a token can never sign), and computes the approval status of a review from the record and its signatures. Offers the enabled members of a project as slot candidates, and assigns slots only to members. Also defines the slot, signature, role, approval, and status types of the Core subsystem. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/SigningService.cs, ReviewerSlot.cs, ReviewSignature.cs, SlotRole.cs, and ReviewApproval.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/SigningServiceTests.cs */
        comment designRef /* Design: docs/design/review-center/core/signing-service.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/signing-service.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/signing-service.yaml */
    }
}
