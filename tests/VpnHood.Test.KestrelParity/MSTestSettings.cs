// One test at a time: each runs its probes side by side against the same two servers, and a second test's probes
// beside them would shift the timings both are measured by.
[assembly: DoNotParallelize]
