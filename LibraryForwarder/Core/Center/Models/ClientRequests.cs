namespace LibraryForwarder.Core.Center;

public enum ClientRequests
{
    None = 0,
    CreateService = 1,   // Provider node creates a service
    ConnectService = 2,  // Visitor node connects to a service
    Data = 3,            // Data packet
}
