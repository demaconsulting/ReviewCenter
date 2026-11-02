package ReviewCenter {
    part def InboxService {
        doc /* The inbox of a signed-in user, which is also the list of what is new: the reviews that need the user (a ballot to hand in, checks the reviewers answered differently that the user, who has handed in, may see, a signature to give or to give again once the ballot is in and nothing blocks the signature, a thread that waits for a reply, failed checks of the consolidated checklist of the review the user authored), the reviews that only wait for other people, and the updates (a review approved or reopened by somebody else since the user last looked), the count of the distinct reviews that need action, the comments of others and the answers of the submitted ballots of other slots that are unread (a draft of another slot is never news), and the read state that only ever moves forward. A user who has not handed in the own ballot is never told what the others answered or that something is disputed, and the section of a reviewer agrees with the next action of the status panel of the review. Nothing is shown to a user who may not act, the read state is neither part of the signed record nor of the audit trail, and the rules need only the stores and the clock. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/InboxService.cs, InboxRules.cs, InboxItem.cs, IInboxStore.cs and ReviewUserState.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/InboxServiceTests.cs and InboxRulesTests.cs */
        comment designRef /* Design: docs/design/review-center/core/inbox-service.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/inbox-service.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/inbox-service.yaml */
    }
}
