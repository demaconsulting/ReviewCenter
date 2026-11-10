package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def DatabaseSetup {
        doc /* Reads the database settings, selects the provider and connection, creates the database on first use, reports without creating anything whether the database file is missing, and resolves the data folder that holds the database, the git cache, and the key ring. */

        metadata info : SoftwareInfo {
            description = "Reads the database settings, selects the provider and connection, creates the database on first use, reports without creating anything whether the database file is missing, and resolves the data folder that holds the database, the git cache, and the key ring.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Storage/DatabaseSetup.cs", "src/DemaConsulting.ReviewCenter/Storage/DatabaseOptions.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Storage/DatabaseSetupTests.cs");
            designFiles = ("docs/design/review-center/storage/database-setup.md");
            verificationFiles = ("docs/verification/review-center/storage/database-setup.md");
            requirementFiles = ("docs/reqstream/review-center/storage/database-setup.yaml");
        }
    }
}
