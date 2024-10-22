namespace VpnHood.Server.Abstractions;

public interface ISwapFileProvider
{
    Task<SwapFileInfo> GetInfo();
    Task SetAppSwapFileSize(long size);
}