// Copyright (c) DEMA Consulting
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     The review checklist every new review starts with: the checks of the Continuous Compliance review template.
/// </summary>
/// <remarks>
///     The identifiers and wording are those of section 2 of the template at
///     https://github.com/demaconsulting/ContinuousCompliance/raw/refs/heads/main/docs/review-template/review-template.md
///     (14 sections, 58 checks). Any change of wording or identifiers must come with a new
///     <see cref="Version"/>; reviews keep the frozen copy they were created with.
/// </remarks>
public static class DefaultChecklist
{
    /// <summary>
    ///     The version label of the wording below.
    /// </summary>
    public const string Version = "default-1";

    /// <summary>
    ///     The applicability hint shared by the sections that only concern source code.
    /// </summary>
    private const string SkipWithoutSourceCode = "Skip this section if the review contains no source code files.";

    /// <summary>
    ///     Gets the default checklist.
    /// </summary>
    public static ChecklistDefinition Current { get; } = new(Version,
    [
        new ChecklistSection("REQ", "Requirements Checks", "Skip this section if the review contains no requirements files.",
        [
            new ChecklistItem("REQ-01", "All requirements have a unique identifier"),
            new ChecklistItem("REQ-02", "All requirements are unambiguous (only one valid interpretation)"),
            new ChecklistItem("REQ-03", "All requirements are testable (compliance can be demonstrated by a test)"),
            new ChecklistItem("REQ-04", "All requirements are consistent (no requirement contradicts another)"),
            new ChecklistItem("REQ-05", "All requirements are complete (no TBDs, undefined terms, or missing information)"),
            new ChecklistItem("REQ-06", "All requirements are verifiable (can be objectively confirmed as met or not met)"),
            new ChecklistItem("REQ-07", "No compound requirements are present (each requirement expresses a single testable criterion)"),
            new ChecklistItem("REQ-08", "No requirements are missing (all expected behaviors and constraints are specified)")
        ]),
        new ChecklistSection("DES", "Design Documentation Checks", "Skip this section if the review contains no design documentation files.",
        [
            new ChecklistItem("DES-01", "Design documentation clearly describes the purpose of the component or feature"),
            new ChecklistItem("DES-02", "Design documentation covers the necessary implementation details"),
            new ChecklistItem("DES-03", "Design documentation describes how the code is interfaced (APIs, inputs, outputs)"),
            new ChecklistItem("DES-04", "Design documentation describes the expected normal operation"),
            new ChecklistItem("DES-05", "Design documentation describes the expected error handling")
        ]),
        new ChecklistSection("DOC", "Technical Documentation Checks", "Skip this section if the review contains no general technical documentation files (e.g., user guides, API references, README files, release notes).",
        [
            new ChecklistItem("DOC-01", "Documentation is free of technical inaccuracies"),
            new ChecklistItem("DOC-02", "Documentation is consistent with the current implementation and requirements"),
            new ChecklistItem("DOC-03", "All referenced external documents and dependencies are correctly identified"),
            new ChecklistItem("DOC-04", "Documentation is free of spelling and grammar errors")
        ]),
        new ChecklistSection("CODE", "Code Checks", SkipWithoutSourceCode,
        [
            new ChecklistItem("CODE-01", "Code conforms to the project coding standards and style guide"),
            new ChecklistItem("CODE-02", "No obvious resource leaks are present (file handles, connections, memory)"),
            new ChecklistItem("CODE-03", "No hardcoded values are present that should be configurable"),
            new ChecklistItem("CODE-04", "Each unit or function has a single, well-defined responsibility"),
            new ChecklistItem("CODE-05", "Code is written at the appropriate level of abstraction"),
            new ChecklistItem("CODE-06", "Code has an appropriate amount of extensibility for its context")
        ]),
        new ChecklistSection("LOGIC", "Logic Error Checks", SkipWithoutSourceCode,
        [
            new ChecklistItem("LOGIC-01", "Code does only what is intended (no unintended side effects or behaviors)"),
            new ChecklistItem("LOGIC-02", "All significant inputs and boundary conditions are handled correctly"),
            new ChecklistItem("LOGIC-03", "Concurrency and threading concerns are identified and addressed")
        ]),
        new ChecklistSection("ERR", "Error Handling & Logging Checks", SkipWithoutSourceCode,
        [
            new ChecklistItem("ERR-01", "Error handling follows the approach described in the design documentation"),
            new ChecklistItem("ERR-02", "The logging volume and level of detail are appropriate"),
            new ChecklistItem("ERR-03", "Error messages are user-friendly and actionable"),
            new ChecklistItem("ERR-04", "Error messages and log entries do not leak sensitive data")
        ]),
        new ChecklistSection("USE", "Usability / Accessibility Checks", "Skip this section if usability and accessibility are not relevant to the files under review.",
        [
            new ChecklistItem("USE-01", "The feature or API is easy to use correctly"),
            new ChecklistItem("USE-02", "All public APIs are well documented")
        ]),
        new ChecklistSection("TEST", "Test Checks", "Skip this section if the review contains no test code files.",
        [
            new ChecklistItem("TEST-01", "Tests cover expected (happy-path) behavior"),
            new ChecklistItem("TEST-02", "Tests cover error conditions and boundary cases"),
            new ChecklistItem("TEST-03", "Tests are independent and repeatable (no shared mutable state, no ordering dependency)"),
            new ChecklistItem("TEST-04", "Test names clearly describe the behavior being verified"),
            new ChecklistItem("TEST-05", "New test cases are added for new functionality or defect fixes")
        ]),
        new ChecklistSection("SEC", "Security Checks", "Skip this section if the review contains no source code files or if security concerns are not relevant to the files under review.",
        [
            new ChecklistItem("SEC-01", "No obvious security vulnerabilities are present (e.g., injection flaws, hardcoded credentials)"),
            new ChecklistItem("SEC-02", "Authentication and authorization are handled correctly (see design documentation)"),
            new ChecklistItem("SEC-03", "Sensitive data is stored and transmitted securely")
        ]),
        new ChecklistSection("READ", "Code Readability Checks", SkipWithoutSourceCode,
        [
            new ChecklistItem("READ-01", "Code is easy to understand"),
            new ChecklistItem("READ-02", "Methods and functions are small enough to be easily understood"),
            new ChecklistItem("READ-03", "Symbols (variables, functions, classes) are well named"),
            new ChecklistItem("READ-04", "Code is located in the correct place in the codebase"),
            new ChecklistItem("READ-05", "Flow of control can be easily followed"),
            new ChecklistItem("READ-06", "Data flow is understandable"),
            new ChecklistItem("READ-07", "Comments are provided where the code is non-obvious"),
            new ChecklistItem("READ-08", "No debug artifacts or commented-out code have been left in the codebase")
        ]),
        new ChecklistSection("REQDOC", "Requirements vs Documentation Checks", "Skip this section if the review contains no requirements files or no general technical documentation files.",
        [
            new ChecklistItem("REQDOC-01", "All reviewed requirements are addressed in the general technical documentation"),
            new ChecklistItem("REQDOC-02", "No reviewed requirement is contradicted by the general technical documentation")
        ]),
        new ChecklistSection("REQIMP", "Requirements vs Implementation Checks", "Skip this section if the review contains no requirements files or no source code files.",
        [
            new ChecklistItem("REQIMP-01", "All requirements under review are addressed by the implementation"),
            new ChecklistItem("REQIMP-02", "No requirement is contradicted by the implementation")
        ]),
        new ChecklistSection("REQTEST", "Requirements vs Testing Checks", "Skip this section if the review contains no requirements files or no test code files.",
        [
            new ChecklistItem("REQTEST-01", "Every requirement under review is covered by at least one test"),
            new ChecklistItem("REQTEST-02", "Tests verify the behavior described in each requirement")
        ]),
        new ChecklistSection("CODEDOC", "Code vs Design Documentation Checks", "Skip this section if the review contains no source code files or no design documentation files.",
        [
            new ChecklistItem("CODEDOC-01", "The code correctly implements the design documentation"),
            new ChecklistItem("CODEDOC-02", "All public APIs and interfaces are documented in the design documentation"),
            new ChecklistItem("CODEDOC-03", "Non-obvious algorithms and significant design decisions are explained in the design documentation"),
            new ChecklistItem("CODEDOC-04", "No important code details are missing from the design documentation")
        ])
    ]);
}
