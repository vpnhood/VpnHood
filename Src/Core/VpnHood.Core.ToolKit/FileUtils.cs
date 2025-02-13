namespace VpnHood.Core.ToolKit;

public static class FileUtils
{
    public static async Task WriteAllTextRetryAsync(string filePath, string content, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var startTime = FastDateTime.Now;
        while (true) {
            try {
                await File.WriteAllTextAsync(filePath, content, cancellationToken);
                return;
            }
            catch (IOException) when (FastDateTime.Now - startTime > timeout) {
                await Task.Delay(100, cancellationToken);
            }
        }
    }

    public static async Task<string> ReadAllTextAsync(string filePath, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var startTime = FastDateTime.Now;
        while (true) {
            try {
                return await File.ReadAllTextAsync(filePath, cancellationToken);
            }
            catch (IOException) when (FastDateTime.Now - startTime > timeout) {
                await Task.Delay(100, cancellationToken);
            }
        }
    }
}