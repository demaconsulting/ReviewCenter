## YamlDotNet Verification

This document provides the verification evidence for the YamlDotNet OTS software item. Requirements for this OTS item
are defined in the YamlDotNet OTS Software Requirements document.

### Required Functionality

YamlDotNet reads YAML in its common styles into typed objects, leaves out the keys that the objects do not have, lets
the tool see an alias before it is expanded, and reports invalid YAML with its position. A passing test confirms that
the parser of the review-set file gets all of this from the library.

### Verification Approach

YamlDotNet has no self-validation mode and is verified by the project's own tests, which run the real library in memory.
Each scenario names a specific test method that depends on the library under verification. A passing pipeline run for
the scenario constitutes evidence that the requirement is satisfied.

### Test Scenarios

#### ReviewMarkConfigParser_Parse_RepositoryOwnConfiguration_ReadsEverySet

**Scenario**: The `.reviewmark.yaml` of this repository, which has many keys that the parser does not read, is read.

**Expected**: Every review-set is read with its identifier, title, and paths, and the other keys are left alone.

**Requirement coverage**: `ReviewCenter-OTS-YamlDotNet-Parse`.

#### ReviewMarkConfigParser_Parse_YamlStyles_AreUnderstood

**Scenario**: A file in flow style, with quoting, a folded block scalar, a byte order mark, and Windows line endings is
read.

**Expected**: The values are the ones that YAML defines.

**Requirement coverage**: `ReviewCenter-OTS-YamlDotNet-Parse`.

#### ReviewMarkConfigParser_Parse_BrokenFile_ThrowsConfigInvalid

**Scenario**: Files with broken YAML and with entries of the wrong shape are read.

**Expected**: Each is reported as an error, and the error of the library carries the line and the column that appear in
the message of the tool.

**Requirement coverage**: `ReviewCenter-OTS-YamlDotNet-Parse`.

#### ReviewMarkConfigParser_Parse_Alias_ThrowsConfigInvalid

**Scenario**: A file that repeats a list through an alias is read.

**Expected**: The event parser of the library reports the alias, and the tool refuses the file before anything is built.

**Requirement coverage**: `ReviewCenter-OTS-YamlDotNet-Parse`.
