namespace VpnHood.AccessServer.HostProviders.Ovh.Dto;

internal class CartData
{
    public required string CartId { get; set; }
    public required string Description { get; set; }
    public required DateTime Expire { get; set; }
    public int[]? Items { get; set; }
    public bool ReadOnly { get; set; }
}