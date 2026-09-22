using System.IO.Ports;
using MikuOS.Transport;

namespace MikuOS.ControlCenter.Serial;
public sealed class SerialTransport : ITransport
{
    private readonly SerialPort port;
    public SerialTransport(string name) { port = new(name, 115200) { Encoding = System.Text.Encoding.ASCII, DtrEnable = false, RtsEnable = false, WriteTimeout = 1000 }; port.DataReceived += OnData; }
    public event Action<string>? Received;
    public event Action<string>? Faulted;
    public bool Connected => port.IsOpen;
    public void Connect() => port.Open();
    private void OnData(object sender, SerialDataReceivedEventArgs args) { try { Received?.Invoke(port.ReadExisting()); } catch (Exception e) when (e is IOException or InvalidOperationException) { Faulted?.Invoke(e.Message); } }
    public void Send(string data) => port.Write(data);
    public void Disconnect() { if (port.IsOpen) port.Close(); }
    public void Dispose() { port.DataReceived -= OnData; port.Dispose(); }
}
