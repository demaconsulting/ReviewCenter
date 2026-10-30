### ProjectPage

#### Verification Approach

`ProjectPage`, `NewReviewPage`, `NewMemberPage`, `NewMemberAccountPage`, and `EvidenceSettingsPage` are verified with
component tests defined
in `ProjectPageTests.cs`, which holds the tests of the first two pages and the member changes of the project page,
`NewReviewPageRepositoryTests.cs`, `ProjectAccessPageTests.cs` (what a member, a non-member,
and a system administrator see and do), `NewMemberPageTests.cs`, `NewMemberAccountPageTests.cs`, and
`EvidenceSettingsPageTests.cs`, which use the
bUnit test context
to render the real components over real Core services and an in-memory fake store (`FakeReviewStore`, `FakeUserStore`
connected to it, and `FakeAuditStore`, with `FakePasswordHasher` for the passwords of the created accounts). The
evidence settings page runs over the real `EvidenceSettingsService` with the real access rules, so who gets the page and
what a save does are the product's; a second service that allows everything (`PageTestBase.SettingsService`) only
arranges stored settings. The audit entry of a save is read back from the fake store. File
selection is simulated the
way a browser does it, by setting the files of the file input in the test context. Tests locate elements by their
`data-testid` attributes (the page units share the helper `PageTestBase`) and wait for the asynchronous file
reading with bUnit's wait helpers. The real database is covered by the `Storage` subsystem and the integration
tests of the `Web` subsystem.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All component tests pass with zero failures.
- An unknown project shows a not-found message and no form, and so does a project the user is no member of (the same
  message, `project-not-found`, and nothing of the project, on the project page and on the page for a new review); a
  known project shows its name and description and
  its reviews as rows with link, title, and an Approved or In review badge, or an empty message. A review
  whose every slot has a current signature is Approved; a review without slots or with an unsigned slot is In review.
- A member who is no administrator sees the reviews and the New review button, but no Members section, no add member
  button, no Audit trail button, and no Repositories button.
- A system administrator or a project admin sees the Members section with each member, the role badge, a button that
  switches the role, and a Remove button, and says so when the project has no members, the + Add member button, which is
  a link to the page for a new member, and the Audit trail button; no list of users is on the page. The role button and
  Remove change the membership. The Repositories button is shown to whoever manages the project.
- The last project admin cannot remove or demote the own membership: the explanation of the refusal is shown next to
  the list, and the membership is unchanged; with a second project admin the first may remove the own membership.
- A system administrator or a project admin who lost the right after the page was opened changes nothing when acting,
  and the members and the administration buttons are dropped, while the reviews stay for a user who still is a member;
  a user who also has no membership is shown the not-found message and none of the project.
- The page for a new member shows whoever administers the project a form with a user name field, the project admin
  choice, a Cancel button, and a Create a new account button, and no list of users; the exact name of an existing user
  is added with the chosen role and the page returns to the project; a missing, a disabled, and an unknown name give the
  same message and add nothing, and keep what was typed; a user who is a member already gets a message and keeps the
  role; anybody else, and a user who lost the right while the page was open, gets no form and the not-found message, and
  nothing is added.
- The page for a new account shows the same people a form with a user name, a display name, and the project admin
  choice; the account is stored with its membership and the must-change-password flag, the temporary password is shown
  once with the user name, Copy reports whether the copy worked, and Done hides the password for good and returns to
  the project; a taken or an invalid name shows a message and creates nothing; anybody else, and a user who lost the
  right while the page was open (also while the password is shown), gets no form, no password, and the not-found
  message, and nothing is created. Loading either page for another project drops the typed names and the password.
- Selected files are listed with names and sizes, and a new selection replaces the previous one; reusing the form
  page for another project shows that project and discards the previous form values and selection.
- A valid form creates the review with the exact bytes and the correct fingerprint and opens the review page.
- Rule violations (no files, duplicate review identifier, duplicate file names, the reserved identifier `new`, an
  ended sign-in state) and a selection over a limit (too many files, a file over the size limit, or a total over the
  size limit) are shown as errors that keep the entered values and create nothing.
- The interactive marker of the project page is `true` after the page has rendered on a live connection.
- The form offers "Upload files" by default and "From a repository", which shows the repository fields instead of
  the file input with the default selection `**/*`; the repository choice lists only enabled repositories of the project
  by name
  (never a location, user name, or token) and is disabled with a hint when none is enabled.
