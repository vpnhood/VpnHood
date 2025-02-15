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
                await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new StreamReader(stream);
                var content = await reader.ReadToEndAsync();
                return content;
            }
            catch (IOException) when (FastDateTime.Now - startTime > timeout) {
                await Task.Delay(100, cancellationToken);
            }
        }
    }
}