namespace MikuOS.Transport;

public interface ITransport : IDisposable
{
    event Action<string>? Received;
    event Action<string>? Faulted;
    bool Connected { get; }
    void Connect();
    void Send(string data);
    void Disconnect();
}