- A valid repository form creates the review with its source and opens it; without a baseline every file at the head
  is reviewed; a selection with no changed file shows the nothing-to-review message, a revision that does not exist
  shows the git error, no chosen repository asks for one, and a repository disabled in the meantime is refused; each
  creates nothing and keeps the entered values.
- Skipped links and submodules are shown to the creator before the review is opened.
- The project page links to the plan page of the project when a repository is enabled, and otherwise says whom to
  ask.
- The project page shows the Evidence settings button, which leads to `/projects/{id}/evidence-settings`, to a project
  admin and a system administrator and to nobody else.
- The evidence settings page shows whoever administers the project the seven switches with a description each, from the
  stored settings or, when none are stored, the defaults (the two anonymous switches off, the five content switches on),
  no warning, no summary, a Save settings button, and a Back to project button that leads to the project; a plain
  member, a user who is no member, and a missing project get the not-found message and no switches.
- Turning a content switch off shows its own warning, which mentions the regulatory regime and the quality or
  compliance lead, and a summary that names the omitted section, before anything is saved; turning it on again removes
  both. The warnings of the five switches differ. While an anonymous switch is on, an exposure warning is shown, and a
  stronger sentence names the review discussion and the reviewer names that anonymous PDFs would still carry, as far as
  those switches are on; with nothing anonymous the warning is gone.
- Saving stores all the switches, confirms with "Evidence settings saved.", stays on the page, and records one audit
  entry of the project, `project.evidence-settings-changed`, with the changed keys, their old and new values, and the
  user; saving unchanged settings says that there is nothing to save and records nothing.
- A save over settings that someone else changed in the meantime stores nothing, shows the message that they were changed
  by someone else, and shows the stored settings.
- A save after the membership was revoked, after a demotion to plain member, or after the sign-in state ended stores
  nothing, records nothing, and shows the not-found message and none of the settings or messages; reusing the page for
  another project drops the choices and the project.
- The interactive marker of the evidence settings page is `true` after it has rendered on a live connection.
- The project page shows a project admin and a system administrator a "Review rules" card with the switch "Require
  independent reviewers", on by default; saving a change stores it, confirms it, and records one audit entry
  `project.independent-reviewers-changed`; a plain member sees no card.

#### Test Scenarios

**Reviews from a repository** are verified in `NewReviewPageRepositoryTests.cs`, with the fake git provider of the
`Web` tests (`FakeGitRepositoryProvider`) behind the real `RepositoryService` and `ReviewService`.

**NewReviewPage_SourceChoice_RepositoryChosen_ShowsRepositoryFieldsInsteadOfUpload**: The upload source is shown first;
choosing the repository source shows the repository, head, baseline, and selection fields (the selection starts as
`**/*`) and hides the file input. This scenario is tested by
`NewReviewPage_SourceChoice_RepositoryChosen_ShowsRepositoryFieldsInsteadOfUpload`.

**NewReviewPage_RepositorySelect_OffersOnlyEnabledRepositoriesWithoutLocationOrToken**: An enabled and a disabled
repository exist; only the enabled one is offered, by name, and the page contains neither location, user name, nor
token. This scenario is tested by
`NewReviewPage_RepositorySelect_OffersOnlyEnabledRepositoriesWithoutLocationOrToken`.

**NewReviewPage_RepositorySelect_OffersOnlyRepositoriesOfThisProject**: A repository exists in each of two projects; the
form of the first project, with the repository source chosen, offers the choice prompt and the repository of that
project only, and the markup does not contain the repository of the other. This scenario is tested by
`NewReviewPage_RepositorySelect_OffersOnlyRepositoriesOfThisProject`.

**NewReviewPage_SourceChoice_NoEnabledRepository_DisablesRepositorySourceWithHint**: With no enabled repository the
repository choice is disabled and a hint says why. This scenario is tested by
`NewReviewPage_SourceChoice_NoEnabledRepository_DisablesRepositorySourceWithHint`.

**NewReviewPage_CreateForm_FromRepository_CreatesReviewWithSourceAndNavigates**: A valid repository form creates the
review with its repository, commits, selection, and changes, and navigates to it. This scenario is tested by
`NewReviewPage_CreateForm_FromRepository_CreatesReviewWithSourceAndNavigates`, and without a baseline by
`NewReviewPage_CreateForm_FromRepositoryWithoutBaseline_ReviewsAllFilesAtHead`.

