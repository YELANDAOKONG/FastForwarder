using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LibraryForwarder.Core;

public class TcpForwarder : IDisposable
{
    public IPAddress LocalIp { get; set; }
    public int LocalPort { get; set; }
    public IPAddress RemoteIp { get; set; }
    public int RemotePort { get; set; }

    private Socket _localSocket;
    private List<Socket> _remoteSockets;
    private bool _isWorking;
    private Task? _mainThread;
    private List<Task> _workingThreads;
    private readonly object _threadLock = new();

    private bool _isDisposed = false;
    
    public TcpForwarder(IPAddress localIp, int localPort, IPAddress remoteIp, int remotePort)
    {
        LocalIp = localIp;
        LocalPort = localPort;
        RemoteIp = remoteIp;
        RemotePort = remotePort;

        _localSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _localSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _localSocket.Bind(new IPEndPoint(LocalIp, LocalPort));
        _remoteSockets = new List<Socket>();

        _isWorking = false;
        _mainThread = null;
        _workingThreads = new List<Task>();
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
            remoteSocket.Connect(RemoteIp, RemotePort);
            lock (_threadLock)
            {
                _remoteSockets.Add(remoteSocket);
            }

            if (_isWorking)
            {
                var local2RemoteTask = Task.Run(() => WorkingThreadLocalToRemote(localSocket, remoteSocket));
                var remote2LocalTask = Task.Run(() => WorkingThreadRemoteToLocal(localSocket, remoteSocket));
                
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

    private void WorkingThreadLocalToRemote(Socket localSocket, Socket remoteSocket)
    {
        while (_isWorking && localSocket.Connected && remoteSocket.Connected && _isWorking && !_isDisposed)
        {
            byte[] bufferL2R = new byte[8192];
            int bytesReadL2R = localSocket.Receive(bufferL2R);
            if (bytesReadL2R > 0)
            {
                remoteSocket.Send(bufferL2R, bytesReadL2R, SocketFlags.None);
                Console.WriteLine($"[DATA] ({localSocket.AddressFamily} -> {remoteSocket.AddressFamily}): {bufferL2R.Length}");
            }
        }
    }
    
    private void WorkingThreadRemoteToLocal(Socket localSocket, Socket remoteSocket)
    {
        while (_isWorking && localSocket.Connected && remoteSocket.Connected && _isWorking && !_isDisposed)
        {
            byte[] bufferR2L = new byte[8192];
            int bytesReadR2L = remoteSocket.Receive(bufferR2L);
            if (bytesReadR2L > 0)
            {
                localSocket.Send(bufferR2L, bytesReadR2L, SocketFlags.None);
                Console.WriteLine($"[DATA] ({remoteSocket.AddressFamily} -> {localSocket.AddressFamily}): {bufferR2L.Length}");
            }
        }
    }
    
    
    
    
}