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
5. Migration code is marked `[Obsolete("Migration (YYYY-MM): drop a few months after it ships, with
   <what goes with it>")]`, so each call is marked too, by its `#pragma warning disable CS0618`. A
   migration method's name says so (`MigrateInstallId`) unless its class's does (`AppStorageMigration`).
   Compatibility code, what stays only so older callers keep working - an old option, an old name, an
   old protocol - carries a dated remark, never a `todo:`, which would list it among the todos:
   `Compatibility (YYYY-MM): <who still calls it>; drop <when>`, so it can be found and removed.
6. What only one head needs stays in that head, as a tiny helper, not as wiring across AppLib.
7. Native and OS helpers take names as parameters, never brand constants; a default is brand-neutral,
   and our products override it (`VhAppCommandName`).
8. A library never enforces the app id's format; it derives from it what the OS needs.
9. A helper is a private method, never a local function inside another method.
10. A generic type that shares its name with another type has a file of its own, named for its type
    parameters' count, as .NET's own source does: `TimeoutItem_1.cs` beside `TimeoutItem.cs`. A
    generic type alone keeps the plain name.
11. No object made and dropped in one statement, kept alive only by the handlers it subscribed
    (`new X(...).Apply()`): a static method whose handlers capture what they need, as
    `AvaloniaWindowFit.Apply` does.
12. `Dispose` and `DisposeAsync` are a class's last members; only its nested types follow them.
13. A type with both closes: `Dispose` is the quick close, and may be ungraceful: it may abort a
    connection, skip a goodbye or drop what is unsent. It never waits: it releases what is local at
    once and starts its async closes without waiting for them, none of which may need what it
    releases. A QUIC object's quick close is its async close, started without waiting: that close
    waits only for its peer, nothing local is left to force, and nothing may count on it having
    ended. `DisposeAsync` is the graceful close, with a limit on each of its waits on the network.
    Async code calls `DisposeAsync`; a sync `Dispose` is called only from a callback the system makes
    synchronously, from the quick close that callback starts, and at termination once the graceful
    close has missed its deadline (rule 15).
14. A `Dispose` may come while `DisposeAsync` is still running. It releases each local resource
    once, by that resource's own atomic guard rather than the shared disposed flag, takes no lock
    that `DisposeAsync` holds across an await, and cancels what `DisposeAsync` waits for.
    `DisposeAsync` expects, after each await, that `Dispose` may have released what it uses; a
    second `DisposeAsync` waits for the first one's end.
15. Termination: the entry point gives the whole close one deadline within the system's stop budget,
    keeping time for the local cleanup. It awaits `DisposeAsync` up to that deadline, then calls
    `Dispose`; a sync system callback, such as Android's `OnDestroy`, calls `Dispose` at once. A
    close's steps that do I/O (a goodbye, a sync, a flush, a system command) each run in their own
    catch that logs the failure, so a failed one never skips the next, and nothing crashes; an async
    close may go through `TryDisposeAsync`, ours or not. A close handles its own failures where they
    arise, so a sync `Dispose` of our own types must not throw (CA1065), and its caller wraps it in
    neither a `TryDispose` nor a `finally`.
16. A token is passed down, never dropped: a method that waits, or calls one that takes a token,
    takes a required `CancellationToken` as its last parameter and passes it on. `CancellationToken.None`
    is written only at a chain's head - a callback the system or the UI makes without one, an entry
    point, a fire-and-forget start, a constructor, a close - or where the work must outlive its
    caller's cancel, which a comment there says; inside the method when that is the method's own
    promise (a stop that waits for a restart, a write a cut would leave truncated). A method's own
    limit is a linked token (`CreateLinkedTokenSource`, `CancelAfter`) when its end is a cancel, and a
    `TimeSpan` beside the token when its end is an error of its own (`FileUtils.WaitOutSharingAsync`
    rethrows the last sharing error).
17. A method that waits on I/O - a file, the network, a delay between tries - returns a `Task`, and so
    do its callers, up to the head. It is sync only on a hot path, for a sync lock's own wait, where the
    I/O has no async form (SQLite opening its databases), or under a caller that is sync by contract
    and needs the answer before it returns, which then blocks on the task (Android's `OnStartCommand`,
    Avalonia's `Main`). A constructor is not such a caller: a type that must wait to be made gets a
    static async `Create`.

## Design

18. The access-manager protocol needs to be compatible one way only: the access manager is updated
    first, so a new server may count on it, but the access manager keeps supporting old servers.
19. The access manager is under heavy load: rather a cost on the server than more requests to it.
    After an outage, the waiting requests are merged, never queued up to bombard it.
20. The service opens no UI and no links; the UI does, as on iOS.
21. A Debug build's name must stand out.
22. An updater runs apart from what it updates, as a standalone script: an app that fails to run
    must still get the release that fixes it.
