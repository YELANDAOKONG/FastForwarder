using System.Net;
using System.Net.Sockets;
using LibraryForwarder.Core.Center.Models;

namespace LibraryForwarder.Core.Center;

public class CenterServer
{
    public IPAddress ServerAddress;
    public int ServerPort;
    
    public Dictionary<byte[], NodeGroup> Groups = new();
    public ReaderWriterLockSlim GroupsLock = new();

    private ILogger _logger;
    private Socket _socket;
    private bool _isWorking = false;
    
    public CenterServer(IPAddress address, int port, ILogger? logger = null)
    {
        ServerAddress = address;
        ServerPort = port;
        
        
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        _socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
        _socket.Bind(new IPEndPoint(ServerAddress, ServerPort));
    }



    public async void MainThread()
    {
        while (_isWorking)
        {
            try
            {
                var clientSocket = await _socket.AcceptAsync();
                Console.WriteLine($"[INFO] ({clientSocket.AddressFamily}): New connection from {clientSocket.RemoteEndPoint}");
                await Task.Run(() => WorkingThread(clientSocket));
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    }

    public async void WorkingThread(Socket clientSocket)
    {
        
    }

    public async void WorkingThreadNodeToNode()
    {
        
    }
}