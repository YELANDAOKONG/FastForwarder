using System.Net;

namespace LibraryForwarder.Core;

public class NoneTcpMiddleman : ITcpMiddleman
{
    public byte[] Data(IPEndPoint from, IPEndPoint to, int random, int counter, bool isToRemote, byte[] data)
    {
        return data;
    }
}