# Notices for original skills in this module

`emitOwnSkills` copies only directories out of `skills/<module>/`, so this file stays in the
repository and never ships. It records where an original skill's material came from when that is
worth knowing.

## writing-tunit-tests

The topic outline — setup, test shape, assertions, data-driven cases, lifecycle, parallelism,
control attributes, migration table — was suggested by `skills/csharp-tunit` in
[github/awesome-copilot](https://github.com/github/awesome-copilot) (MIT).

No text was taken from it, and that turned out to matter. Checking its claims against the TUnit
documentation found six that would have produced code that does not compile or does not mean what it
says: `[MethodData]` for `[MethodDataSource]`; `[ClassData]` for `[ClassDataSource<T>]`, which is a
shared fixture rather than a row provider; a non-existent `ITestDataSource`, which is an MSTest
type; `[ParallelLimit<T>]` for `[ParallelLimiter<T>]`; a fluent `ThrowsAsync<T>()` that does not
exist; and the claim that tests in one class run sequentially, which is an xUnit assumption and the
opposite of what TUnit does.

The content here is written from <https://tunit.dev/docs>, with the framework comparisons and
migration mapping from that site's own comparison and migration pages.
