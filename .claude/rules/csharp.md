---
paths:
  - "**/*.cs"
---

## Language

- Use primary constructors when possible.
- Use `TestHelper.WorkingPath` as the temp directory for tests.
- Don't use the `async` postfix if there is no async method with the same name.
- Use `SafeDisposeAsync` if you want to dispose an `IAsyncDisposable` and catch/ignore any exception thrown.
- Use `AsyncLock` instead of `SemaphoreSlim` for non-hot-path code, and use it with a `using` statement.

## Await and ConfigureAwait

- Use `.Vhc()` instead of `.ConfigureAwait(false)` if it is available, but do not add it to the project if
  it is not available.
- For UI code such as Android UI, always use `.ConfigureAwait(false)` when it is required.
- The build warns on an await that says neither in library and background code (VH0001), and on `.Vhc()`
  in code that runs on a UI thread (VH0002): `src/Net/VpnHood.Net.Toolkit.Analyzers`, by folder in `.editorconfig`.
