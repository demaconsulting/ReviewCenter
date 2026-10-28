### UserCommand

#### Verification Approach

`UserCommand` is verified with unit tests defined in `UserCommandTests.cs`. The tests run the real command, which
builds the same services as the server through `ServerHost.AddServices` over a new temporary SQLite database, with a
real `Context` created from the `user add` arguments. The password source is replaced by a function the test
supplies, so that no console input is needed; the source from the environment variable and the closed standard input
are verified in the system-level tests of `ReviewCenter`. Everything the command writes is read back from the log
file of the context. A database is then opened again through the real pipeline to check what was stored.

#### Test Environment

N/A - standard test environment (temporary SQLite databases are created by the test helper).

#### Acceptance Criteria

- All unit tests pass with zero failures.
- A valid command creates the user with the given display name, reports the user name and whether it is a system
  administrator, never prints the password, and returns exit code 0.
- A command without `--system-admin` creates an ordinary user.
- A user name that already exists, in any case, and a password that breaks the policy are reported as errors with
  exit code 1 and the password is not printed.
- A missing password is reported as the error "No password was provided." with exit code 1, and no database is
  created.
- A null context is rejected with an `ArgumentNullException`.

#### Test Scenarios

**UserCommand_Run_ValidUser_CreatesUserAndReportsItWithoutPassword**: A system administrator is added to a new
database with `--system-admin`; the exit code is 0, the output says "Created user 'root' (system administrator)." and
does not contain the password, and the user signs in afterwards with the password and has the display name and the
system administrator flag. This scenario is
tested by `UserCommand_Run_ValidUser_CreatesUserAndReportsItWithoutPassword`.

**UserCommand_Run_NoSwitch_CreatesOrdinaryUser**: A user is added without `--system-admin`; the exit code is 0 and
the stored user is not a system administrator. This scenario is tested by `UserCommand_Run_NoSwitch_CreatesOrdinaryUser`.

**UserCommand_Run_UserNameTaken_ReportsErrorWithExitCodeOne**: A user is added twice with the user name in another
case the second time; the second run returns exit code 1 and the output says that the user already exists. This
scenario is tested by `UserCommand_Run_UserNameTaken_ReportsErrorWithExitCodeOne`.

**UserCommand_Run_PasswordTooShort_ReportsErrorAndCreatesNothing**: A user is added with a password that is too
short; the exit code is 1, the output explains the number of characters and does not contain the password, and the
user was not created. This scenario is tested by `UserCommand_Run_PasswordTooShort_ReportsErrorAndCreatesNothing`.

**UserCommand_Run_NoPassword_ReportsErrorWithExitCodeOne**: A user is added with a password source that has nothing;
the exit code is 1, the output says "No password was provided.", and the database file was not created. This
scenario is tested by `UserCommand_Run_NoPassword_ReportsErrorWithExitCodeOne`.

**UserCommand_Run_NullContext_ThrowsArgumentNullException**: `Run` is called with a null context; an
`ArgumentNullException` is thrown. This scenario is tested by `UserCommand_Run_NullContext_ThrowsArgumentNullException`.

**Actor of the command**: The user created by the command is recorded in the audit trail with the actor `(command
line)`. This is tested at the service level by `UserService_CreateAsync_CommandLineActor_IsRecordedAsCommandLine`.
