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

    private Socket _localSocket;
    private List<Socket> _remoteSockets;
    private bool _isWorking;
    private Task? _mainThread;
    private List<Task> _workingThreads;
    private readonly object _threadLock = new();
    
    
    private readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;
    private readonly int _bufferSize;
    
    private bool _isDisposed = false;
    private ITrafficLogger _trafficLogger;
    
    
    public TcpForwarder(
        IPAddress localIp, 
        int localPort, 
        IPAddress remoteIp,
        int remotePort, 
        ITrafficLogger? trafficLogger = null,
        int bufferSize = 8192
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
        _remoteSockets = new List<Socket>();

        _isWorking = false;
        _mainThread = null;
        _workingThreads = new List<Task>();
        
        _trafficLogger = trafficLogger ?? new SimpleTrafficLogger();
        _bufferSize = bufferSize;
    }

    public void Start()
    {
        lock (_threadLock)
        {
            _localSocket.Listen(100);
            _mainThread = Task.Run(MainThread);
            _isWorking = true;
        }
    }

    public void Wait()
    {
        if (_isWorking)
        {
            _mainThread?.Wait();
        }
    }

    private void Stop()
    {
        lock (_threadLock)
        {
            _isWorking = false;
            _mainThread?.Dispose();
            foreach (var workingThread in _workingThreads)
            {
                workingThread.Dispose();
            }
        }
    }
    
    public void Dispose()
    {
        _isDisposed = true;
        Stop();
        _localSocket.Dispose();
        _remoteSockets.ForEach(
            socket =>
            {
                socket.Dispose();
            }
        );
    }
    
    public bool IsDisposed()
    {
        return _isDisposed;
    }
    
    public ITrafficLogger GetTrafficLogger()
    {
        return _trafficLogger;
    }

    private void MainThread()
    {
        while (_isWorking)
        {
            try
            {
                var localSocket = _localSocket.Accept();
                lock (_threadLock)
                {
                    _remoteSockets.Add(localSocket);
                    _workingThreads.Add(Task.Run(() => WorkingThread(localSocket)));
                }
                Console.WriteLine($"[INFO] ({localSocket.AddressFamily}): New connection from {localSocket.RemoteEndPoint}");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }

    private void WorkingThread(Socket localSocket)
    {
        try
        {
            var remoteSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            remoteSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            remoteSocket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
            remoteSocket.Connect(RemoteIp, RemotePort);
            lock (_threadLock)
            {
                _remoteSockets.Add(remoteSocket);
            }

            if (_isWorking)
            {
                int random = Random.Shared.Next();
                var local2RemoteTask = Task.Run(() => WorkingThreadLocalToRemote(localSocket, remoteSocket, random));
                var remote2LocalTask = Task.Run(() => WorkingThreadRemoteToLocal(localSocket, remoteSocket, random));
                
                lock (_threadLock)
                {
                    _workingThreads.Add(local2RemoteTask);
                    _workingThreads.Add(remote2LocalTask);
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[ERROR] ({localSocket.AddressFamily}): {e.Message}");
            return;
        }
    }

    private void WorkingThreadLocalToRemote(Socket localSocket, Socket remoteSocket, int random = -1)
    {
        byte[] buffer = _bufferPool.Rent(_bufferSize);
        try
        {
            while (_isWorking && localSocket.Connected && remoteSocket.Connected && _isWorking && !_isDisposed)
            {
                int bytesRead = localSocket.Receive(buffer);
                if (bytesRead > 0)
                {
                    remoteSocket.Send(buffer, bytesRead, SocketFlags.None);
                    _trafficLogger.LogAsync(
                        (IPEndPoint)localSocket.RemoteEndPoint!,
                        (IPEndPoint)remoteSocket.RemoteEndPoint!,
                        random,
                        bytesRead,
                        true
                    );
                }
            }
        }
        finally
        {
            _bufferPool.Return(buffer);
        }
    }
    
    private void WorkingThreadRemoteToLocal(Socket localSocket, Socket remoteSocket, int random = -1)
    {
        byte[] buffer = _bufferPool.Rent(_bufferSize);
        try
        {
            while (_isWorking && localSocket.Connected && remoteSocket.Connected && _isWorking && !_isDisposed)
            {
                int bytesRead = remoteSocket.Receive(buffer);
                if (bytesRead > 0)
                {
                    localSocket.Send(buffer, bytesRead, SocketFlags.None);
                    _trafficLogger.LogAsync(
                        (IPEndPoint)localSocket.RemoteEndPoint!,
                        (IPEndPoint)remoteSocket.RemoteEndPoint!,
                        random,
                        bytesRead,
                        false
                    );
                }
            }
        }
        finally
        {
            _bufferPool.Return(buffer);
        }
    }
    
    
    
    
}