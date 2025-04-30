using System.Net;

namespace LibraryForwarder.Core;

public interface ITrafficLogger
{
    public void Log(IPEndPoint from, IPEndPoint to, int random, long bytes, bool isToRemote);
    public Task LogAsync(IPEndPoint from, IPEndPoint to, int random, long bytes, bool isToRemote);
    
    public (long from, long to) Queue(IPEndPoint from, IPEndPoint to, int random);
}