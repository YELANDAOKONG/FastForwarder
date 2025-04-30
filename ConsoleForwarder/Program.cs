using System.Net;
using LibraryForwarder.Core;

namespace ConsoleForwarder;

internal static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");
        TcpForwarder forwarder = new TcpForwarder(
            IPAddress.Parse("127.0.0.1"),
            8000,
            IPAddress.Parse("103.205.253.87"),
            34015
        );
        forwarder.Start();
        forwarder.Wait();
    }
}