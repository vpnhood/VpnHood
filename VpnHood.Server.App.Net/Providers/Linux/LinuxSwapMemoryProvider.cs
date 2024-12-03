using Microsoft.Extensions.Logging;
using VpnHood.Common.Utils;
using VpnHood.Server.Abstractions;
// ReSharper disable StringLiteralTypo

namespace VpnHood.Server.App.Providers.Linux;

public class LinuxSwapMemoryProvider(ILogger logger)
    : ISwapMemoryProvider
{
    // ReSharper disable once NotAccessedPositionalProperty.Local
    private record struct SwapFile(string FilePath, long Size, long Used);
    private const string SwapFilePath = "/vpnhood.swap";

    public async Task<SwapMemoryInfo> GetInfo()
    {
        var swapFiles = await ListCurrentSwapFiles();
        return new SwapMemoryInfo {
            TotalSize = swapFiles.Sum(x => x.Size),
            TotalUsed = swapFiles.Sum(x => x.Used),
            AppSize = swapFiles.Where(x => x.FilePath == SwapFilePath).Sum(x => x.Size),
            AppUsed = swapFiles.Where(x => x.FilePath == SwapFilePath).Sum(x => x.Used),
        };
    }

    public async Task SetAppSwapMemorySize(long size)
    {
        logger.LogInformation("Configuring swap file. File: {SwapFilePath}, Size: {Size}.", SwapFilePath, VhUtil.FormatBytes(size));

        // Disable the current swap file
        if (File.Exists($"{SwapFilePath}")) {
            logger.LogInformation("Disabling the current swap file.");

            // it may raise exception if the swap file is not active
            try {
                var swapFiles = await ListCurrentSwapFiles();
                if (swapFiles.Any(x => x.FilePath == SwapFilePath))
                    await LinuxUtils.ExecuteCommandAsync($"swapoff {SwapFilePath}");
            }
            catch (Exception ex) {
                logger.LogWarning(ex, "Failed to disable the current swap file.");
            }

            // Delete the existing swap file
            await LinuxUtils.ExecuteCommandAsync($"rm {SwapFilePath}");
        }

        // ignore if size is zero
        if (size < 1_000_000) {
            logger.LogInformation("No swap file size specified. Skipping the creation of a new swap file.");
            return;
        }

        // Create a new swap file with the specified size
        logger.LogInformation("Creating a new swap file.");
        await LinuxUtils.ExecuteCommandAsync($"fallocate -l {size} {SwapFilePath}");

        // Set the correct permissions for the new swap file
        logger.LogInformation("Setting the correct permissions for the new swap file.");
        await LinuxUtils.ExecuteCommandAsync($"chmod 600 /{SwapFilePath}");

        // Format the file as swap space
        logger.LogInformation("Formatting the file as swap space.");
        await LinuxUtils.ExecuteCommandAsync($"mkswap {SwapFilePath}");

        // Enable the swap file
        logger.LogInformation("Enabling the swap file.");
        await LinuxUtils.ExecuteCommandAsync($"swapon {SwapFilePath}");

        // Let ignore the following and activate the swap file everytime after reboot
        // Ensure the swap file is listed in /etc/fstab for persistence after reboot
        //var fstabContent = await LinuxUtils.ExecuteCommandAsync("cat /etc/fstab");
        //if (!fstabContent.Contains($"{SwapFilePath}")) {
        //    logger.LogInformation("Adding the swap file to /etc/fstab for persistence after reboot.");
        //    await LinuxUtils.ExecuteCommandAsync($"echo '{SwapFilePath} none swap sw 0 0' | tee -a /etc/fstab");
        //}
    }

    private static SwapFile? ParseSwapFileLine(string line)
    {
        // ReSharper disable once CommentTypo
        // Expected output line format: "NAME      TYPE      SIZE   USED  PRIO"
        var columns = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (columns.Length < 4 ||
            !long.TryParse(columns[2], out var sizeInBytes) ||
            !long.TryParse(columns[3], out var usedInBytes))
            return null;

        // Return the FileSize struct with both size and used space
        return new SwapFile {
            FilePath = columns[0],
            Size = sizeInBytes,
            Used = usedInBytes
        };
    }

    private static async Task<List<SwapFile>> ListCurrentSwapFiles()
    {
        var swapFiles = new List<SwapFile>();

        // ReSharper disable StringLiteralTypo
        var output = await LinuxUtils.ExecuteCommandAsync("swapon --show --bytes");
        var lines = output.Split(['\n'], StringSplitOptions.RemoveEmptyEntries);

        // Skip the header row and parse the lines
        foreach (var line in lines) {
            if (line.StartsWith("NAME")) continue; // Skip the header
            var swapFile = ParseSwapFileLine(line);
            if (swapFile != null) {
                swapFiles.Add(swapFile.Value);
            }
        }

        return swapFiles;
    }
}
