using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LibraryForwarder.Core;

public class TcpForwarder : IDisposable
{
    public IPAddress LocalIp { get; }
    public int LocalPort { get; }
    public IPAddress RemoteIp { get; }
    public int RemotePort { get; }
    
    public bool IsDisposed = false;

    private Socket _localSocket;
    private bool _isWorking;
    private Task? _mainThread;
    private CancellationTokenSource _cancellationTokenSource = new();
    
    private readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;
    private readonly int _bufferSize;
    
    private ITrafficLogger _trafficLogger;
    private ILogger _logger;
    
    
    public TcpForwarder(
        IPAddress localIp, 
        int localPort, 
        IPAddress remoteIp,
        int remotePort, 
        ITrafficLogger? trafficLogger = null,
        ILogger? logger = null,
        int bufferSize = 16384
    )
    {
        LocalIp = localIp;
        LocalPort = localPort;
        RemoteIp = remoteIp;
        RemotePort = remotePort;

        _localSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _localSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _localSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        _localSocket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
        _localSocket.Bind(new IPEndPoint(LocalIp, LocalPort));

        _isWorking = false;
        _mainThread = null;
        
        _trafficLogger = trafficLogger ?? new SimpleTrafficLogger();
        _logger = logger ?? new SimpleLogger();
        _bufferSize = bufferSize;
    }

    public async void Start()
    {
        try
        {
            if (_isWorking)
            {
                return;
            }
            _isWorking = true;
            _cancellationTokenSource = new CancellationTokenSource();
            _localSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _localSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _localSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            _localSocket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
            _localSocket.Bind(new IPEndPoint(LocalIp, LocalPort));
            _localSocket.Listen();
            _mainThread = Task.Run(MainThread);
            await _mainThread;
        }
        catch (Exception e)
        {
            _logger.Error($"<Root> => ERROR START SERVICES: {e.Message}");
        }
    }

    public void Wait()
    {
        if (_isWorking)
        {
            _mainThread?.Wait();
        }
    }

    private async void Stop()
    {
        try
        {
            if (!_isWorking)
            {
                return;
            }
            _isWorking = false;
            await _cancellationTokenSource.CancelAsync();
            _localSocket?.Dispose();
        }
        catch (Exception e)
        {
            _logger.Error($"<Root> => ERROR STOP SERVICES: {e.Message}");
        }
    }
    
    public async void Dispose()
    {
        try
        {
            IsDisposed = true;
            await _cancellationTokenSource.CancelAsync();
            _isWorking = false;
            _localSocket.Dispose();
        }
        catch (Exception e)
        {
            _logger.Error($"<Root> => ERROR DISPOSE SERVICES: {e.Message}");
        }
    }
    
    public ITrafficLogger GetTrafficLogger()
    {
        return _trafficLogger;
    }

    private async void MainThread()
    {
        try
        {
            _logger.Info($"<Main> => Server started on {LocalIp}:{LocalPort}");
            while (_isWorking && !_cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    var localSocket = await _localSocket.AcceptAsync();
                    WorkingThread(localSocket);
                    _logger.Info($"<Main> => Client connected from {((IPEndPoint)localSocket.RemoteEndPoint!).Address}");
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }
        catch (Exception e)
        {
            _logger.Error($"<Main> => ERROR MAIN THREAD: {e.Message}");
        }
    }

    private async void WorkingThread(Socket localSocket)
    {
        try
        {
            var remoteSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            remoteSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            remoteSocket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
            await remoteSocket.ConnectAsync(RemoteIp, RemotePort);

            if (!_isWorking && !_cancellationTokenSource.IsCancellationRequested) return;
            int random = Random.Shared.Next();
            WorkingThreadLocalToRemote(localSocket, remoteSocket, random);
            WorkingThreadRemoteToLocal(localSocket, remoteSocket, random);
        }
        
        catch (Exception e)
        {
            _logger.Error($"<Working> => ERROR WORKING THREAD: {e.Message}");
        }
    }

    private async void WorkingThreadLocalToRemote(Socket localSocket, Socket remoteSocket, int random = -1)
    {
        byte[] buffer = _bufferPool.Rent(_bufferSize);
        try
        {
            while (_isWorking && !IsDisposed && !_cancellationTokenSource.IsCancellationRequested)
            {
                int bytesRead = await localSocket.ReceiveAsync(buffer);
                if (bytesRead == 0) return;
                if (!localSocket.Connected || !remoteSocket.Connected) return;
                try
                {
                    remoteSocket.Send(buffer, bytesRead, SocketFlags.None);
                }
                catch (SocketException e)
                {
                    return;
                }
                _trafficLogger.LogAsync(
                    (IPEndPoint)localSocket.RemoteEndPoint!,
                    (IPEndPoint)remoteSocket.RemoteEndPoint!,
                    random,
                    bytesRead,
                    true
                );
            }
        }
        catch (Exception e)
        {
            _logger.Error($"<Working> => ERROR WORKING DATA THREAD (LOCAL TO REMOTE): {e.Message}");
        }
        finally
        {
            remoteSocket.Dispose();
            _bufferPool.Return(buffer);
        }
    }
    
    private async void WorkingThreadRemoteToLocal(Socket localSocket, Socket remoteSocket, int random = -1)
    {
        byte[] buffer = _bufferPool.Rent(_bufferSize);
        try
        {
            while (_isWorking && localSocket.Connected && remoteSocket.Connected && _isWorking && !IsDisposed)
            {
                int bytesRead = await remoteSocket.ReceiveAsync(buffer);
                if (bytesRead == 0) return;
                if (!localSocket.Connected || !remoteSocket.Connected) return;
                try
                {
                    localSocket.Send(buffer, bytesRead, SocketFlags.None);
                }
                catch (SocketException e)
                {
                    return;
                }
                _trafficLogger.LogAsync(
                    (IPEndPoint)localSocket.RemoteEndPoint!,
                    (IPEndPoint)remoteSocket.RemoteEndPoint!,
                    random,
                    bytesRead,
                    false
                );
            }
        }
        catch (Exception e)
        {
            _logger.Error($"<Working> => ERROR WORKING DATA THREAD (REMOTE TO LOCAL): {e.Message}");
        }
        finally
        {
            localSocket.Dispose();
            _bufferPool.Return(buffer);
        }
    }
}