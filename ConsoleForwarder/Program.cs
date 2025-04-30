using System.Net;
using LibraryForwarder.Core;

namespace ConsoleForwarder;

internal static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");
        SimpleTrafficLogger logger = new SimpleTrafficLogger();
        TcpForwarder forwarder = new TcpForwarder(
            IPAddress.Parse("127.0.0.1"),
            8000,
            IPAddress.Parse("103.205.253.87"),
            34015,
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