**NewReviewPage_CreateForm_EmptySelection_ShowsNothingToReviewAndCreatesNothing**: A selection with no changed file
shows "Nothing to review: no files matching the selection changed between X and Y" and creates nothing. This
scenario is tested by `NewReviewPage_CreateForm_EmptySelection_ShowsNothingToReviewAndCreatesNothing`.

**NewReviewPage_CreateForm_BadRevision_ShowsErrorAndCreatesNothing**: A revision that does not exist shows the error
and creates nothing. This scenario is tested by `NewReviewPage_CreateForm_BadRevision_ShowsErrorAndCreatesNothing`.

**NewReviewPage_CreateForm_NoRepositoryChosen_AsksForOne**: Submitting without a repository shows "Choose a
repository." and creates nothing. This scenario is tested by `NewReviewPage_CreateForm_NoRepositoryChosen_AsksForOne`.

**NewReviewPage_CreateForm_RepositoryDisabledMeanwhile_ShowsErrorAndCreatesNothing**: A repository disabled after the
page opened is refused on submit and nothing is created. This scenario is tested by
`NewReviewPage_CreateForm_RepositoryDisabledMeanwhile_ShowsErrorAndCreatesNothing`.

**NewReviewPage_CreateForm_SkippedEntries_AreShownBeforeOpeningTheReview**: Skipped entries are listed with a link to
open the created review, and the page does not navigate until the link is followed. This scenario is tested by
`NewReviewPage_CreateForm_SkippedEntries_AreShownBeforeOpeningTheReview`.

**ProjectPage_Render_PlanLink_ShownOnlyWithEnabledRepository**: A project without any repository shows the note and no
link; after a repository is registered, the link goes to `/projects/{id}/plan` and the note is gone. This scenario is
tested by `ProjectPage_Render_PlanLink_ShownOnlyWithEnabledRepository`.

**ProjectPage_Render_UnknownProject_ShowsNotFound**: The project page is rendered for an unknown project; the
not-found message names the identity and no form is shown. This scenario is tested by
`ProjectPage_Render_UnknownProject_ShowsNotFound`.

**ProjectPage_Render_NonMember_LooksLikeAMissingProject**: A project with a review exists, and a user who is no member
and no system administrator is signed in. The project page shows the same not-found message as for a missing project
and neither the name of the project nor its review, and the page for a new review shows the not-found message and no
form. This scenario is tested by `ProjectPage_Render_NonMember_LooksLikeAMissingProject`.

**ProjectPage_Render_Member_SeesReviewsButNoMembersSection**: A member who is no administrator opens the page of the
project; the review is listed and the New review button is shown, but there is no members list, no add member button,
and no Repositories button. This scenario is tested by `ProjectPage_Render_Member_SeesReviewsButNoMembersSection`.

**ProjectPage_Render_SystemAdmin_ListsMembersWithRolesAndActions**: A project has a member and a project administrator,
and another has no members. For the system administrator the first lists "Member" and "Project admin" badges, the
buttons "Make project admin" and "Make member", and a Remove button, and has the Repositories button; the second says
that it has no members. This scenario is tested by `ProjectPage_Render_SystemAdmin_ListsMembersWithRolesAndActions`.

**ProjectPage_AddMember_ButtonLeadsToNewMemberPage_WhichAddsTheNamedUser**: The system administrator opens the page of
a project; the + Add member button is a link to the page for a new member, and the project page has no add form and
no list of users. The page for a new member adds the user typed with the exact name as a project administrator, and
the membership is stored with that role. This scenario is tested by
`ProjectPage_AddMember_ButtonLeadsToNewMemberPage_WhichAddsTheNamedUser`.

**ProjectPage_Render_ProjectAdmin_ShowsMembersAndButtons**: A project administrator opens the page of the project; the
members are listed with the role and Remove buttons, the add member button and the Audit trail button are shown, and
no other user is listed. This scenario is tested by `ProjectPage_Render_ProjectAdmin_ShowsMembersAndButtons`.

**ProjectPage_Render_PlainMember_ShowsNoMembersAndNoAuditLink**: A plain member sees the reviews, but no members
section, no add button, and no Audit trail button. This scenario is tested by
`ProjectPage_Render_PlainMember_ShowsNoMembersAndNoAuditLink`.

**ProjectPage_MemberActions_ToggleRoleAndRemove_ChangeTheMembership**: The role button of a member is pressed; the
stored role becomes project administrator and the button reads "Make member". Remove is then pressed; the membership is
gone, the row is gone, and the empty message is shown. This scenario is tested by
`ProjectPage_MemberActions_ToggleRoleAndRemove_ChangeTheMembership`.

