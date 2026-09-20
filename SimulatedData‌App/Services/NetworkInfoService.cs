using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using SimulatedDataApp.Models;

namespace SimulatedDataApp.Services;

/// <summary>
/// 枚举本机当前可用网络的 IPv4 地址。
/// 多网卡（Wi-Fi / 以太网 / VPN / 虚拟网桥）时逐个返回并绑定网卡名，
/// 配置了默认网关的网卡（通常是当前实际联网的网卡）排在最前。
/// </summary>
public static class NetworkInfoService
{
    public static IReadOnlyList<LocalAddressInfo> GetLocalIPv4Addresses()
    {
        var result = new List<LocalAddressInfo>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            // 只看处于启用状态的网卡；排除回环接口（VPN 等 Tunnel 接口保留，
            // 某些环境下 VPN 可能是唯一可达路径）。
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            IPInterfaceProperties properties;
            try
            {
                properties = nic.GetIPProperties();
            }
            catch
            {
                continue;
            }

            var hasGateway = properties.GatewayAddresses
                .Select(static g => g.Address)
                .Any(static a => a.AddressFamily == AddressFamily.InterNetwork
                                 && !IPAddress.IsLoopback(a));

            foreach (var address in properties.UnicastAddresses)
            {
                var ip = address.Address;
                if (ip.AddressFamily != AddressFamily.InterNetwork)
                    continue;

                // 排除回环段和链路本地 169.254/16（DHCP 失败时的自配置地址）。
                if (IPAddress.IsLoopback(ip))
                    continue;
                if (ip.IsIPv4LinkLocal())
                    continue;

                result.Add(new LocalAddressInfo(nic.Name, ip.ToString(), hasGateway));
            }
        }

        // 有默认网关的网卡优先，其次物理网卡，最后按接口名稳定排序。
        return result
            .OrderByDescending(static a => a.HasGateway)
            .ThenBy(static a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

internal static class IPAddressExtensions
{
    /// <summary>是否为 IPv4 链路本地地址（169.254.0.0/16）。</summary>
    public static bool IsIPv4LinkLocal(this IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }
}
