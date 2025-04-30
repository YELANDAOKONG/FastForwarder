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
            
            int packetLength = StreamVarInt.Read(stream);
            Console.WriteLine($"Packet Length: {packetLength}");
            
            int packetId = StreamVarInt.Read(stream);
            Console.WriteLine($"Packet ID: {packetId}");
            
            if (packetId == 0) // Handshake
            {
                int version = StreamVarInt.Read(stream);
                Console.WriteLine($"Protocol Version: {version}");
                
                string host = StreamVarString.Read(stream);
                Console.WriteLine($"Server Address: {host}");
                
                byte[] portBytes = new byte[2];
                stream.Read(portBytes, 0, 2);
                int port = (portBytes[0] << 8) | portBytes[1];
                Console.WriteLine($"Server Port: {port}");
                
                int nextState = StreamVarInt.Read(stream);
                Console.WriteLine($"Next State: {nextState}");
            }
        }
        return data;
    }

}