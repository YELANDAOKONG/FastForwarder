using System.Net.Sockets;

namespace LibraryForwarder.Core.Center.Models;

public class NodeGroup
{
    public required byte[] SessionId { get; set; }
    public required Socket Provider { get; set; }
    public List<Socket> Visitors { get; } = new();
}
