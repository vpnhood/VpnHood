namespace VpnHood.Core.ToolKit;

public static class FileUtils
{
    public static async Task WriteAllTextRetryAsync(string filePath, string content, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        // don't use fast date time, it's not accurate enough
        var startTime = DateTime.Now;
        while (true) {
            try {
                await File.WriteAllTextAsync(filePath, content, cancellationToken);
                return;
            }
            catch (IOException) when (DateTime.Now - startTime < timeout) {
                await Task.Delay(100, cancellationToken);
            }
        }
    }

    public static async Task<string> ReadAllTextAsync(string filePath, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        // don't use fast date time, it's not accurate enough
        var startTime = DateTime.Now;
        while (true) {
            try {
                await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new StreamReader(stream);
                var content = await reader.ReadToEndAsync();
                return content;
            }
            catch (IOException) when (DateTime.Now - startTime < timeout) {
                await Task.Delay(100, cancellationToken);
            }
        }
    }
}