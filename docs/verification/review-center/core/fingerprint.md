### Fingerprint

#### Verification Approach

`Fingerprint` is verified with unit tests defined in `FingerprintTests.cs`. The tests call the static methods
directly with byte arrays and compare the results with known-answer vectors. The vectors come from the ReviewMark
reference tool and an independent implementation, as recorded in the design of this unit, so the tests prove
agreement with the reference rather than repeat the implementation. No mocking is applied.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- The hash of a file and the fingerprint of file sets equal the known answers, including content with carriage
  returns, zero bytes, and bytes that are not valid text.
- The fingerprint does not depend on the order of the files and does not collapse duplicated content.
- Line endings are not normalized.

#### Test Scenarios

**Fingerprint_ComputeFileHash_KnownText_MatchesKnownAnswer**: The hash of the content "hello" and a line feed
equals the known SHA-256. This scenario is tested by `Fingerprint_ComputeFileHash_KnownText_MatchesKnownAnswer`.

**Fingerprint_ComputeFileHash_BinaryWithCrLf_MatchesKnownAnswer**: The hash of binary content that contains a
zero byte, a 0xFF byte, and a carriage return plus line feed pair equals the known SHA-256. This scenario is
tested by `Fingerprint_ComputeFileHash_BinaryWithCrLf_MatchesKnownAnswer`.

**Fingerprint_ComputeFileHash_CrLfVersusLf_DiffersBecauseNoNormalization**: The same text with line feed and with
carriage return plus line feed gives different hashes. This scenario is tested by
`Fingerprint_ComputeFileHash_CrLfVersusLf_DiffersBecauseNoNormalization`.

**Fingerprint_Compute_SingleFile_MatchesKnownAnswer**: The fingerprint of a set of one file equals the known
answer. This scenario is tested by `Fingerprint_Compute_SingleFile_MatchesKnownAnswer`.

**Fingerprint_Compute_ThreeFilesIncludingBinary_MatchesKnownAnswer**: The fingerprint of the three-file set that
includes the binary file equals the known answer. This scenario is tested by
`Fingerprint_Compute_ThreeFilesIncludingBinary_MatchesKnownAnswer`.

**Fingerprint_Compute_DifferentOrder_GivesSameFingerprint**: The same files given in another order give the same
fingerprint. This scenario is tested by `Fingerprint_Compute_DifferentOrder_GivesSameFingerprint`.

**Fingerprint_Compute_DuplicateContent_IsNotCollapsed**: Two files with identical content give the known
two-file answer, which differs from the answer for one file. This scenario is tested by
`Fingerprint_Compute_DuplicateContent_IsNotCollapsed`.

**Fingerprint_Compute_FromFileHashes_EqualsFromContents**: The fingerprint calculated from the per-file hashes
equals the fingerprint calculated from the contents. This scenario is tested by
`Fingerprint_Compute_FromFileHashes_EqualsFromContents`.
