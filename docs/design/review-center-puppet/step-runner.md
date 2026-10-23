## StepRunner

![ReviewCenterPuppet Structure](ReviewCenterPuppetView.svg)

### Purpose

`StepRunner` (in `StepFile.cs` and `StepRunner.cs`) is the logic of Puppet: it reads and validates the steps file,
and runs the steps in order against an abstract page. It never touches a real browser, so it is tested with a fake
page.

### Data Model

**Step**: one validated step: number, kind, and the properties that kind needs.

| Kind         | Properties                                                      |
|--------------|-----------------------------------------------------------------|
| `goto`       | `url`: a path starting with `/`                                 |
| `click`      | `selector`                                                      |
| `fill`       | `selector`, `value`                                             |
| `text`       | `selector`                                                      |
| `screenshot` | `file`: optional plain name (`.png` is added), `fullPage`: bool |

**IBrowserPage**: the page abstraction (`Goto`, `Click`, `Fill`, `Text`, `Screenshot`) that `PlaywrightBrowser`
implements.

**StepResult**: one result line: step number, kind, `ok`, and the optional `text`, `file`, or `error`. It serializes to
one JSON line.

### Key Methods

**StepFile.Parse**: Parses and validates the file text. Unknown properties are errors, so a typo is reported. An
invalid file raises a `PuppetException` of the form `Steps file 'x': step N (kind): problem`.

**StepRunner.RunAsync**: Runs the steps in order, reports one result per step through a callback, and returns whether
all succeeded. At the first failing step it stops, reports the error, and tries to save `failure-step-N.png` in the
output folder; a failure of that screenshot is ignored.

### Error Handling

Invalid input is a `PuppetException` before anything is started. A failing step is reported as a result, not thrown.

### Dependencies

- **SelectorResolver**: resolves the selector of `click`, `fill`, and `text`.
