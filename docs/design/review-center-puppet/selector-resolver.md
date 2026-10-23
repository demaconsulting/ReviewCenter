## SelectorResolver

![ReviewCenterPuppet Structure](ReviewCenterPuppetView.svg)

### Purpose

`SelectorResolver` turns the selector text a user types into the selector that the browser evaluates. A plain name
means the `data-testid` attribute of an element, because the ReviewCenter web interface marks the elements a driver
needs with stable test ids. Any other text is a Playwright selector and is passed through unchanged.

### Data Model

None - `SelectorResolver` is a static class without state.

### Key Methods

**Resolve**: Resolves selector text.

- *Parameters*: `string selector` - the text typed by the user.
- *Returns*: `string` - the selector for the browser.
- *Postconditions*: A plain name made only of letters, digits, hyphens, and underscores is returned as the attribute
  selector `[data-testid="name"]`. Text containing any other character, such as `#`, `.`, a space, `=`, `>`, or `:`,
  is returned exactly as given.

### Error Handling

`Resolve` throws `ArgumentException` when the selector is null, empty, or only white space. `StepFile` rejects a
missing selector first, so a user sees the step-file message.

### Dependencies

None - `SelectorResolver` uses only the .NET base class library.

### Callers

- **StepRunner**: resolves the selector of the `click`, `fill`, and `text` steps.
