namespace VpnHood.App.Server.Providers.Linux;

// a line of `swapon --show --bytes`, in bytes
internal readonly record struct LinuxSwapFile(string FilePath, long Size, long Used);
