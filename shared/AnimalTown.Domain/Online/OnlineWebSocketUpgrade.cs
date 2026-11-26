using System.Text;

namespace AnimalTown.Online;

// The HTTP upgrade runs only during connection setup, with the scene unloaded.
public sealed class OnlineWebSocketUpgrade
{
    public const int HeaderLimit = 8192;
    public const string Subprotocol = "animaltown.v1";
    readonly string expected;
    public readonly byte[] Request;

    public OnlineWebSocketUpgrade(string origin, IOnlineMaskSource random)
    {
        if (!OnlineCardCredential.ValidOrigin(origin)) throw new ArgumentException("Expected a valid HTTPS origin.");
        var nonce = new byte[16];
        for (int block = 0; block < 4; block++)
        {
            uint value = random.NextMask();
            for (int i = 0; i < 4; i++) nonce[block * 4 + i] = (byte)(value >> (i * 8));
        }
        string key = Base64(nonce);
        expected = AcceptKey(key);
        Request = Encoding.ASCII.GetBytes("GET /v1/gameplay HTTP/1.1\r\nHost: " + origin.Substring(8) +
            "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Version: 13\r\nSec-WebSocket-Key: " + key +
            "\r\nSec-WebSocket-Protocol: " + Subprotocol + "\r\n\r\n");
    }

    public OnlineFrameRead ReadResponse(byte[] bytes, int count, out int consumed)
    {
        consumed = 0;
        if (bytes is null || count < 0 || count > bytes.Length) return OnlineFrameRead.Invalid;
        int end = -1;
        for (int i = 0; i + 3 < count && i + 3 < HeaderLimit; i++)
            if (bytes[i] == 13 && bytes[i + 1] == 10 && bytes[i + 2] == 13 && bytes[i + 3] == 10) { end = i + 4; break; }
        if (end < 0) return count >= HeaderLimit ? OnlineFrameRead.Invalid : OnlineFrameRead.Incomplete;
        for (int i = 0; i < end; i++) if (bytes[i] > 126 || bytes[i] < 32 && bytes[i] is not (9 or 10 or 13)) return OnlineFrameRead.Invalid;
        string headers = Encoding.ASCII.GetString(bytes, 0, end);
        int first = headers.IndexOf("\r\n", StringComparison.Ordinal);
        if (first < 12 || !headers.StartsWith("HTTP/1.1 101 ", StringComparison.Ordinal)) return OnlineFrameRead.Invalid;
        bool upgrade = false, connection = false, accept = false, protocol = false;
        int fields = 0, at = first + 2;
        while (at < end - 2)
        {
            if (++fields > 64) return OnlineFrameRead.Invalid;
            int lineEnd = headers.IndexOf("\r\n", at, StringComparison.Ordinal);
            if (lineEnd < at) return OnlineFrameRead.Invalid;
            int colon = headers.IndexOf(':', at);
            if (colon <= at || colon >= lineEnd) return OnlineFrameRead.Invalid;
            string name = Lower(headers.Substring(at, colon - at));
            for (int i = 0; i < name.Length; i++)
                if (!(name[i] >= 'a' && name[i] <= 'z' || name[i] >= '0' && name[i] <= '9' || name[i] == '-')) return OnlineFrameRead.Invalid;
            string value = headers.Substring(colon + 1, lineEnd - colon - 1).Trim();
            if (name == "upgrade") { if (upgrade || Lower(value) != "websocket") return OnlineFrameRead.Invalid; upgrade = true; }
            else if (name == "connection") { if (connection || !HasToken(value, "upgrade")) return OnlineFrameRead.Invalid; connection = true; }
            else if (name == "sec-websocket-accept") { if (accept || value != expected) return OnlineFrameRead.Invalid; accept = true; }
            else if (name == "sec-websocket-protocol") { if (protocol || value != Subprotocol) return OnlineFrameRead.Invalid; protocol = true; }
            else if (name == "sec-websocket-extensions" || name == "transfer-encoding" || name == "content-length" && value != "0") return OnlineFrameRead.Invalid;
            at = lineEnd + 2;
        }
        if (!upgrade || !connection || !accept || !protocol) return OnlineFrameRead.Invalid;
        consumed = end;
        return OnlineFrameRead.Complete;
    }

