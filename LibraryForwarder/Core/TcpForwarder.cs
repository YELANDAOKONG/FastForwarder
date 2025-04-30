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
    private readonly int _remoteConnectTimeout;
    
    private ITrafficLogger _trafficLogger;
    private ILogger _logger;
    
    
    public TcpForwarder(
        IPAddress localIp, 
        int localPort, 
        IPAddress remoteIp,
        int remotePort, 
        ITrafficLogger? trafficLogger = null,
        ILogger? logger = null,
        int bufferSize = 16384,
        int remoteConnectTimeout = 10000
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
        _remoteConnectTimeout = remoteConnectTimeout;
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
            _logger.Trace($"<Root> => ERROR START SERVICES: \n{e.StackTrace}");
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
            _logger.Trace($"<Root> => ERROR STOP SERVICES: \n{e.StackTrace}");
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
            _logger.Trace($"<Root> => ERROR DISPOSE SERVICES: \n{e.StackTrace}");
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
                    _logger.Info($"<Main> => Client connected from {((IPEndPoint)localSocket.RemoteEndPoint!).Address}");
                    WorkingThread(localSocket);
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
            _logger.Trace($"<Main> => ERROR MAIN THREAD: \n{e.StackTrace}");
        }
    }

    private async void WorkingThread(Socket localSocket)
    {
        try
        {
            if (_isWorking && !IsDisposed && !_cancellationTokenSource.IsCancellationRequested)
            {
                var remoteSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                remoteSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                remoteSocket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
                
                using var connectionTimeoutCts = new CancellationTokenSource(_remoteConnectTimeout);
                try
                {
                    await remoteSocket.ConnectAsync(RemoteIp, RemotePort, connectionTimeoutCts.Token);
                }
                catch (OperationCanceledException)
                {
                    _logger.Warn($"<Working> => Connect timeout: {RemoteIp}:{RemotePort}");
                    localSocket.Dispose();
                    remoteSocket.Dispose();
                    return;
                }
                catch (Exception e)
                {
                    _logger.Error($"<Working> => Remote connect error: {e.Message}");
                    localSocket.Dispose();
                    remoteSocket.Dispose();
                    return;
                }
                
                var remoteEndPoint = remoteSocket.RemoteEndPoint as IPEndPoint;
                if (remoteEndPoint == null)
                {
                    localSocket.Dispose();
                    remoteSocket.Dispose();
                    return;
                }
            
                if (!_isWorking || _cancellationTokenSource.IsCancellationRequested)
                {
                    localSocket.Dispose();
                    remoteSocket.Dispose();
                    return;
                }

                using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token);
                int random = Random.Shared.Next();
                var taskL2R = WorkingThreadLocalToRemote(localSocket, remoteSocket, random, connectionCts.Token);
                var taskR2L = WorkingThreadRemoteToLocal(localSocket, remoteSocket, random, connectionCts.Token);
                
                await Task.WhenAny(taskL2R, taskR2L);
                try { await connectionCts.CancelAsync(); } catch { /* Ignored */ }
                try {
                    await Task.WhenAll(taskL2R, taskR2L);
                } catch { /* Ignored */ }
        
                // Close sockets
                try { localSocket.Shutdown(SocketShutdown.Both); } catch { }
                try { remoteSocket.Shutdown(SocketShutdown.Both); } catch { }
                localSocket.Dispose();
                remoteSocket.Dispose();
                
                _logger.Info($"<Working> => Client disconnected from {(remoteEndPoint!).Address}");
            }
        }
        catch (Exception e)
        {
            _logger.Error($"<Working> => ERROR WORKING THREAD: {e.Message}");
            _logger.Trace($"<Working> => ERROR WORKING THREAD: \n{e.StackTrace}");
        }
    }

    private async Task WorkingThreadLocalToRemote(Socket localSocket, Socket remoteSocket, int random = -1, CancellationToken token = default)
    {
        
        byte[] buffer = _bufferPool.Rent(_bufferSize);
        try
        {
            while (_isWorking && !IsDisposed && !_cancellationTokenSource.IsCancellationRequested)
            {
                int bytesRead;
                try
                {
                    bytesRead = await localSocket.ReceiveAsync(new Memory<byte>(buffer), SocketFlags.None, token).ConfigureAwait(false);
                    if (bytesRead == 0)
                        break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    _logger.Debug($"<Working> => (LOCAL TO REMOTE) Local receive error: {e.Message}");
                    break;
                }
            
                try
                {
                    await remoteSocket.SendAsync(new ReadOnlyMemory<byte>(buffer, 0, bytesRead), SocketFlags.None, token).ConfigureAwait(false);
                
                    _trafficLogger.LogAsync(
                        (IPEndPoint)localSocket.RemoteEndPoint!,
                        (IPEndPoint)remoteSocket.RemoteEndPoint!,
                        random,
                        bytesRead,
                        true
                    );
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    _logger.Debug($"<Working> => (LOCAL TO REMOTE) Remote send error: {e.Message}");
                    break;
                }
            }
        }
        catch (Exception e)
        {
            _logger.Error($"<Working> => ERROR WORKING DATA THREAD (LOCAL TO REMOTE): {e.Message}");
            _logger.Trace($"<Working> => ERROR WORKING DATA THREAD (LOCAL TO REMOTE): \n{e.StackTrace}");
        }
        finally
        {
            _bufferPool.Return(buffer);
            _logger.Debug($"<Working> => Dispose remote socket for (LOCAL TO REMOTE THREAD)");
        }
    }
    
    private async Task WorkingThreadRemoteToLocal(Socket localSocket, Socket remoteSocket, int random = -1, CancellationToken token = default)
    {
        byte[] buffer = _bufferPool.Rent(_bufferSize);
        try
        {
            while (_isWorking && localSocket.Connected && remoteSocket.Connected && _isWorking && !IsDisposed)
            {
                int bytesRead;
                try
                {
                    bytesRead = await remoteSocket.ReceiveAsync(new Memory<byte>(buffer), SocketFlags.None, token).ConfigureAwait(false);
                    if (bytesRead == 0)
                        break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    _logger.Debug($"<Working> => (REMOTE TO LOCAL) Remote receive error: {e.Message}");
                    break;
                }
            
                try
                {
                    await localSocket.SendAsync(new ReadOnlyMemory<byte>(buffer, 0, bytesRead), SocketFlags.None, token).ConfigureAwait(false);
                
                    _trafficLogger.LogAsync(
                        (IPEndPoint)localSocket.RemoteEndPoint!,
                        (IPEndPoint)remoteSocket.RemoteEndPoint!,
                        random,
                        bytesRead,
                        false
                    );
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    _logger.Debug($"<Working> => (REMOTE TO LOCAL) Local send error: {e.Message}");
                    break;
                }
            }
        }
        catch (Exception e)
        {
            _logger.Error($"<Working> => ERROR WORKING DATA THREAD (REMOTE TO LOCAL): {e.Message}");
            _logger.Trace($"<Working> => ERROR WORKING DATA THREAD (REMOTE TO LOCAL): \n{e.StackTrace}");
        }
        finally
        {
            _bufferPool.Return(buffer);
            _logger.Debug($"<Working> => Dispose local socket (REMOTE TO LOCAL THREAD)");
        }
    }
}