**ProjectPage_MemberChange_ProjectAdmin_ChangesRoleAndRemoves**: A project administrator makes a member a project admin
and removes a member; both changes are stored. This scenario is tested by
`ProjectPage_MemberChange_ProjectAdmin_ChangesRoleAndRemoves`.

**ProjectPage_MemberChange_LastProjectAdminSelf_ShowsExplanationAndChangesNothing**: The only project administrator
presses Remove on the own row and, in a second run, the role button of the own row. The explanation of the last
project admin rule is shown as the error of the section, the row still reads "Project admin", and the stored role is
unchanged. This scenario is tested by
`ProjectPage_MemberChange_LastProjectAdminSelf_ShowsExplanationAndChangesNothing`.

**ProjectPage_MemberChange_SecondAdminExists_AdminMayRemoveSelf**: With a second project administrator, the first one
removes the own membership and the membership is gone. This scenario is tested by
`ProjectPage_MemberChange_SecondAdminExists_AdminMayRemoveSelf`.

**ProjectPage_MemberChange_ProjectAdminMembershipRevoked_ShowsNotFoundAndChangesNothing**: A project administrator has
the page open and another administrator removes the membership; pressing Remove or the role button of a member shows
only the not-found message, with no members, reviews, or member names on the page, and the role of the member is
unchanged. This scenario is tested by
`ProjectPage_MemberChange_ProjectAdminMembershipRevoked_ShowsNotFoundAndChangesNothing`.

**ProjectPage_MemberChange_ProjectAdminDemoted_DropsMembersKeepsReviews**: A project administrator is demoted to a
plain member while the page is open; the next member action changes nothing, and the members section and the Audit
trail button are gone while the reviews are still listed. This scenario is tested by
`ProjectPage_MemberChange_ProjectAdminDemoted_DropsMembersKeepsReviews`.

**ProjectPage_MemberChange_SystemAdminDemotedButStillMember_DropsMembersSectionKeepsReviews**: A system administrator
who also is a plain member of the project has the page open with the Members section, and a second administrator
exists; the first loses the flag and presses Remove on a member. Nothing is removed, no error is shown, the Members
section and the member rows are gone, and the reviews are still listed. This scenario is tested by
`ProjectPage_MemberChange_SystemAdminDemotedButStillMember_DropsMembersSectionKeepsReviews`.

**ProjectPage_MemberChange_SystemAdminDemotedAndNotMember_ShowsNotFoundAndNoData**: The same arrangement for an
administrator who is no member of the project. After the demotion, Remove shows only the not-found message, with no
member, review, or project name on the page, and the membership of the member is unchanged. This scenario is tested by
`ProjectPage_MemberChange_SystemAdminDemotedAndNotMember_ShowsNotFoundAndNoData`.

**ProjectPage_ToggleRole_SystemAdminDemoted_ChangesNothingAndDropsMembers**: A system administrator who is a plain
member is demoted while the page is open, and the role button of a member is pressed. The role is not changed and the
members section is dropped. This scenario is tested by
`ProjectPage_ToggleRole_SystemAdminDemoted_ChangesNothingAndDropsMembers`.

**ProjectPage_MemberChange_SessionEndedBeforePageKnows_ShowsNotFoundAndChangesNothing**: The sign-in state ends without
the page being told, and Remove is pressed. The page shows the not-found message and no members, and the membership is
unchanged. This scenario is tested by
`ProjectPage_MemberChange_SessionEndedBeforePageKnows_ShowsNotFoundAndChangesNothing`.

**NewMemberPage_Render_Administrator_ShowsFormWithoutUserList**: The page for a new member is rendered for a project
administrator; the user name field, the project admin choice, the Cancel button, and the Create a new account button
are shown, and there is no list of users. This scenario is tested by
`NewMemberPage_Render_Administrator_ShowsFormWithoutUserList`.

**NewMemberPage_Render_NoAdministerRight_ShowsNotFoundAndNoForm**: A plain member, a user who is no member, and a
missing project each give the not-found message and no form. This scenario is tested by
`NewMemberPage_Render_NoAdministerRight_ShowsNotFoundAndNoForm`.

**NewMemberPage_Submit_ExistingUser_AddsWithRoleAndReturnsToTheProject**: The exact name of an existing user is
submitted with the project admin choice; the membership is stored with that role and the page navigates to the
project. This scenario is tested by `NewMemberPage_Submit_ExistingUser_AddsWithRoleAndReturnsToTheProject`.

