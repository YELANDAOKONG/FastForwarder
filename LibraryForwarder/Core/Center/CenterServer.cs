using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using LibraryForwarder.Core.Center.Models;

namespace LibraryForwarder.Core.Center;

public class CenterServer : IDisposable
{
    public IPAddress ServerAddress { get; }
    public int ServerPort { get; }

    public bool IsDisposed = false;
    private readonly Dictionary<string, NodeGroup> _sessionGroups = new();
    private readonly ReaderWriterLockSlim _groupsLock = new();

    private readonly ILogger _logger;
    private readonly ITrafficLogger _trafficLogger;
    private Socket _serverSocket;
    private bool _isWorking = false;
    private readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;
    private readonly int _bufferSize;
    
    private Task? _mainTask = null;
    private CancellationTokenSource _cancellationTokenSource = new();
    
    public CenterServer(
        IPAddress address, 
        int port, 
        ILogger? logger = null, 
        ITrafficLogger? trafficLogger = null,
        int bufferSize = 16384)
    {
        ServerAddress = address;
        ServerPort = port;
        
        _logger = logger ?? new SimpleLogger("Center", true);
        _trafficLogger = trafficLogger ?? new SimpleTrafficLogger();
        _bufferSize = bufferSize;
        
        _serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _serverSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _serverSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        _serverSocket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
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
            
            _serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _serverSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _serverSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            _serverSocket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
            _serverSocket.Bind(new IPEndPoint(ServerAddress, ServerPort));
            _serverSocket.Listen(100); // Allow a queue of up to 100 pending connections
            
            _logger.Info($"<Main> => Center Server started on {ServerAddress}:{ServerPort}");
            
            _mainTask = Task.Run(MainThread);
            await _mainTask;
        }
        catch (Exception e)
        {
            _logger.Error($"<Root> => ERROR STARTING CENTER SERVER: {e.Message}");
            _logger.Trace($"<Root> => ERROR STARTING CENTER SERVER: \n{e.StackTrace}");
        }
    }

    public void Wait()
    {
        if (_isWorking)
        {
            _mainTask?.Wait();
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
            
            _isWorking = false;
            await _cancellationTokenSource.CancelAsync();
            
            // Close all connections
            _groupsLock.EnterWriteLock();
            try 
            {
                foreach (var group in _sessionGroups.Values)
                {
                    try { group.Provider?.Close(); } catch { /* Ignored */ }
                    foreach (var visitor in group.Visitors)
                    {
                        try { visitor?.Close(); } catch { /* Ignored */ }
                    }
                }
                _sessionGroups.Clear();
            }
            finally
            {
                _groupsLock.ExitWriteLock();
            }
            
            _serverSocket.Close();
            _serverSocket.Dispose();
            
            _logger.Info("<Main> => Center Server stopped");
        }
        catch (Exception e)
        {
            _logger.Error($"<Root> => ERROR STOPPING CENTER SERVER: {e.Message}");
            _logger.Trace($"<Root> => ERROR STOPPING CENTER SERVER: \n{e.StackTrace}");
        }
    }
    
    public async void Dispose()
    {
        try
        {
            IsDisposed = true;
            await _cancellationTokenSource.CancelAsync();
            _isWorking = false;
            
            // Clean up all connections
            _groupsLock.EnterWriteLock();
            try 
            {
                foreach (var group in _sessionGroups.Values)
                {
                    try { group.Provider?.Dispose(); } catch { /* Ignored */ }
                    foreach (var visitor in group.Visitors)
                    {
                        try { visitor?.Dispose(); } catch { /* Ignored */ }
                    }
                }
                _sessionGroups.Clear();
            }
            finally
            {
                _groupsLock.ExitWriteLock();
            }
            
            _serverSocket.Dispose();
            _groupsLock.Dispose();
            
            _logger.Info("<Main> => Center Server disposed");
        }
        catch (Exception e)
        {
            _logger.Error($"<Root> => ERROR DISPOSING CENTER SERVER: {e.Message}");
            _logger.Trace($"<Root> => ERROR DISPOSING CENTER SERVER: \n{e.StackTrace}");
        }
    }

    private async void MainThread()
    {
        try
        {
            while (_isWorking && !_cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    var clientSocket = await _serverSocket.AcceptAsync(_cancellationTokenSource.Token);
                    
                    // Configure socket for high performance
                    clientSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                    clientSocket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
                    
                    _logger.Info($"<Main> => New connection from {((IPEndPoint)clientSocket.RemoteEndPoint!).Address}:{((IPEndPoint)clientSocket.RemoteEndPoint!).Port}");
                    
                    // Handle the connection in a separate task
                    Task.Run(() => HandleNewConnection(clientSocket));
                }
                catch (OperationCanceledException)
                {
                    break; // Normal cancellation, exit the loop
                }
                catch (Exception e)
                {
                    _logger.Error($"<Main> => Error accepting client connection: {e.Message}");
                    _logger.Trace($"<Main> => {e.StackTrace}");
                    
                    // Short delay to avoid CPU spinning on repeated errors
                    await Task.Delay(100);
                }
            }
        }
        catch (Exception e)
        {
            _logger.Error($"<Main> => CENTER SERVER MAIN THREAD ERROR: {e.Message}");
            _logger.Trace($"<Main> => CENTER SERVER MAIN THREAD ERROR: \n{e.StackTrace}");
        }
        finally
        {
            _logger.Info("<Main> => Center Server main thread stopped");
        }
    }

    private async void HandleNewConnection(Socket clientSocket)
    {
        // Buffer for receiving the initial request type
        var requestBuffer = _bufferPool.Rent(4);
        
        try
        {
            // Read the request type (4 bytes for int enum)
            int bytesRead = await clientSocket.ReceiveAsync(requestBuffer, SocketFlags.None);
            if (bytesRead != 4)
            {
                _logger.Warn($"<Handler> => Invalid request format from {((IPEndPoint)clientSocket.RemoteEndPoint!).Address}");
                return;
            }
            
            ClientRequests requestType = (ClientRequests)BitConverter.ToInt32(requestBuffer, 0);
            _logger.Debug($"<Handler> => Received request: {requestType} from {((IPEndPoint)clientSocket.RemoteEndPoint!).Address}");
            
            switch (requestType)
            {
                case ClientRequests.CreateService:
                    await HandleProviderConnection(clientSocket);
                    break;
                    
                case ClientRequests.ConnectService:
                    await HandleVisitorConnection(clientSocket);
                    break;
                    
                default:
                    _logger.Warn($"<Handler> => Unknown request type: {requestType}");
                    clientSocket.Close();
                    break;
            }
        }
        catch (Exception e)
        {
            _logger.Error($"<Handler> => Error handling client connection: {e.Message}");
            _logger.Trace($"<Handler> => {e.StackTrace}");
            
            try { clientSocket.Close(); } catch { /* Ignored */ }
        }
        finally
        {
            _bufferPool.Return(requestBuffer);
        }
    }

    private async Task HandleProviderConnection(Socket providerSocket)
    {
        // Create a new 128-byte SessionId
        byte[] sessionId = new byte[128];
        Random.Shared.NextBytes(sessionId);
        string sessionIdKey = Convert.ToBase64String(sessionId);
        
        try
        {
            // Create a new node group with this provider
            var nodeGroup = new NodeGroup
            {
                SessionId = sessionId,
                Provider = providerSocket
            };
            
            // Add to collection
            _groupsLock.EnterWriteLock();
            try
            {
                _sessionGroups[sessionIdKey] = nodeGroup;
            }
            finally
            {
                _groupsLock.ExitWriteLock();
            }
            
            // Send the SessionId to the provider as confirmation
            await providerSocket.SendAsync(sessionId, SocketFlags.None);
            
            _logger.Info($"<Provider> => New service created with SessionId: {BitConverter.ToString(sessionId, 0, 8)}...");
            
            // Keep the connection alive and wait for termination
            var terminationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token);
            var keepAliveBuffer = new byte[1];
            
            try
            {
                // Simple heartbeat - if the socket gets disconnected this will throw
                while (!terminationTokenSource.Token.IsCancellationRequested)
                {
                    // Check if socket is still connected
                    if (!IsSocketConnected(providerSocket))
                    {
                        _logger.Info($"<Provider> => Provider disconnected for SessionId: {BitConverter.ToString(sessionId, 0, 8)}...");
                        break;
                    }
                    
                    await Task.Delay(5000, terminationTokenSource.Token); // Check every 5 seconds
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (Exception e)
            {
                _logger.Debug($"<Provider> => Provider disconnected: {e.Message}");
            }
            finally
            {
                // Clean up the group when provider disconnects
                CleanupNodeGroup(sessionIdKey);
                terminationTokenSource.Dispose();
            }
        }
        catch (Exception e)
        {
            _logger.Error($"<Provider> => Error handling provider connection: {e.Message}");
            _logger.Trace($"<Provider> => {e.StackTrace}");
            try { providerSocket.Close(); } catch { /* Ignored */ }
        }
    }

    private async Task HandleVisitorConnection(Socket visitorSocket)
    {
        // Buffer for receiving the SessionId
        var sessionIdBuffer = _bufferPool.Rent(128);
        
        try
        {
            // Receive the session ID the visitor wants to connect to
            int bytesRead = await visitorSocket.ReceiveAsync(sessionIdBuffer, SocketFlags.None);
            if (bytesRead != 128)
            {
                _logger.Warn($"<Visitor> => Invalid SessionId format: received {bytesRead} bytes");
                visitorSocket.Close();
                return;
            }
            
            string sessionIdKey = Convert.ToBase64String(sessionIdBuffer, 0, 128);
            
            // Find the corresponding provider
            NodeGroup? nodeGroup = null;
            Socket? providerSocket = null;
            
            _groupsLock.EnterReadLock();
            try
            {
                if (_sessionGroups.TryGetValue(sessionIdKey, out nodeGroup))
                {
                    providerSocket = nodeGroup.Provider;
                }
            }
            finally
            {
                _groupsLock.ExitReadLock();
            }
            
            if (nodeGroup == null || providerSocket == null || !IsSocketConnected(providerSocket))
            {
                _logger.Warn($"<Visitor> => Provider not found or disconnected for SessionId: {BitConverter.ToString(sessionIdBuffer, 0, 8)}...");
                
                // Send failure response (0 byte)
                await visitorSocket.SendAsync(new byte[] { 0 }, SocketFlags.None);
                visitorSocket.Close();
                return;
            }
            
            // Add visitor to group
            _groupsLock.EnterWriteLock();
            try
            {
                nodeGroup.Visitors.Add(visitorSocket);
            }
            finally
            {
                _groupsLock.ExitWriteLock();
            }
            
            // Send success response (1 byte)
            await visitorSocket.SendAsync(new byte[] { 1 }, SocketFlags.None);
            
            _logger.Info($"<Visitor> => New visitor connected to SessionId: {BitConverter.ToString(sessionIdBuffer, 0, 8)}...");
            
            // Start data forwarding between visitor and provider
            await ForwardTraffic(visitorSocket, providerSocket, sessionIdBuffer);
        }
        catch (Exception e)
        {
            _logger.Error($"<Visitor> => Error handling visitor connection: {e.Message}");
            _logger.Trace($"<Visitor> => {e.StackTrace}");
            try { visitorSocket.Close(); } catch { /* Ignored */ }
        }
        finally
        {
            _bufferPool.Return(sessionIdBuffer);
        }
    }

    private async Task ForwardTraffic(Socket visitorSocket, Socket providerSocket, byte[] sessionId)
    {
        // Generate a random value for traffic logging
        int random = Random.Shared.Next();
        
        var visitorEp = (IPEndPoint)visitorSocket.RemoteEndPoint!;
        var providerEp = (IPEndPoint)providerSocket.RemoteEndPoint!;
        
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token);
        
        // Create two forwarding tasks
        var visitorToProviderTask = ForwardData(
            visitorSocket, 
            providerSocket, 
            visitorEp, 
            providerEp, 
            true, 
            random, 
            sessionId, 
            cts.Token
        );
        
        var providerToVisitorTask = ForwardData(
            providerSocket, 
            visitorSocket, 
            providerEp, 
            visitorEp, 
            false, 
            random, 
            sessionId, 
            cts.Token
        );
        
        try
        {
            // Wait for either task to complete (indicates one side disconnected)
            await Task.WhenAny(visitorToProviderTask, providerToVisitorTask);
            
            // Cancel the other task
            await cts.CancelAsync();
            
            try
            {
                // Wait for both tasks to finish (with cancellation)
                await Task.WhenAll(visitorToProviderTask, providerToVisitorTask);
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation, ignore
            }
        }
        catch (Exception e)
        {
            _logger.Error($"<Forward> => Error in forwarding: {e.Message}");
            _logger.Trace($"<Forward> => {e.StackTrace}");
        }
        finally
        {
            // Handle cleanup of visitor
            string sessionIdKey = Convert.ToBase64String(sessionId);
            _groupsLock.EnterWriteLock();
            try
            {
                if (_sessionGroups.TryGetValue(sessionIdKey, out var nodeGroup))
                {
                    nodeGroup.Visitors.Remove(visitorSocket);
                }
            }
            finally
            {
                _groupsLock.ExitWriteLock();
            }
            
            try { visitorSocket.Close(); } catch { /* Ignored */ }
            
            _logger.Info($"<Forward> => Forwarding stopped for connection: {visitorEp.Address}:{visitorEp.Port} <-> {providerEp.Address}:{providerEp.Port}");
        }
    }

    private async Task ForwardData(
        Socket source, 
        Socket destination, 
        IPEndPoint sourceEp, 
        IPEndPoint destEp, 
        bool isVisitorToProvider, 
        int random, 
        byte[] sessionId, 
        CancellationToken token)
    {
        byte[] buffer = _bufferPool.Rent(_bufferSize);
        int packetCounter = 0;
        
        try
        {
            while (!token.IsCancellationRequested && IsSocketConnected(source) && IsSocketConnected(destination))
            {
                int bytesRead;
                
                try
                {
                    bytesRead = await source.ReceiveAsync(buffer, SocketFlags.None, token);
                    if (bytesRead == 0)
                    {
                        // Connection closed gracefully
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break; // Cancellation requested
                }
                catch (Exception e)
                {
                    _logger.Debug($"<Forward> => Error receiving data: {e.Message}");
                    break;
                }
                
                try
                {
                    // Forward the data
                    await destination.SendAsync(new ReadOnlyMemory<byte>(buffer, 0, bytesRead), SocketFlags.None, token);
                    
                    // Log traffic
                    await _trafficLogger.LogAsync(sourceEp, destEp, random, bytesRead, isVisitorToProvider);
                    
                    packetCounter++;
                }
                catch (OperationCanceledException)
                {
                    break; // Cancellation requested
                }
                catch (Exception e)
                {
                    _logger.Debug($"<Forward> => Error sending data: {e.Message}");
                    break;
                }
            }
        }
        catch (Exception e)
        {
            _logger.Error($"<Forward> => Unhandled error in data forwarding: {e.Message}");
            _logger.Trace($"<Forward> => {e.StackTrace}");
        }
        finally
        {
            _bufferPool.Return(buffer);
            
            var direction = isVisitorToProvider ? "visitor → provider" : "provider → visitor";
            _logger.Debug($"<Forward> => {direction} forwarding ended after {packetCounter} packets");
        }
    }

    private bool IsSocketConnected(Socket socket)
    {
        try
        {
            return !(socket.Poll(1, SelectMode.SelectRead) && socket.Available == 0);
        }
        catch
        {
            return false;
        }
    }

    private void CleanupNodeGroup(string sessionIdKey)
    {
        _groupsLock.EnterWriteLock();
        try
        {
            if (_sessionGroups.TryGetValue(sessionIdKey, out var nodeGroup))
            {
                // Close all visitor connections
                foreach (var visitorSocket in nodeGroup.Visitors)
                {
                    try { visitorSocket.Close(); } catch { /* Ignored */ }
                }
                
                // Remove the group
                _sessionGroups.Remove(sessionIdKey);
                
                _logger.Info($"<Cleanup> => Node group removed: {BitConverter.ToString(nodeGroup.SessionId, 0, 8)}...");
            }
        }
        finally
        {
            _groupsLock.ExitWriteLock();
        }
    }

    public ITrafficLogger GetTrafficLogger()
    {
        return _trafficLogger;
    }
}
