using System.Net;
using System.Net.Sockets;
using LibraryForwarder.Core.Center.Models;

namespace LibraryForwarder.Core.Center;

public class CenterServer : IDisposable
{
    public IPAddress ServerAddress;
    public int ServerPort;

    public bool IsDisposed = false;
    public Dictionary<byte[], NodeGroup> Groups = new();
    public ReaderWriterLockSlim GroupsLock = new();

    private ILogger _logger;
    private ITrafficLogger _trafficLogger;
    private Socket _socket;
    private bool _isWorking = false;
    
    private Task? _mainTask = null;
    private CancellationTokenSource _cancellationToken = new CancellationTokenSource();
    
    public CenterServer(IPAddress address, int port, ILogger? logger = null, ITrafficLogger? trafficLogger = null)
    {
        ServerAddress = address;
        ServerPort = port;
        
        _logger = logger ?? new SimpleLogger("Center", true);
        _trafficLogger = trafficLogger ?? new SimpleTrafficLogger();
        
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        _socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
        _socket.Bind(new IPEndPoint(ServerAddress, ServerPort));
    }

    public async void Start()
    {
        try
        {
            if (_isWorking)
            {
                return;
            }
            _cancellationToken = new CancellationTokenSource();
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            _socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
            _socket.Bind(new IPEndPoint(ServerAddress, ServerPort));
            _socket.Listen();
            _mainTask = Task.Run(MainThread);
            await _mainTask;
        }
        catch (Exception e)
        {
            _logger.Error($"<Root> => ERROR START SERVICES: {e.Message}");
            _logger.Trace($"<Root> => ERROR START SERVICES: {e.StackTrace}");
        }
    }

    public async void Stop()
    {
        try
        {
            if (!_isWorking)
            {
                return;
            }
            await _cancellationToken.CancelAsync();
            _isWorking = false;
            _socket.Close();
            _socket.Dispose();
        }
        catch (Exception e)
        {
            _logger.Error($"<Root> => ERROR STOP SERVICES: {e.Message}");
            _logger.Trace($"<Root> => ERROR STOP SERVICES: {e.StackTrace}");
        }
    }
    
    public async void Dispose()
    {
        try
        {
            IsDisposed = true;
            await _cancellationToken.CancelAsync();
            _isWorking = false;
            _socket.Dispose();
        }
        catch (Exception e)
        {
            _logger.Error($"<Root> => ERROR DISPOSE SERVICES: {e.Message}");
            _logger.Trace($"<Root> => ERROR DISPOSE SERVICES: {e.StackTrace}");
        }
    }


    public async void MainThread()
    {
        try
        {
            while (_isWorking && !_cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var clientSocket = await _socket.AcceptAsync();
                    _logger.Info($"<Main> => New connection from {clientSocket.RemoteEndPoint}");
                    WorkingThread(clientSocket);
                    continue;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            _logger.Error($"<Main> => ERROR MAIN THREAD: {e.Message}");
            _logger.Trace($"<Main> => ERROR MAIN THREAD: {e.StackTrace}");
        }
    }

    private async void WorkingThread(Socket clientSocket)
    {
        try
        {
            int random = Random.Shared.Next();
            await using var stream = new NetworkStream(clientSocket, ownsSocket: false);

            byte[] initStatusCode = new byte[4];
            var byteRead = await stream.ReadAsync(initStatusCode, 0, initStatusCode.Length);
            if (byteRead == 0 && !_cancellationToken.IsCancellationRequested) { return; }
            
            while (_isWorking && !_cancellationToken.IsCancellationRequested)
            {
                
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            _logger.Error($"<Working> => ERROR WORKING THREAD: {e.Message}");
            _logger.Trace($"<Working> => ERROR WORKING THREAD: {e.StackTrace}");
        }
        finally
        {
            try { clientSocket.Close(); } catch { }
        }
    }

    public async void WorkingThreadNodeToNode()
    {
        
    }
}