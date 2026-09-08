namespace SkuMaster.Infrastructure;

internal sealed class SharedInputFile : IDisposable
{
    public FileStream Stream { get; }
    private bool disposed;
    public SharedInputFile(string path)
    {
        Stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        try
        {
            // Keep existing editor handles open, but protect the short snapshot read
            // from overlapping ordinary file writes, including same-length writes.
            if (OperatingSystem.IsWindows()) Stream.Lock(0, long.MaxValue);
        }
        catch { Stream.Dispose(); throw; }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { if (OperatingSystem.IsWindows()) Stream.Unlock(0, long.MaxValue); }
        finally { Stream.Dispose(); }
    }
}