**NewMemberPage_Submit_UnusableNames_GiveOneGenericMessageAndAddNothing**: A name that does not exist and the name of a
disabled account are submitted; the message is the same each time and says that the name cannot be added, the form
keeps what was typed, and no membership is added. This scenario is tested by
`NewMemberPage_Submit_UnusableNames_GiveOneGenericMessageAndAddNothing`.

**NewMemberPage_Submit_AlreadyMember_ShowsMessageAndKeepsTheRole**: A user who already is a member is submitted; a
message is shown and the role of the membership is unchanged. This scenario is tested by
`NewMemberPage_Submit_AlreadyMember_ShowsMessageAndKeepsTheRole`.

**NewMemberPage_Submit_RightLostWhileOpen_AddsNothingAndDropsTheForm**: The membership is revoked, the user is demoted
to plain member, or the session ends without the page knowing, and the form is submitted; nothing is added and the page
shows the not-found message with no form, no error, and nothing typed. This scenario is tested by
`NewMemberPage_Submit_RightLostWhileOpen_AddsNothingAndDropsTheForm`.

**NewMemberPage_Load_OtherProject_DropsWhatWasTypedAndTheProject**: The page is loaded again for another project the
user may not administer; the typed name and the first project do not follow. This scenario is tested by
`NewMemberPage_Load_OtherProject_DropsWhatWasTypedAndTheProject`.

**NewMemberPage_AfterRender_Interactive_SetsMarker**: The page for a new member is rendered; its root element has
`data-interactive` equal to `true`. This scenario is tested by `NewMemberPage_AfterRender_Interactive_SetsMarker`.

**NewMemberAccountPage_Render_Administrator_ShowsTheForm**: The page for a new account is rendered for whoever
administers the project; the form and the button back to the page for a new member are shown. This scenario is tested
by `NewMemberAccountPage_Render_Administrator_ShowsTheForm`.

**NewMemberAccountPage_Render_NoAdministerRight_ShowsNotFoundAndNoForm**: A plain member, a user who is no member, and a
missing project each give the not-found message and no form. This scenario is tested by
`NewMemberAccountPage_Render_NoAdministerRight_ShowsNotFoundAndNoForm`.

**NewMemberAccountPage_Submit_ValidNames_CreatesAccountShowsPasswordOnceAndDoneHidesIt**: The form is submitted with
valid names; the account is stored with its membership and the must-change-password flag, the temporary password is
shown once with the user name, and Done hides it for good and returns to the project. This scenario is tested by
`NewMemberAccountPage_Submit_ValidNames_CreatesAccountShowsPasswordOnceAndDoneHidesIt`.

**NewMemberAccountPage_Copy_Password_CopiesAndReportsTheResult**: Copy hands the password to the browser and says so;
when the browser refuses, the page says how to go on. This scenario is tested by
`NewMemberAccountPage_Copy_Password_CopiesAndReportsTheResult`.

**NewMemberAccountPage_Submit_TakenOrInvalidName_ShowsMessageAndCreatesNothing**: A taken name and an invalid name each
show a message next to the form, keep what was typed, and create nothing. This scenario is tested by
`NewMemberAccountPage_Submit_TakenOrInvalidName_ShowsMessageAndCreatesNothing`.

**NewMemberAccountPage_Submit_RightLostWhileOpen_CreatesNothingAndDropsTheForm**: The membership is revoked, the user
is demoted to plain member, or the session ends without the page knowing, and the form is submitted; nothing is
created and the page shows the not-found message with no form, no error, and nothing typed. This scenario is tested by
`NewMemberAccountPage_Submit_RightLostWhileOpen_CreatesNothingAndDropsTheForm`.

**NewMemberAccountPage_Copy_RightLostWhilePasswordShown_DropsThePasswordAndCopiesNothing**: The right is lost while the
password is shown and Copy is pressed; the password is dropped at once and is not handed to the browser. This scenario
is tested by `NewMemberAccountPage_Copy_RightLostWhilePasswordShown_DropsThePasswordAndCopiesNothing`.

**NewMemberAccountPage_Load_OtherProject_DropsNamesAndPassword**: The page is loaded again for a project the user may
not administer; the typed names and the password of the previous project are dropped. This scenario is tested by
`NewMemberAccountPage_Load_OtherProject_DropsNamesAndPassword`.

**NewMemberAccountPage_AfterRender_Interactive_SetsMarker**: The page for a new account is rendered; its root element
has `data-interactive` equal to `true`. This scenario is tested by
`NewMemberAccountPage_AfterRender_Interactive_SetsMarker`.

