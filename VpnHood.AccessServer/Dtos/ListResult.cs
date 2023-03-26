using System.Collections.Generic;

namespace VpnHood.AccessServer.Dtos;

public class ListResult<T>
{
    public required long? TotalCount { get; set; }
    public required IEnumerable<T> Results { get; set; } 
}