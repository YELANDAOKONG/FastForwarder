using System.Net.Sockets;

namespace LibraryForwarder.Core.Center.Models;

public record NodeGroup
{
    public byte[] SessionId = [];
    public required Socket Provider;
    public List<Socket> Visitors = new();
}