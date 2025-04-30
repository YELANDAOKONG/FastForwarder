using System.Net;
using LibraryForwarder.Core;
using LibraryForwarder.Utils;

namespace ConsoleForwarder;

internal static class Program
{
    public static void Main(string[] args)
    {
        if (args.Length >= 1)
        {
            bool checksum = false;
            bool echo = false;
            int threads = 4;
            if (args[0] == "client")
            {
                TcpBenchmark benchmark = new TcpBenchmark(new SimpleLogger("TBC", true));
                benchmark.VerifyIntegrity = checksum;
                benchmark.ParallelConnections = threads;
                benchmark.BufferSize = 256 * 1024;
                benchmark.EchoMode = echo; 
                benchmark.RunClientAsync(IPAddress.Parse("127.0.0.1"), 5000).Wait();
                return;
            }
            if (args[0] == "client-9000")
            {
                TcpBenchmark benchmark = new TcpBenchmark(new SimpleLogger("TBC", true));
                benchmark.VerifyIntegrity = checksum;
                benchmark.ParallelConnections = threads;
                benchmark.BufferSize = 256 * 1024;
                benchmark.EchoMode = echo; 
                benchmark.RunClientAsync(IPAddress.Parse("127.0.0.1"), 9000).Wait();
                return;
            }
            if (args[0] == "client-direct")
            {
                TcpBenchmark benchmark = new TcpBenchmark(new SimpleLogger("TBC", true));
                benchmark.VerifyIntegrity = checksum;
                benchmark.ParallelConnections = threads;
                benchmark.BufferSize = 256 * 1024;
                benchmark.EchoMode = echo; 
                benchmark.RunClientAsync(IPAddress.Parse("127.0.0.1"), 8000).Wait();
                return;
            }
            if (args[0] == "server" || args[0] == "server-direct")
            {
                TcpBenchmark benchmark = new TcpBenchmark(new SimpleLogger("TBC", true));
                benchmark.VerifyIntegrity = checksum;
                benchmark.ParallelConnections = threads;
                benchmark.BufferSize = 256 * 1024;
                benchmark.EchoMode = echo; 
                benchmark.RunServerAsync(IPAddress.Parse("127.0.0.1"), 8000).Wait();
                return;
            }
        }
        Console.WriteLine("Hello, World!");
        ILogger log = new SimpleLogger("APP", true);
        log.All("Hello, World!");
        log.Trace("Hello, World!");
        log.Debug("Hello, World!");
        log.Info("Hello, World!");
        log.Warn("Hello, World!");
        log.Error("Hello, World!");
        log.Fatal("Hello, World!");
        log.Off("Hello, World!");
        SimpleTrafficLogger logger = new SimpleTrafficLogger();
        TcpForwarder forwarder = new TcpForwarder(
            IPAddress.Parse("127.0.0.1"),
            5000,
            IPAddress.Parse("127.0.0.1"),
            8000,
            trafficLogger: logger
        );
        if (args.Length >= 1 && args[0] == "mc")
        {
            forwarder = new TcpForwarder(
                IPAddress.Parse("127.0.0.1"),
                8000,
                IPAddress.Parse("103.205.253.87"),
                34015,
                trafficLogger: logger
            );
        }
        
        forwarder.Start();
        while (!forwarder.IsDisposed)
        {
            Console.ReadLine();
            logger.TrafficLock.EnterReadLock();
            foreach (var traffic in logger.Traffic)
            {
                Console.WriteLine($"[TRAFFIC] ({traffic.Key.from}) <=> ({traffic.Key.to}) [{traffic.Key.random}]: {traffic.Value.fromRemote} <-> {traffic.Value.toRemote}");
            }
            logger.TrafficLock.ExitReadLock();
        }
        forwarder.Wait();
    }
}