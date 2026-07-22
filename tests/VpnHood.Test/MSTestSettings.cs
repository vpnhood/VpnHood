// Run tests in parallel. WinDivert tests run in parallel too: WinDivert is a machine-wide
// kernel driver, but each TestIps instance allocates a machine-wide-unique IP block, so every
// adapter's capture filter is disjoint and adapters never see each other's traffic — within
// this host or across the test-assembly hosts that Visual Studio runs in parallel processes.
// [DoNotParallelize] remains only for tests that mutate process-global state or are
// timing-sensitive.
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]
