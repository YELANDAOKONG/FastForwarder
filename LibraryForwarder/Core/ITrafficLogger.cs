using System.Net;

namespace LibraryForwarder.Core;

public interface ITrafficLogger
{
    public void Log(IPEndPoint from, IPEndPoint to, int random, int bytes, bool isToRemote);
    public Task LogAsync(IPEndPoint from, IPEndPoint to, int random, int bytes, bool isToRemote);
    
    public (int from, int to) Queue(IPEndPoint from, IPEndPoint to, int random);
}