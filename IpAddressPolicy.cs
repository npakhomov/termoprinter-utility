using System.Net;
using System.Net.Sockets;

namespace ThermoPrinterTool;

internal readonly record struct IpRange(uint Start, uint End)
{
    public bool Contains(IPAddress address) => IpAddressPolicy.Number(address) is var value && value >= Start && value <= End;
}

internal static class IpAddressPolicy
{
    public const string ReservationAdvice = "Рекомендуется передать технику: включить DHCP на принтере и закрепить адрес за его MAC " +
        "на роутере. Если нужен статический IP, он должен быть исключен из DHCP-пула. Отсутствие ответа не гарантирует свободный адрес.";

    internal static uint Number(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("Требуется IPv4-адрес.");
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    internal static IPAddress Address(uint value) => new(new[]
        { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value });

    internal static IpRange Subnet(IPAddress address, IPAddress mask)
    {
        var inverse = ~Number(mask);
        if ((inverse & (inverse + 1UL)) != 0 || inverse < 3 || inverse == uint.MaxValue)
            throw new ArgumentException("Маска не задает поддерживаемую подсеть с адресами узлов.");
        var network = Number(address) & Number(mask);
        return new IpRange(network + 1, (network | inverse) - 1);
    }

    internal static IpRange? ParseDhcpPool(string start, string end, IpRange subnet)
    {
        if (string.IsNullOrWhiteSpace(start) && string.IsNullOrWhiteSpace(end))
            return null;
        if (!IPAddress.TryParse(start, out var first) || first.AddressFamily != AddressFamily.InterNetwork ||
            !IPAddress.TryParse(end, out var last) || last.AddressFamily != AddressFamily.InterNetwork ||
            !subnet.Contains(first) || !subnet.Contains(last) || Number(first) > Number(last))
            throw new ArgumentException("Укажите начало и конец DHCP-пула в выбранной подсети, например 192.168.1.100 и 192.168.1.254.");
        return new IpRange(Number(first), Number(last));
    }

    internal static IEnumerable<IPAddress> Candidates(IPAddress local, IPAddress mask,
        IpRange? dhcpPool, ISet<string> excluded)
    {
        var subnet = Subnet(local, mask);
        var localBlock = Number(local) & 0xFFFFFF00;
        var seen = new HashSet<uint>();
        var yielded = 0;
        foreach (var interval in new[] { new IpRange(Math.Max(subnet.Start, localBlock + 1),
                     Math.Min(subnet.End, localBlock + 254)), subnet })
        {
            for (var value = interval.Start; value <= interval.End; value++)
            {
                if (dhcpPool is { } pool && value >= pool.Start && value <= pool.End)
                {
                    value = Math.Min(pool.End, interval.End);
                    continue;
                }
                if (!seen.Add(value))
                    continue;
                var candidate = Address(value);
                if (excluded.Contains(candidate.ToString()) || candidate.Equals(local) || dhcpPool?.Contains(candidate) == true)
                    continue;
                yield return candidate;
                if (++yielded >= 512)
                    yield break;
            }
        }
    }
}
