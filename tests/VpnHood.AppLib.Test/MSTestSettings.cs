// Run tests in parallel; VpnHoodApp instances opt out of singleton registration in tests
// (AppOptions.IsSingleton = false), so apps can coexist. WinDivert tests run in parallel too:
// each TestIps instance allocates a machine-wide-unique IP block, so every adapter's capture
// filter is disjoint and adapters never see each other's traffic. [DoNotParallelize] remains
// only for tests that mutate process-global state or are timing-sensitive.
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]