**ProjectPage_Render_ProjectWithoutReviews_ShowsNameAndEmptyMessage**: The page is rendered for a project without
reviews; the name, the description, the empty message, and the New review button, which links to the review creation
page of the project, are shown. This scenario is tested by
`ProjectPage_Render_ProjectWithoutReviews_ShowsNameAndEmptyMessage`.

**ProjectPage_Render_ProjectWithReview_ListsReviewWithLinkTitleAndApproval**: The page is rendered for a project with
a review; the review is listed with a link to its page, its title, and the badge "In review" (it has
no reviewer slot), and no state badge (`review-state-{id}`) is shown. This scenario is tested
by `ProjectPage_Render_ProjectWithReview_ListsReviewWithLinkTitleAndApproval`.

**ProjectPage_Render_SignedAndUnsignedReviews_ShowApprovedOrInReview**: A project has one review whose only slot was
signed with the right password and one review without slots; the first is listed as "Approved" and the second as "In
review". This scenario is tested by `ProjectPage_Render_SignedAndUnsignedReviews_ShowApprovedOrInReview`.

**NewReviewPage_FileInput_FilesSelected_ListsNamesAndSizes**: Two files are chosen; each is listed with its name and
size in bytes. This scenario is tested by `NewReviewPage_FileInput_FilesSelected_ListsNamesAndSizes`.

**NewReviewPage_FileInput_SecondSelection_ReplacesFirst**: A second selection replaces the first. This scenario is
tested by `NewReviewPage_FileInput_SecondSelection_ReplacesFirst`.

**NewReviewPage_ParametersChange_OtherProject_ClearsFormAndSelection**: A form is filled and a file selected, then the
page is reused for another project; the other project is shown with an empty form and no selected files. This
scenario is tested by `NewReviewPage_ParametersChange_OtherProject_ClearsFormAndSelection`.

**NewReviewPage_CreateForm_ValidInput_CreatesReviewAndNavigatesToIt**: Files are chosen, the form is completed and
submitted; the review is stored with the title, the fingerprint of the files, and the exact file bytes, and the
browser is sent to the review page. This scenario is tested by
`NewReviewPage_CreateForm_ValidInput_CreatesReviewAndNavigatesToIt`.

**NewReviewPage_CreateForm_SessionEndedBeforePageKnows_ShowsNotFoundAndCreatesNothing**: Files are chosen and a
title is entered, and the sign-in state ends without the page being told; the form is submitted. The access check
finds no signed-in user, so the page for a project that does not exist replaces the form, and no review is stored.
This scenario is tested by `NewReviewPage_CreateForm_SessionEndedBeforePageKnows_ShowsNotFoundAndCreatesNothing`.

**NewReviewPage_CreateForm_MembershipRevoked_ShowsNotFoundAndCreatesNothing**: A member has the filled-in form open and
the membership is removed; submitting shows the page for a project that does not exist, without the project name or
the form, and no review is stored. This scenario is tested by
`NewReviewPage_CreateForm_MembershipRevoked_ShowsNotFoundAndCreatesNothing`.

**NewReviewPage_CreateForm_NoFiles_ShowsErrorAndCreatesNothing**: The form is submitted without files; an error that
mentions files is shown and no review is stored. This scenario is tested by
`NewReviewPage_CreateForm_NoFiles_ShowsErrorAndCreatesNothing`.

**NewReviewPage_CreateForm_DuplicateReviewId_ShowsErrorNamingTheId**: A review with an existing identifier is
submitted; the error names the identifier, the entered values stay, and only the original review exists. This
scenario is tested by `NewReviewPage_CreateForm_DuplicateReviewId_ShowsErrorNamingTheId`.

**NewReviewPage_CreateForm_ReservedReviewId_ShowsErrorAndCreatesNothing**: The form is submitted with the review
identifier `new`, in any letter case; the error says that the identifier is reserved and no review is stored. This
scenario is tested by `NewReviewPage_CreateForm_ReservedReviewId_ShowsErrorAndCreatesNothing`.

**NewReviewPage_CreateForm_DuplicateFileNames_ShowsDuplicatePathError**: Two files with the same name are chosen and
the form is submitted; the error names the file and no review is stored. This scenario is tested by
`NewReviewPage_CreateForm_DuplicateFileNames_ShowsDuplicatePathError`.

