# Implementation rules

Rules agreed while building this code, beside the conventions in [`CLAUDE.md`](../CLAUDE.md). Read
them before implementing. A rule that no longer holds is deleted here, not kept marked.

## Repo

1. The repo keeps only `main` and `develop`.
2. Names spell out "Windows", types as well as ids; `Win` stays only inside a proper name (WinTun,
   WinDivert).

## Code

3. A stop's cancellation goes in its own catch that rethrows, ahead of the catch-all, not in a
   `when` filter: it reads more easily.
4. A member is required when every platform has it. A setting with a natural default is a plain type
   set to that default, not a nullable one.
5. Migration code carries a dated remark: `Migration (YYYY-MM): drop a few months after it ships`.
6. What only one head needs stays in that head, as a tiny helper, not as wiring across AppLib.
7. Native and OS helpers take names as parameters, never brand constants; a default is brand-neutral,
   and our products override it (`VhAppCommandName`).
8. A library never enforces the app id's format; it derives from it what the OS needs.
9. A helper is a private method, never a local function inside another method.
10. A generic type that shares its name with another type has a file of its own, named for its type
    parameters' count, as .NET's own source does: `TimeoutItem_1.cs` beside `TimeoutItem.cs`. A
    generic type alone keeps the plain name.

## Design

11. The access-manager protocol needs to be compatible one way only: the access manager is updated
    first, so a new server may count on it, but the access manager keeps supporting old servers.
12. The access manager is under heavy load: rather a cost on the server than more requests to it.
    After an outage, the waiting requests are merged, never queued up to bombard it.
13. The service opens no UI and no links; the UI does, as on iOS.
14. A Debug build's name must stand out.
