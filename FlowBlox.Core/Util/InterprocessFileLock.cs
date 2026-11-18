using System.Security.Cryptography;
using System.Text;

namespace FlowBlox.Core.Util;

/// <summary>
/// Serializes short synchronous file-store transactions across desktop and server processes.
/// </summary>
internal sealed class InterprocessFileLock : IDisposable
{
    private readonly Mutex _mutex;

    private InterprocessFileLock(Mutex mutex)
    {
        _mutex = mutex;
    }

    public static InterprocessFileLock Acquire(string path)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));
        var mutex = new Mutex(false, (OperatingSystem.IsWindows() ? @"Global\" : string.Empty) + "FlowBlox.Store." + key);
        try
        {
            try
            {
                if (!mutex.WaitOne(TimeSpan.FromSeconds(15)))
                    throw new TimeoutException("Timed out waiting for the launcher file store.");
            }
            catch (AbandonedMutexException)
            {
                // The previous writer exited. Atomic replacement keeps the last complete file available.
            }
            return new InterprocessFileLock(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
