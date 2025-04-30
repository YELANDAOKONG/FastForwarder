using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using ConsoleForwarder.Tools;
using LibraryForwarder.Core;

namespace ConsoleForwarder.Examples;

public class MinecraftMiddleman : ITcpMiddleman
{
    public byte[] Data(IPEndPoint from, IPEndPoint to, int random, int counter, bool isToRemote, byte[] data)
    {
        if (counter == 0 && isToRemote == true && from.Address.ToString().Equals("127.0.0.1"))
        {
            MemoryStream stream = new MemoryStream(data);
            int version = StreamVarInt.Read(stream);
            string host = StreamVarString.Read(stream);
            Console.WriteLine(version);
            Console.WriteLine(host);
            
        }
        return data;
    }
}