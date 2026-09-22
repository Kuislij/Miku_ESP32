using System.Text;

namespace MikuOS.Protocol;

// ASCII envelope; UTF-8 payload is Base64 so delimiters and newlines are unambiguous.
public sealed record Frame(string Type, uint Id, string Payload)
{
    public const int MaxLine = 8192;
    private static readonly HashSet<string> Types = ["CMD", "RES", "ERR", "EVT", "LOG", "STAT", "TASKS", "HB"];
    public string Encode()
    {
        if (!Types.Contains(Type)) throw new FormatException("Unknown message type");
        var line = $"1|{Type}|{Id}|{Convert.ToBase64String(Encoding.UTF8.GetBytes(Payload))}\n";
        if (line.Length > MaxLine) throw new FormatException("Frame too large");
        return line;
    }
    public static Frame Parse(string line)
    {
        var parts = line.Split('|');
        if (parts.Length != 4 || parts[0] != "1" || !Types.Contains(parts[1]) ||
            !uint.TryParse(parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id))
            throw new FormatException("Invalid protocol envelope");
        return new(parts[1], id, new UTF8Encoding(false, true).GetString(Convert.FromBase64String(parts[3])));
    }
}

public sealed class FrameDecoder
{
    private readonly StringBuilder buffer = new();
    private bool dropping;
    public int Rejected { get; private set; }
    public IEnumerable<Frame> Feed(string chunk)
    {
        var result = new List<Frame>();
        foreach (var c in chunk)
        {
            if (c == '\n')
            {
                if (!dropping)
                {
                    try { result.Add(Frame.Parse(buffer.ToString().TrimEnd('\r'))); }
                    catch (Exception e) when (e is FormatException or DecoderFallbackException) { Rejected++; }
                }
                buffer.Clear(); dropping = false;
            }
            else if (!dropping)
            {
                if (c > 127 || buffer.Length >= Frame.MaxLine - 1) { Rejected++; buffer.Clear(); dropping = true; }
                else buffer.Append(c);
            }
        }
        return result;
    }
}
