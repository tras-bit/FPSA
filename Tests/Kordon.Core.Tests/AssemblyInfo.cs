using Xunit;

// The suites share one loaded GameDatabase (and therefore one set of definition
// objects). Several tests build kits, mutate match configs and advance matches, so
// running them in parallel lets one suite observe another's state - which showed up
// as impossible recoil values and flaky network config results. The rules are
// deterministic by design; the test run has to be too.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
