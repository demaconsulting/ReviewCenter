# Introduction

This document contains the requirements for the ReviewCenter project.

## Purpose

ReviewCenter is a web-based formal code review service with a companion .NET command-line
interface. The service is an ASP.NET Core Blazor Server application started with the `serve`
verb; the command-line interface provides argument parsing, self-validation, and comprehensive
documentation generation. The repository also contains Puppet, a separate development tool that
drives one persistent headless browser against the web interface, one command at a time; it is
specified as its own system, ReviewCenterPuppet.

## Scope

This requirements document covers:

- Command-line interface and options
- The `serve` verb, the web home page, and the health endpoint
- Self-validation framework
- Test result output formats
- Logging capabilities
- Platform support
- Documentation generation
- CI/CD integration
- The Puppet development tool (ReviewCenterPuppet system)
- Off-the-shelf software items used by the above

## Audience

This document is intended for:

- Software developers working on ReviewCenter
- Quality assurance teams validating requirements
- Project stakeholders reviewing project scope
- Users understanding the tool's capabilities
