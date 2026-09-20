namespace SimulatedDataApp.Models;

/// <summary>
/// 本机一个可用网络地址（绑定到具体网卡，避免多网卡场景混淆）。
/// </summary>
/// <param name="Name">网卡/接口名称，如 en0、以太网。</param>
/// <param name="Address">该网卡上的 IPv4 地址。</param>
/// <param name="HasGateway">是否配置了默认网关（通常表示该网卡当前可访问外网/局域网）。</param>
public sealed record LocalAddressInfo(string Name, string Address, bool HasGateway)
{
    public string Display => $"{Name}：{Address}";
}