**NewReviewPage_FileInput_FileTooLarge_ShowsErrorAndKeepsNothing**: A file over the size limit is chosen; the error
names the file and no selection is kept. The form is then submitted; the error still names the file (it is not
replaced by the "no files" error) and nothing is stored. This scenario is tested by
`NewReviewPage_FileInput_FileTooLarge_ShowsErrorAndKeepsNothing`.

**NewReviewPage_FileInput_TooManyFiles_ShowsErrorAndKeepsNothing**: One more small file than the file-count limit is
chosen; the error names the limit and no selection is kept. The form is then submitted; the error is still shown
and nothing is stored. This scenario is tested by `NewReviewPage_FileInput_TooManyFiles_ShowsErrorAndKeepsNothing`.

**NewReviewPage_FileInput_TotalSizeTooLarge_ShowsErrorAndKeepsNothing**: Enough files, each exactly at the per-file
size limit, are chosen so that together they exceed the total size limit (the files share one buffer to keep the
test's memory use low); the error says the files together are too large and no selection is kept. The form is then
submitted; the error is still shown and nothing is stored. This scenario is tested by
`NewReviewPage_FileInput_TotalSizeTooLarge_ShowsErrorAndKeepsNothing`.

**NewReviewPage_FileInput_DeclaredSizesUnderstated_RejectsOnReceivedBytes**: A fake browser file reports one byte
but streams the per-file maximum, and enough of them are delivered to the file input to pass the total limit; the
total-size error is shown and no selection is kept. This scenario is tested by
`NewReviewPage_FileInput_DeclaredSizesUnderstated_RejectsOnReceivedBytes`.

**ProjectPage_AfterRender_Interactive_SetsMarker**: The project page is rendered; its root element has
`data-interactive` equal to `true`. This scenario is tested by `ProjectPage_AfterRender_Interactive_SetsMarker`.

**ProjectPage_ReviewRules_AdminSavesToggle**: A project admin opens the project page; the Review rules card
(`review-rules-card`) shows the switch `independent-reviewers-switch` turned on. The switch is turned off and
`independent-reviewers-save` is pressed; the status "Independent reviewers are no longer required." is shown, the
project stores the new value, and one audit entry `project.independent-reviewers-changed` exists. Saving again
without a change says that the rules were already set this way. This scenario is tested by
`ProjectPage_ReviewRules_AdminSavesToggle`.

**ProjectPage_ReviewRules_NonAdmin_HidesCard**: The project page of a plain member has no Review rules card, no
switch, and no save button. This scenario is tested by `ProjectPage_ReviewRules_NonAdmin_HidesCard`.

**Evidence settings** are verified in `EvidenceSettingsPageTests.cs`, with the real `EvidenceSettingsService` and the
real access rules behind the page.

**ProjectPage_Render_ProjectAdmin_ShowsMembersAndButtons** and
**ProjectPage_Render_PlainMember_ShowsNoMembersAndNoAuditLink**: The project page of a project admin has the Evidence
settings button with the address `/projects/{id}/evidence-settings`; the page of a plain member has none. These scenarios
are tested by `ProjectPage_Render_ProjectAdmin_ShowsMembersAndButtons` and
`ProjectPage_Render_PlainMember_ShowsNoMembersAndNoAuditLink`.

**EvidenceSettingsPage_Render_Administrator_ShowsDefaultsWithoutWarnings**: A project admin and a system administrator
open the page of a project without stored settings; the two anonymous switches are off, the five content switches are
on, there is no summary, no exposure warning, and no warning notice, the Save settings button is there, and the Back to
project button leads to the project. This scenario is tested by
`EvidenceSettingsPage_Render_Administrator_ShowsDefaultsWithoutWarnings`.

**EvidenceSettingsPage_Render_EverySwitchHasADescription**: The seven rows each have a hint, and the one of the index
says that it is useless to ReviewMark without the PDFs, and mentions credentials. This scenario is tested by
`EvidenceSettingsPage_Render_EverySwitchHasADescription`.

**EvidenceSettingsPage_Render_NoAdministerRight_ShowsNotFoundAndNoSwitches**: A plain member, a user who is no member,
and a project admin who asks for a project that does not exist get the not-found message and no form, and the page does
not contain the project name. This scenario is tested by
`EvidenceSettingsPage_Render_NoAdministerRight_ShowsNotFoundAndNoSwitches`.

**EvidenceSettingsPage_Render_StoredSettings_AreShown**: The settings of the project were stored with the index on and
the file list off; the page shows exactly those, and the other switches keep their defaults. This scenario is tested by
`EvidenceSettingsPage_Render_StoredSettings_AreShown`.

**EvidenceSettingsPage_Switch_ContentOff_ShowsItsWarningAndTheBanner**: For each of the five content switches, turning
it off shows its own warning, which names the regulatory regime and the quality or compliance lead, as the only warning
notice of the row, and a summary that names its section; turning it on again removes both. This scenario is tested by
`EvidenceSettingsPage_Switch_ContentOff_ShowsItsWarningAndTheBanner`.

**EvidenceSettingsPage_Switch_AllContentOff_WarningsAreTailoredAndBannerListsAll**: With all five content switches off,
the reviewer names warning has the exact text "Omitting reviewer names may not satisfy your regulatory regime. Confirm
with your quality or compliance lead.", the five warnings differ from each other, and the summary lists all five
sections. This scenario is tested by `EvidenceSettingsPage_Switch_AllContentOff_WarningsAreTailoredAndBannerListsAll`.

**EvidenceSettingsPage_Switch_AnonymousOn_ShowsExposureWarningAndPeopleLine**: Turning the index on shows the exposure
warning and no sentence about people; turning the anonymous PDFs on adds the sentence that names the review discussion
and the reviewer names; turning the reviewer names off keeps only the discussion; turning the threads off removes the
sentence and keeps the general warning; turning both anonymous switches off removes the warning. This scenario is tested
by `EvidenceSettingsPage_Switch_AnonymousOn_ShowsExposureWarningAndPeopleLine`.

**EvidenceSettingsPage_Save_Change_StoresConfirmsAndAudits**: The anonymous PDFs are turned on and the reviewer names
off, and Save settings is pressed; the stored settings have exactly those two changes, the page says "Evidence settings
saved." and shows no error, and one audit entry of the project with the action `project.evidence-settings-changed`, the
user name, and the detail "AnonymousAttestations off->on; IncludeReviewerNames on->off" exists. This scenario is tested
by `EvidenceSettingsPage_Save_Change_StoresConfirmsAndAudits`.

**EvidenceSettingsPage_Save_NoChange_SaysNothingToSaveAndRecordsNothing**: Save settings is pressed with no change; the
page says that there is nothing to save and no audit entry exists. This scenario is tested by
`EvidenceSettingsPage_Save_NoChange_SaysNothingToSaveAndRecordsNothing`.

**EvidenceSettingsPage_Save_ChangedByOthers_ShowsMessageAndTheCurrentSettings**: Another administrator turns the index
on while the page is open; the first administrator turns the file list off and saves; the page shows the message that
someone else changed the settings, shows the index on and the file list on, the stored file list setting is unchanged,
and only the audit entry of the other administrator exists. This scenario is tested by
`EvidenceSettingsPage_Save_ChangedByOthers_ShowsMessageAndTheCurrentSettings`.

**EvidenceSettingsPage_Save_RightLostWhileOpen_StoresNothingAndDropsThePage**: With the page open and a switch changed,
the membership is revoked, the user is demoted to plain member, or the sign-in state is ended without the page knowing;
Save settings is pressed; the page shows the not-found message and no form, error, or status, does not contain the
project name, the stored settings are unchanged, and no audit entry exists. This scenario is tested by
`EvidenceSettingsPage_Save_RightLostWhileOpen_StoresNothingAndDropsThePage`.

**EvidenceSettingsPage_Switch_RightLostWhileOpen_DropsThePageAndTakesNoChange**: With the page open, the membership is
revoked, the user is demoted to plain member, or the sign-in state is ended without the page knowing; the file list
switch is turned off; the page shows the not-found message and no form, no file list warning, no summary banner, no
error or status, does not contain the project name, the stored file list setting is unchanged, and no audit entry
exists. This scenario is tested by `EvidenceSettingsPage_Switch_RightLostWhileOpen_DropsThePageAndTakesNoChange`.

**EvidenceSettingsPage_Load_OtherProject_DropsTheChoicesAndTheProject**: With a switch changed for the first project, the
page is given the identity of a second project that the user does not administer; it shows the not-found message and
neither project name. This scenario is tested by `EvidenceSettingsPage_Load_OtherProject_DropsTheChoicesAndTheProject`.

**EvidenceSettingsPage_AfterRender_Interactive_SetsMarker**: The evidence settings page is rendered; its root element has
`data-interactive` equal to `true`. This scenario is tested by `EvidenceSettingsPage_AfterRender_Interactive_SetsMarker`.
