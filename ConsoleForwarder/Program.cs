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
            if (args[0] == "client")
            {
                TcpBenchmark benchmark = new TcpBenchmark(new SimpleLogger("TBC", true));
                benchmark.RunClientAsync(IPAddress.Parse("127.0.0.1"), 5000).Wait();
                return;
            }
            if (args[0] == "client-direct")
            {
                TcpBenchmark benchmark = new TcpBenchmark(new SimpleLogger("TBC", true));
                benchmark.RunClientAsync(IPAddress.Parse("127.0.0.1"), 8000).Wait();
                return;
            }
            if (args[0] == "server")
            {
                TcpBenchmark benchmark = new TcpBenchmark(new SimpleLogger("TBC", true));
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
        
        forwarder.Start();
        while (!forwarder.IsDisposed())
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