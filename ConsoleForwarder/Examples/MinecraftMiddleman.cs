using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using ConsoleForwarder.Tools;
using LibraryForwarder.Core;

namespace ConsoleForwarder.Examples;

public class MinecraftMiddleman : ITcpMiddleman
{
    
    private readonly List<byte> _buffer = new List<byte>();
    private bool _handshakeProcessed = false;

    public byte[] Data(IPEndPoint from, IPEndPoint to, int random, int counter, bool isToRemote, byte[] data)
    {
        if (isToRemote && from.Address.ToString().Equals("127.0.0.1"))
        {
            _buffer.AddRange(data);
            
            if (_handshakeProcessed)
                return data;
            try
            {
                using MemoryStream stream = new MemoryStream(_buffer.ToArray());
                int initialPosition = (int)stream.Position;
                
                if (_buffer.Count < 1) return data;
                
                long startPos = stream.Position;
                
                int packetLength = StreamVarInt.Read(stream);
                int headerLength = (int)(stream.Position - startPos);
                
                if (_buffer.Count < headerLength + packetLength) 
                    return data; 
                
                stream.Position = startPos;

                packetLength = StreamVarInt.Read(stream);
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
                    
                    _handshakeProcessed = true;
                }
                
                int totalConsumed = headerLength + packetLength;
                if (_buffer.Count >= totalConsumed)
                {
                    _buffer.RemoveRange(0, totalConsumed);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing packet: {ex.Message}");
            }
        }
        
        return data;
    }
}
