using System.Net;

namespace ThermoPrinterTool;

internal static class HprtEthernetProtocol
{
    private const byte PrinterNumber = 1;
    private const ushort DhcpCommand = 100;
    private const ushort IpCommand = 101;
    private const ushort MaskCommand = 102;
    private const ushort GatewayCommand = 103;

    public static byte[] BuildDhcp(bool enabled) =>
        BuildSetKey(DhcpCommand, new[] { enabled ? (byte)2 : (byte)1 });

    public static byte[] BuildIp(string value) => BuildSetKey(IpCommand, ParseIpv4(value));

    public static byte[] BuildMask(string value) => BuildSetKey(MaskCommand, ParseIpv4(value));

    public static byte[] BuildGateway(string value) => BuildSetKey(GatewayCommand, ParseIpv4(value));

    private static byte[] BuildSetKey(ushort command, byte[] payload)
    {
        var packet = new byte[19 + payload.Length];
        byte[] header =
        {
            0x0D, 0x0A,
            0x1B, 0x1C, 0x26, 0x20, 0x56, 0x31, 0x20,
            0x73, 0x65, 0x74, 0x6B, 0x65, 0x79
        };

        Buffer.BlockCopy(header, 0, packet, 0, header.Length);
        packet[15] = PrinterNumber;
        packet[16] = (byte)(command & 0xFF);
        packet[17] = (byte)(command >> 8);
        packet[18] = checked((byte)payload.Length);
        Buffer.BlockCopy(payload, 0, packet, 19, payload.Length);
        return packet;
    }

    private static byte[] ParseIpv4(string value)
    {
        if (!IPAddress.TryParse(value, out var address) ||
            address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            throw new ArgumentException($"Invalid IPv4 address: {value}", nameof(value));
        }

        return address.GetAddressBytes();
    }
}
