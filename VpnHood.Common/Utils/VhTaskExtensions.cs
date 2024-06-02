namespace VpnHood.Common.Utils;

public static class VhTaskExtensions
{
    public static async Task VhConfigureAwait(this Task task)
    {
        await task.ConfigureAwait(false);
    }

    public static async Task<T> VhConfigureAwait<T>(this Task<T> task)
    {
        return await task.ConfigureAwait(false);
    }
}