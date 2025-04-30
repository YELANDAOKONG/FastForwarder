using System.Net;
using LibraryForwarder.Core;

namespace ConsoleForwarder.Examples;

public class MinecraftMiddleman : ITcpMiddleman
{
    public byte[] Data(IPEndPoint from, IPEndPoint to, int random, int counter, bool isToRemote, byte[] data)
    {
        throw new NotImplementedException();
    }
}