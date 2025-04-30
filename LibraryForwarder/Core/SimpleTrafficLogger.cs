using System.Net;

namespace LibraryForwarder.Core;

public class SimpleTrafficLogger : ITrafficLogger
{
    
    public readonly Dictionary<(IPEndPoint from, IPEndPoint to, int random), (long fromRemote, long toRemote)> Traffic = new();
    public readonly ReaderWriterLockSlim TrafficLock = new();
    
    public SimpleTrafficLogger()
    {
        
    }

    public void Log(IPEndPoint from, IPEndPoint to, int random, long bytes, bool isToRemote)
    {
        TrafficLock.EnterWriteLock();
        if (!Traffic.ContainsKey((from, to, random)))
        {
            Traffic.Add((from, to, random), (0, 0));
        }
        if (isToRemote)
        {
            Traffic[(from, to, random)] = (Traffic[(from, to, random)].Item1, Traffic[(from, to, random)].Item2 + bytes);
        }
        else
        {
            Traffic[(from, to, random)] = (Traffic[(from, to, random)].Item1 + bytes, Traffic[(from, to, random)].Item2);
        }
        TrafficLock.ExitWriteLock();
    }

    public Task LogAsync(IPEndPoint from, IPEndPoint to, int random, long bytes, bool isToRemote)
    {
        return Task.Run(() => Log(from, to, random, bytes, isToRemote));
    }

    public (long from, long to) Queue(IPEndPoint from, IPEndPoint to, int random)
    {
        TrafficLock.EnterReadLock();
        var data = (0L, 0L);
        if (Traffic.ContainsKey((from, to, random)))
        {
            data = Traffic[(from, to, random)];
        }
        TrafficLock.ExitReadLock();
        return data;
    }
}