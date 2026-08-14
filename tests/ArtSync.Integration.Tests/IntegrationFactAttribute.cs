namespace ArtSync.Integration.Tests;

/// <summary>
/// Runs the test only when ARTSYNC_INTEGRATION=true is set.
/// Automatically skips (with a clear message) in all other environments,
/// so the unit-test suite stays green without a database.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class IntegrationFactAttribute : Xunit.FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (!TestEnvironment.IsEnabled)
            Skip = TestEnvironment.SkipReason;
    }
}
