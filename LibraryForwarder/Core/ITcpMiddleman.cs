using System.Net;

namespace LibraryForwarder.Core;

public interface ITcpMiddleman
{
    public byte[] Data(IPEndPoint from, IPEndPoint to, int random, int counter, bool isToRemote, byte[] data);
    public async Task<byte[]> DataAsync(IPEndPoint from, IPEndPoint to, int random, int counter, bool isToRemote, byte[] data)
    {
        return await Task.Run(() => Data(from, to, random, counter, isToRemote, data));
    }
}