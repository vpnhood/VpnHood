// Run tests in parallel; tests that use the WinDivert adapter are marked [DoNotParallelize]
// because WinDivert is a machine-wide kernel driver and its tests intercept each other's traffic.
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]
