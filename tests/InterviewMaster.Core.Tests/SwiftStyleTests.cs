namespace InterviewMaster.Tests;

/// The Swift tests are scripts of `assert(condition, message)` lines that keep going after a failure
/// and report every one. `Check` does the same inside a test; the test fails at its end with all of them.
public abstract class SwiftStyleTests : IDisposable
{
    private readonly List<string> failures = new();

    protected void Check(bool condition, string message)
    {
        if (!condition) failures.Add(message);
    }

    public virtual void Dispose()
    {
        if (failures.Count > 0)
        {
            throw new Xunit.Sdk.XunitException($"{failures.Count} failed:\n  " + string.Join("\n  ", failures));
        }
    }
}
