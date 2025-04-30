using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LibraryForwarder.Core;

namespace LibraryForwarder.Utils;

/// <summary>
/// TCP Benchmark utility for testing performance and integrity of TCP connections and relay servers.
/// Similar to iPerf3, but with additional integrity verification.
/// </summary>
public class TcpBenchmark : IDisposable
{
    private readonly ILogger _logger;
    private CancellationTokenSource? _cts;
    private readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;
    
    // Performance metrics
    private long _totalBytesSent;
    private long _totalBytesReceived;
    private readonly ConcurrentDictionary<long, long> _packetLatencies = new();
    private long _corruptedPackets;
    private long _totalPackets;
    
    // Configuration options
    public int BufferSize { get; set; } = 64 * 1024;       // Default 64KB buffer
    public TimeSpan TestDuration { get; set; } = TimeSpan.FromSeconds(10);
    public bool VerifyIntegrity { get; set; } = true;
    public int ParallelConnections { get; set; } = 1;
    
    public enum TestMode
    {
        Throughput,
        Latency,
        Both
    }
    
    public TestMode Mode { get; set; } = TestMode.Both;

    /// <summary>
    /// Creates a new TCP benchmark utility
    /// </summary>
    /// <param name="logger">Logger for output</param>
    public TcpBenchmark(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Run as a benchmark server that receives data
    /// </summary>
    /// <param name="listenAddress">Address to listen on</param>
    /// <param name="port">Port to listen on</param>
    /// <returns>Async task</returns>
    public async Task RunServerAsync(IPAddress listenAddress, int port)
    {
        _logger.Info($"Starting TCP benchmark server on {listenAddress}:{port}");
        _cts = new CancellationTokenSource();
        
        ResetMetrics();
        
        var listener = new TcpListener(listenAddress, port);
        listener.Start();
        _logger.Info("Server started, waiting for client connections...");
        
        try
        {
            var connectionTasks = new List<Task>();
            var stopwatch = Stopwatch.StartNew();
            
            while (stopwatch.Elapsed < TestDuration && !_cts.Token.IsCancellationRequested)
            {
                var tcpClient = await listener.AcceptTcpClientAsync(_cts.Token);
                _logger.Info($"Client connected from {((IPEndPoint)tcpClient.Client.RemoteEndPoint!).Address}");
                
                connectionTasks.Add(HandleClientConnectionAsync(tcpClient, _cts.Token));
            }
            
            await Task.WhenAll(connectionTasks);
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Test canceled");
        }
        catch (Exception ex)
        {
            _logger.Error($"Server error: {ex.Message}");
        }
        finally
        {
            listener.Stop();
            await ReportResultsAsync();
        }
    }

    /// <summary>
    /// Run as a benchmark client that sends data to server
    /// </summary>
    /// <param name="serverAddress">Server address</param>
    /// <param name="port">Server port</param>
    /// <returns>Async task</returns>
    public async Task RunClientAsync(IPAddress serverAddress, int port)
    {
        _logger.Info($"Starting TCP benchmark client connecting to {serverAddress}:{port}");
        _logger.Info($"Test configuration: Duration={TestDuration}, BufferSize={BufferSize}, Connections={ParallelConnections}, VerifyIntegrity={VerifyIntegrity}");
        
        _cts = new CancellationTokenSource();
        ResetMetrics();
        
        try
        {
            _cts.CancelAfter(TestDuration);
            
            var connectionTasks = new List<Task>();
            for (int i = 0; i < ParallelConnections; i++)
            {
                connectionTasks.Add(Task.Run(() => RunClientConnectionAsync(serverAddress, port, i, _cts.Token)));
            }
            
            await Task.WhenAll(connectionTasks);
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Test completed - time limit reached");
        }
        catch (Exception ex)
        {
            _logger.Error($"Client error: {ex.Message}");
        }
        finally
        {
            await ReportResultsAsync();
        }
    }

    /// <summary>
    /// Run test through a relay/forwarding server to measure its performance
    /// </summary>
    /// <param name="relayAddress">Relay server address</param>
    /// <param name="relayPort">Relay server port</param>
    /// <returns>Async task</returns>
    public async Task TestRelayPerformanceAsync(IPAddress relayAddress, int relayPort)
    {
        _logger.Info($"Starting TCP relay benchmark through {relayAddress}:{relayPort}");
        
        // First start a local server to receive data 
        var serverEndpoint = new IPEndPoint(IPAddress.Loopback, GetAvailablePort());
        var serverTask = RunServerAsync(serverEndpoint.Address, serverEndpoint.Port);
        
        // Wait a moment for server to start
        await Task.Delay(500);
        
        // Then run client through the relay
        await RunClientAsync(relayAddress, relayPort);
        
        // Cancel the server after client completes
        _cts?.Cancel();
        await serverTask;
    }

    /// <summary>
    /// Handle incoming client connection
    /// </summary>
    private async Task HandleClientConnectionAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            client.NoDelay = true;
            var stream = client.GetStream();
            
            // Rent buffer from pool
            byte[] buffer = _bufferPool.Rent(BufferSize);
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct);
                    if (bytesRead == 0) break; // Connection closed
                    
