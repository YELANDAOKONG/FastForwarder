using System.Net;
using System.Text;

namespace LibraryForwarder.Core;

public class SimpleTcpMiddleman : ITcpMiddleman
{
    
    private ILogger _logger = new SimpleLogger("STM", true);

    public SimpleTcpMiddleman(){}

    public SimpleTcpMiddleman(ILogger logger)
    {
        _logger = logger;
    }
    
    public byte[] Data(IPEndPoint from, IPEndPoint to, int random, int counter, bool isToRemote, byte[] data)
    {
        StringBuilder builder = new();
        builder.Append("<Packet>");
        builder.Append('\n');
        if (isToRemote)
        {
            builder.Append($"F/T: ({from.Address}) --> ({to.Address})");
            builder.Append('\n');
        }
        else
        {
            builder.Append($"F/T: ({to.Address}) --> ({from.Address})");
            builder.Append('\n');
            
        }
        builder.Append($"SRN: {random}");
        builder.Append('\n');
        builder.Append($"CER: {counter}");
        builder.Append('\n');
        builder.Append($"DATA: ");
        builder.Append('\n');
        
        // Print data with Hex format
        // Hex and ASCII display
        const int bytesPerLine = 16;
        for (int i = 0; i < data.Length; i += bytesPerLine)
        {
            // Hex portion
            var lineBytes = Math.Min(bytesPerLine, data.Length - i);
            var hexLine = new StringBuilder();
            var asciiLine = new StringBuilder();
        
            for (int j = 0; j < lineBytes; j++)
            {
                byte b = data[i + j];
                hexLine.Append($"{b:X2} ");
            
                // ASCII portion - replace non-printable characters with '.'
                asciiLine.Append(b >= 0x20 && b <= 0x7E ? (char)b : '.');
            }
        
            // Pad the hex line if needed
            hexLine.Append(new string(' ', 3 * (bytesPerLine - lineBytes)));
        
            // Format the output line
            builder.Append($"{i:X8}: {hexLine}  {asciiLine}");
            builder.Append('\n');
        }
    
        _logger.Info(builder.ToString());
        return data;
    }
}