using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Dtos;

public class AccessData
{
    public Access Access { get; set; } = default!;
    public AccessStatus AccessStatus { get; set; }

    public Usage? Usage { get; set; } = new ();
}