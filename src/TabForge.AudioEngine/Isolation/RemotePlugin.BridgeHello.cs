using System.IO;
using System.Security.Cryptography;
using TabForge.Audio.Contracts;

namespace TabForge.AudioEngine.Isolation;

// Owns: the optional bridge hello frames (the named-pipe TF32 handshake and the Wine TCP TFH1 handshake with its token).
// Does not own: starting the session (RemotePlugin) or the helper's own protocol constants (tfvst32_protocol.h).
// Tests: Vst32RoutingTests, Vst32TcpBridgeTests.
public sealed partial class RemotePlugin
{
    /// <summary>Version and fixed header size of the 32-bit helper's optional hello frame (the stage-3 handshake, before MsgLoaded).</summary>
    public const uint BridgeHelloMagic = 0x32334654;   // "TF32"
    public const ushort BridgeProtocolVersion = 1, BridgeHeaderSize = 64;
    /// <summary>The TCP/file transport's hello magic "TFH1"; it adds arch, version strings and the session token.</summary>
    public const uint BridgeTcpHelloMagic = 0x31484654;
    /// <summary>The session token's environment variable, never a command-line argument.</summary>
    public const string TokenVariable = WineRuntime.TokenVariable;

    /// <summary>
    /// The optional hello frame of the 32-bit helper (sent before the load, so a slow plug-in open is not a dead pipe):
    /// magic "TF32", protocol version, fixed header size, block size, sample rate and buffer slot count. A helper built for
    /// another protocol or another session is refused with a readable failure instead of being fed the wrong shared block.
    /// The existing 64-bit host does not send this frame and is unchanged.
    /// </summary>
    internal static void ReadBridgeHello(BinaryReader reader, int sampleRate, int maxBlock, int bufferSlots)
    {
        var magic = reader.ReadUInt32();
        var version = reader.ReadUInt16();
        var headerSize = reader.ReadUInt16();
        var helperBlock = reader.ReadInt32();
        var helperRate = reader.ReadInt32();
        var helperSlots = reader.ReadInt32();
        if (magic != BridgeHelloMagic || version != BridgeProtocolVersion || headerSize != BridgeHeaderSize ||
            helperBlock != maxBlock || helperRate != sampleRate || helperSlots != Math.Clamp(bufferSlots, 1, 2))
            throw new InvalidOperationException(
                $"The 32-bit plug-in helper uses a different protocol (magic 0x{magic:X8}, version {version}, header {headerSize}, block {helperBlock}, rate {helperRate}, slots {helperSlots}).");
    }

    /// <summary>
    /// The TCP transport's hello: "TFH1", protocol/header, block/rate/slots, arch, flags, helper and Wine versions, then the
    /// 32 raw token bytes. The token is read from the child's environment and must match before any command is sent. The
    /// expected architecture is the helper the launch chose (32 for the x86 helper, 64 for the x64 helper under Wine).
    /// </summary>
    internal static void ReadBridgeTcpHello(BinaryReader reader, string tokenHex, int sampleRate, int maxBlock, int bufferSlots,
        int expectedArch, out string helperVersion, out string wineVersion)
    {
        var magic = reader.ReadUInt32();
        var version = reader.ReadUInt16();
        var headerSize = reader.ReadUInt16();
        var helperBlock = reader.ReadInt32();
        var helperRate = reader.ReadInt32();
        var helperSlots = reader.ReadInt32();
        var arch = reader.ReadInt32();
        var flags = reader.ReadInt32();
        helperVersion = reader.ReadBoundedString(128);
        wineVersion = reader.ReadBoundedString(256);
        var token = reader.ReadBytes(32);
        var expected = Convert.FromHexString(tokenHex);
        if (magic != BridgeTcpHelloMagic || version != BridgeProtocolVersion || headerSize != BridgeHeaderSize ||
            helperBlock != maxBlock || helperRate != sampleRate || helperSlots != Math.Clamp(bufferSlots, 1, 2) ||
            arch != expectedArch || token.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(token, expected))
            throw new InvalidOperationException(
                $"The Windows plug-in helper refused the session (magic 0x{magic:X8}, version {version}, header {headerSize}, block {helperBlock}, rate {helperRate}, slots {helperSlots}, arch {arch} (expected {expectedArch}), flags {flags}, token {(token.Length == expected.Length && CryptographicOperations.FixedTimeEquals(token, expected) ? "ok" : "mismatch")}).");
    }
}
