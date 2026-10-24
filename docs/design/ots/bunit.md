## bUnit

This document describes the integration and usage design for the `bUnit` OTS software item.

### Purpose

bUnit is a component testing library for Blazor. Together with `Microsoft.AspNetCore.TestHost` it lets the test
project render Razor components and host the web application in memory, so the user interface and the hosting
pipeline can be verified without a browser or a network listener. It is used by tests only and is not part of the
distributed ReviewCenter package.

### Features Used

- Rendering of Razor components in a test context, with markup assertions
- Simulated user interaction such as clicking buttons, submitting forms, and selecting files for a file input,
  followed by re-rendering
- Rendering of the router for a given URL through the test navigation manager, and observing navigation
- `Microsoft.AspNetCore.TestHost` in-memory server and HTTP client for request/response tests of the hosted
  application

### Integration Pattern

bUnit and the test host are consumed as NuGet package references of the `DemaConsulting.ReviewCenter.Tests` project.
Each component test creates a bUnit test context, renders the component, interacts with it, and asserts on the
rendered markup; the context is disposed at the end of the test. In-memory host tests build the application through
the `ServerHost` creation seam, send requests with the test client, and dispose the application afterwards. Neither
library has a self-validation mode; the passing component and hosting tests are the integration evidence.