    static bool HasToken(string value, string token)
    {
        int at = 0;
        while (at < value.Length)
        {
            int end = value.IndexOf(',', at); if (end < 0) end = value.Length;
            if (Lower(value.Substring(at, end - at).Trim()) == token) return true;
            at = end + 1;
        }
        return false;
    }
    static string Lower(string text)
    {
        var chars = text.ToCharArray();
        for (int i = 0; i < chars.Length; i++) if (chars[i] >= 'A' && chars[i] <= 'Z') chars[i] = (char)(chars[i] + 32);
        return new string(chars);
    }

    // SHA-1 is mandated by the WebSocket upgrade, not used for certificate or
    // password security. Fixed-size input and scratch avoid crypto dependencies
    // unsupported by the console AOT target.
    public static string AcceptKey(string key)
    {
        if (key.Length != 24) throw new ArgumentException("Expected a 16-byte WebSocket nonce.");
        var source = Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11");
        var padded = new byte[128]; Array.Copy(source, padded, source.Length);
        padded[source.Length] = 128; padded[126] = 1; padded[127] = 224; // 60 bytes = 480 bits
        var words = new uint[80];
        uint h0 = 0x67452301, h1 = 0xefcdab89, h2 = 0x98badcfe, h3 = 0x10325476, h4 = 0xc3d2e1f0;
        unchecked
        {
            for (int block = 0; block < 2; block++)
            {
                for (int i = 0; i < 16; i++) { int p = block * 64 + i * 4; words[i] = (uint)(padded[p] << 24 | padded[p + 1] << 16 | padded[p + 2] << 8 | padded[p + 3]); }
                for (int i = 16; i < 80; i++) words[i] = Rotate(words[i - 3] ^ words[i - 8] ^ words[i - 14] ^ words[i - 16], 1);
                uint a = h0, b = h1, c = h2, d = h3, e = h4;
                for (int i = 0; i < 80; i++)
                {
                    uint f = i < 20 ? (b & c) | (~b & d) : i < 40 || i >= 60 ? b ^ c ^ d : (b & c) | (b & d) | (c & d);
                    uint k = i < 20 ? 0x5a827999u : i < 40 ? 0x6ed9eba1u : i < 60 ? 0x8f1bbcdcu : 0xca62c1d6u;
                    uint next = Rotate(a, 5) + f + e + k + words[i]; e = d; d = c; c = Rotate(b, 30); b = a; a = next;
                }
                h0 += a; h1 += b; h2 += c; h3 += d; h4 += e;
            }
        }
        var digest = new byte[20];
        Write(digest, 0, h0); Write(digest, 4, h1); Write(digest, 8, h2); Write(digest, 12, h3); Write(digest, 16, h4);
        return Base64(digest);
    }
    static uint Rotate(uint value, int count) => value << count | value >> (32 - count);
    static void Write(byte[] bytes, int at, uint value) { for (int i = 0; i < 4; i++) bytes[at + i] = (byte)(value >> (24 - i * 8)); }
    static string Base64(byte[] bytes)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        var chars = new char[(bytes.Length + 2) / 3 * 4];
        for (int i = 0, at = 0; i < bytes.Length; i += 3)
        {
            int remaining = bytes.Length - i;
            int value = bytes[i] << 16 | (remaining > 1 ? bytes[i + 1] << 8 : 0) | (remaining > 2 ? bytes[i + 2] : 0);
            chars[at++] = alphabet[value >> 18]; chars[at++] = alphabet[value >> 12 & 63];
            chars[at++] = remaining > 1 ? alphabet[value >> 6 & 63] : '='; chars[at++] = remaining > 2 ? alphabet[value & 63] : '=';
        }
        return new string(chars);
    }
}
