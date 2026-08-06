using HaelpMi.Core.Storage;

namespace HaelpMi.Core.Tests;

/// <summary>Redirects AppPaths to a throwaway temp folder for the lifetime of a test, then cleans up.</summary>
internal sealed class TestAppDataScope : IDisposable
{
    private readonly string _tempFolder;
    private readonly IDisposable _restore;

    public TestAppDataScope()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "HaelpMiTests_" + Guid.NewGuid().ToString("N"));
        _restore = AppPaths.UseRootForTests(_tempFolder);
    }

    public void Dispose()
    {
        _restore.Dispose();
        try
        {
            if (Directory.Exists(_tempFolder))
            {
                Directory.Delete(_tempFolder, recursive: true);
            }
        }
        catch (IOException)
        {
            // best-effort cleanup only
        }
    }
}
