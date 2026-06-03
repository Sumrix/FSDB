using System;
using System.Threading.Tasks;

namespace FSDB.Helpers;

public static class DisposeHelper
{
    public static void SafeDispose(IDisposable? disposable)
    {
        try
        {
            disposable?.Dispose();
        }
        catch
        {
            // Best effort
        }
    }

    public static async Task SafeDispose(IAsyncDisposable? disposable)
    {
        try
        {
            if (disposable != null)
                await disposable.DisposeAsync();
        }
        catch
        {
            // Best effort
        }
    }
}