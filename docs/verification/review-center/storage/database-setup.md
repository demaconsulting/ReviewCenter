### DatabaseSetup

#### Verification Approach

`DatabaseSetup` is verified with unit tests defined in `DatabaseSetupTests.cs`. Path and connection string logic is
tested as pure functions with injected folders. Provider selection and creation are tested with real SQLite
database files in a private temporary folder, because creating the folder, the file, and the tables is the
behavior under test. No mocking is applied.

#### Test Environment

Tests create temporary SQLite database files, which are removed after the test run. The native SQLite library must
be loadable on the test platform.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- The default database file is in the ReviewCenter folder of the application data folder, and beside the program
  when no application data folder exists.
- An empty connection string selects the default file, a plain value is a file path, and a connection string is
  used unchanged.
- The provider name is matched without regard to case, and an unsupported provider is refused with a message that
  names it, before anything is created.
- An in-memory database setting is refused with a message that asks for a file path.
- Creation makes the missing folder, the file, and the tables.

#### Test Scenarios

**DatabaseSetup_GetDefaultDatabasePath_WithDataFolder_UsesAppDataSubfolder**: A known data folder gives the path
`<data>/ReviewCenter/reviewcenter.db`. This scenario is tested by
`DatabaseSetup_GetDefaultDatabasePath_WithDataFolder_UsesAppDataSubfolder`.

**DatabaseSetup_GetDefaultDatabasePath_WithoutDataFolder_FallsBackToBaseDirectory**: An empty data folder gives
the path `<program folder>/data/reviewcenter.db`. This scenario is tested by
`DatabaseSetup_GetDefaultDatabasePath_WithoutDataFolder_FallsBackToBaseDirectory`.

**DatabaseSetup_ResolveConnectionString_Empty_UsesDefaultFile**: Nothing is configured; the data source is the
default path. This scenario is tested by `DatabaseSetup_ResolveConnectionString_Empty_UsesDefaultFile`.

**DatabaseSetup_ResolveConnectionString_PlainPath_BecomesDataSource**: A plain file path becomes the data source.
This scenario is tested by `DatabaseSetup_ResolveConnectionString_PlainPath_BecomesDataSource`.

**DatabaseSetup_ResolveConnectionString_FullConnectionString_IsUsedAsIs**: A full connection string is returned
unchanged. This scenario is tested by `DatabaseSetup_ResolveConnectionString_FullConnectionString_IsUsedAsIs`.

**DatabaseSetup_ResolveConnectionString_InMemory_ThrowsClearError**: Each in-memory form (`Data Source=:memory:`,
`:memory:`, `Mode=Memory`, an empty data source) is refused with a message naming the in-memory database and asking
for a file path. This scenario is tested by `DatabaseSetup_ResolveConnectionString_InMemory_ThrowsClearError`.

**DatabaseSetup_Configure_UnsupportedProvider_ThrowsClearError**: An unsupported provider is refused with a
message that names it and the supported provider. This scenario is tested by
`DatabaseSetup_Configure_UnsupportedProvider_ThrowsClearError`.

**DatabaseSetup_Configure_ProviderInOtherCase_IsAccepted**: A provider name in lowercase is accepted. This
scenario is tested by `DatabaseSetup_Configure_ProviderInOtherCase_IsAccepted`.

**DatabaseSetup_EnsureCreated_UnsupportedProvider_ThrowsAndCreatesNothing**: Creation with an unsupported
provider is refused with a message that names it, and the database folder is not created. This scenario is tested
by `DatabaseSetup_EnsureCreated_UnsupportedProvider_ThrowsAndCreatesNothing`.

**DatabaseSetup_EnsureCreated_MissingFolder_CreatesFolderFileAndTables**: Creation with a database path in a
missing folder makes the folder, the file, and the tables. This scenario is tested by
`DatabaseSetup_EnsureCreated_MissingFolder_CreatesFolderFileAndTables`.
