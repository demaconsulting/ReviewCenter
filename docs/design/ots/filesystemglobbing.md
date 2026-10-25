## FileSystemGlobbing

This document describes the integration and usage design for the `FileSystemGlobbing` OTS software item
(`Microsoft.Extensions.FileSystemGlobbing`).

### Purpose

FileSystemGlobbing is a widely used library for matching paths against glob patterns. ReviewCenter does not use it in
the distributed tool, because the `SelectionMatcher` unit has its own bounded matcher. The tests use the library as an
independent reference, to check that the matcher agrees with it on the common pattern syntax. It is a test-only
dependency.

### Features Used

- Matching a path against include and exclude glob patterns in memory, without a file system

### Integration Pattern

The package is a reference of the test project only, and no other project refers to it. The differential test builds
a set of patterns and paths, asks both matchers, and fails when they disagree. The library has no self-validation
mode; the passing differential test is the integration evidence.
