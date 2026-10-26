## YamlDotNet

This document describes the integration and usage design for the `YamlDotNet` OTS software item (`YamlDotNet`).

### Purpose

YamlDotNet is a widely used library for reading and writing YAML. ReviewCenter uses it in the distributed application
to read the review-sets out of the ReviewMark `.reviewmark.yaml` file of a registered repository. ReviewMark itself reads
the file with the same library, so the file means the same to both tools.

### Features Used

- Deserializing YAML text into plain typed objects, with the camel-case naming convention and with the keys that the
  objects do not have ignored
- Reading the events of a YAML text (the event parser), to find an alias before anything is built
- The error type of the library, which carries the line and the column of a mistake

### Integration Pattern

The package is a reference of the application project only, and only the `ReviewSetParser` unit (the class
`ReviewMarkConfigParser`) uses it, so that no other unit depends on the library. The parser never gives the types, the
messages, or the stack of the library to its caller: a failure becomes a `DomainRuleException` with a plain message. The
deserializer builds plain objects only (no tags and no object graphs), and aliases are refused, so a file cannot
choose what is built or expand into an enormous document. The library has no self-validation mode; the passing tests of
the parser are the integration evidence.