                    Interlocked.Add(ref _totalBytesReceived, bytesRead);
                    
                    if (VerifyIntegrity)
                    {
                        VerifyPacketIntegrity(buffer, bytesRead);
                    }
                    
                    if (Mode == TestMode.Latency || Mode == TestMode.Both)
                    {
                        ProcessLatencyData(buffer, bytesRead);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (Exception ex)
            {
                _logger.Error($"Error handling client: {ex.Message}");
            }
            finally
            {
                _bufferPool.Return(buffer);
            }
        }
    }

    /// <summary>
    /// Run a single client connection to the server
    /// </summary>
    private async Task RunClientConnectionAsync(IPAddress serverAddress, int port, int connectionId, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            client.NoDelay = true;
            
            await client.ConnectAsync(serverAddress, port, ct);
            _logger.Debug($"Connection {connectionId} established");
            
            var stream = client.GetStream();
            var stopwatch = Stopwatch.StartNew();
            
            // Rent buffer from pool
            byte[] buffer = _bufferPool.Rent(BufferSize);
            try
            {
                long packetId = 0;
                while (!ct.IsCancellationRequested)
                {
                    // Generate test data
                    int dataSize = GenerateTestData(buffer, packetId++);
                    
                    // Send data
                    var sendTime = stopwatch.ElapsedMilliseconds;
                    await stream.WriteAsync(buffer, 0, dataSize, ct);
                    Interlocked.Add(ref _totalBytesSent, dataSize);
                    Interlocked.Increment(ref _totalPackets);
                    
                    // Store send time for latency calculation if needed
                    if (Mode == TestMode.Latency || Mode == TestMode.Both)
                    {
                        _packetLatencies[packetId] = sendTime;
                    }
                    
                    // Small delay to avoid overwhelming the network
                    if (Mode == TestMode.Latency)
                    {
                        await Task.Delay(10, ct);
                    }
                }
            }
            finally
            {
                _bufferPool.Return(buffer);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (Exception ex)
        {
            _logger.Error($"Connection {connectionId} error: {ex.Message}");
        }
    }

    /// <summary>
    /// Generate test data with integrity verification if enabled
    /// </summary>
    private int GenerateTestData(byte[] buffer, long packetId)
    {
        int dataSize = BufferSize;
        
        // Write packet header (packet ID and timestamp)
        BitConverter.TryWriteBytes(buffer, packetId);
        BitConverter.TryWriteBytes(buffer.AsSpan(8), DateTime.UtcNow.Ticks);
        
        if (VerifyIntegrity)
        {
            // Fill the rest with deterministic but unique data 
            for (int i = 16; i < dataSize - 32; i++)
            {
                buffer[i] = (byte)((i + packetId) % 256);
            }
            
            // Add checksum at the end
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(buffer, 0, dataSize - 32);
            Buffer.BlockCopy(hash, 0, buffer, dataSize - 32, 32);
        }
        else
        {
            // Fill with any data if integrity check not needed
            for (int i = 16; i < dataSize; i++)
            {
                buffer[i] = (byte)(i % 256);
            }
        }
        
        return dataSize;
    }

    /// <summary>
    /// Verify the integrity of received data
    /// </summary>
    private void VerifyPacketIntegrity(byte[] buffer, int bytesRead)
    {
        if (bytesRead < 48) return; // Too small for proper verification
        
        // Get packet ID
        long packetId = BitConverter.ToInt64(buffer, 0);
        
        // Verify checksum
        using var sha = SHA256.Create();
        var computedHash = sha.ComputeHash(buffer, 0, bytesRead - 32);
        
        for (int i = 0; i < 32; i++)
        {
            if (computedHash[i] != buffer[bytesRead - 32 + i])
            {
                Interlocked.Increment(ref _corruptedPackets);
                _logger.Warn($"Packet {packetId} integrity check failed");
                return;
            }
        }
        
        // Verify data pattern
        for (int i = 16; i < bytesRead - 32; i++)
        {
            if (buffer[i] != (byte)((i + packetId) % 256))
            {
                Interlocked.Increment(ref _corruptedPackets);
                _logger.Warn($"Packet {packetId} data pattern check failed at offset {i}");
                return;
            }
        }
    }

    /// <summary>
    /// Process latency data from received packets
    /// </summary>
    private void ProcessLatencyData(byte[] buffer, int bytesRead)
    {
        if (bytesRead < 16) return;
        
        // Get packet ID and timestamp
        long packetId = BitConverter.ToInt64(buffer, 0);
        long sendTimeTicks = BitConverter.ToInt64(buffer, 8);
        
        // Calculate latency
        long currentTicks = DateTime.UtcNow.Ticks;
        long latencyTicks = currentTicks - sendTimeTicks;
        long latencyMs = latencyTicks / TimeSpan.TicksPerMillisecond;
        
        _packetLatencies[packetId] = latencyMs;
    }

    /// <summary>
    /// Report benchmark results
    /// </summary>
    private async Task ReportResultsAsync()
    {
        await Task.Delay(100); // Brief delay to ensure all metrics are collected
        
        _logger.Info("--- TCP Benchmark Results ---");
        
        // Calculate throughput
        double durationSeconds = TestDuration.TotalSeconds;
        double sendMbps = _totalBytesSent * 8.0 / (1024 * 1024 * durationSeconds);
        double receiveMbps = _totalBytesReceived * 8.0 / (1024 * 1024 * durationSeconds);
        
        _logger.Info($"Test duration: {durationSeconds:F2} seconds");
        _logger.Info($"Total data sent: {FormatByteSize(_totalBytesSent)}");
        _logger.Info($"Total data received: {FormatByteSize(_totalBytesReceived)}");
        _logger.Info($"Send throughput: {sendMbps:F2} Mbps");
        _logger.Info($"Receive throughput: {receiveMbps:F2} Mbps");
        
        if (_totalPackets > 0)
        {
            _logger.Info($"Total packets: {_totalPackets}");
        }
        
        if (VerifyIntegrity)
        {
            double errorRate = _totalPackets > 0 ? (double)_corruptedPackets / _totalPackets * 100 : 0;
            _logger.Info($"Data integrity: {_corruptedPackets} corrupted packets ({errorRate:F4}% error rate)");
        }
        
        if (Mode == TestMode.Latency || Mode == TestMode.Both)
        {
            var latencies = _packetLatencies.Values.Where(l => l > 0).ToList();
            if (latencies.Count > 0)
            {
                double avgLatency = latencies.Average();
                double minLatency = latencies.Min();
                double maxLatency = latencies.Max();
                double p95Latency = CalculatePercentile(latencies, 95);
                double p99Latency = CalculatePercentile(latencies, 99);
                
                _logger.Info($"Latency (ms): Min={minLatency:F2}, Avg={avgLatency:F2}, Max={maxLatency:F2}, p95={p95Latency:F2}, p99={p99Latency:F2}");
            }
        }
        
        _logger.Info("--- End of Results ---");
    }

    /// <summary>
    /// Calculate percentile from a list of values
    /// </summary>
    private double CalculatePercentile(List<long> values, int percentile)
    {
        var sortedValues = values.OrderBy(v => v).ToList();
        int index = (int)Math.Ceiling((percentile / 100.0) * sortedValues.Count) - 1;
        return sortedValues[Math.Max(0, index)];
    }

    /// <summary>
    /// Reset all metrics before starting a new test
    /// </summary>
    private void ResetMetrics()
    {
        _totalBytesSent = 0;
        _totalBytesReceived = 0;
        _corruptedPackets = 0;
        _totalPackets = 0;
        _packetLatencies.Clear();
    }

    /// <summary>
    /// Format byte size to human-readable format
    /// </summary>
    private string FormatByteSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        
        return $"{len:F2} {sizes[order]} ({bytes:N0} bytes)";
    }

    /// <summary>
    /// Find an available port on the local machine
    /// </summary>
    private int GetAvailablePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    /// <summary>
    /// Dispose of resources
    /// </summary>
    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Print help information about this utility
    /// </summary>
    public void PrintHelp()
    {
        _logger.Info("TCP Benchmark Usage:");
        _logger.Info("  Server mode: Run a benchmark server to receive data");
        _logger.Info("    Example: TcpBenchmark -s -p 5001");
        _logger.Info("");
        _logger.Info("  Client mode: Connect to a server and send benchmark data");
        _logger.Info("    Example: TcpBenchmark -c server_ip -p 5001 -t 30");
        _logger.Info("");
        _logger.Info("  Relay test: Test performance through a relay server");
        _logger.Info("    Example: TcpBenchmark -r relay_ip -p 5001 -t 30");
        _logger.Info("");
        _logger.Info("Common Options:");
        _logger.Info("  -t <seconds>     Test duration (default: 10s)");
        _logger.Info("  -b <size>        Buffer size in KB (default: 64KB)");
        _logger.Info("  -p <port>        Port to use (default: 5001)");
        _logger.Info("  -P <num>         Number of parallel connections (default: 1)");
        _logger.Info("  -i <0|1>         Verify data integrity (default: 1)");
        _logger.Info("  -m <t|l|b>       Mode: throughput, latency, or both (default: both)");
    }
}
