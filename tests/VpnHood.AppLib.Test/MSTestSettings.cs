// Run tests in parallel; VpnHoodApp instances opt out of singleton registration in tests
// (AppOptions.IsSingletonMode = false), so apps can coexist. Tests that use the machine-wide
// WinDivert adapter or mutate process-global state are marked [DoNotParallelize].
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]