using System.Globalization;
using System.Text;

namespace SimulatedDataApp.Models;

/// <summary>
/// 国标 GPS KSXT 帧的构建器。
/// 根据 1-26 号信号开关组合出 judgeSignal1 / judgeSignal2 字段，
/// 填充空白 GPS 帧并计算 NMEA XOR 校验。
/// </summary>
public static class GbGpsFrameBuilder
{
    /// <summary>信号编号 1-26（对应 HsIoSignalMap）。</summary>
    public const int SignalCount = 26;

    /// <summary>judgeSignal2 中的 Pile（桩杆）占 3 bit。</summary>
    public const int PileMask = 0x07;

    // 信号 1-21、23-26 的布尔位在两个 judge 字中的位置。
    // key 为 HsIoSignalMap 的 1-based 下标；value 为 (judgeIndex, bit)
    // judgeIndex: 0 = judgeSignal1, 1 = judgeSignal2
    private static readonly Dictionary<int, (int judge, int bit)> BoolSignalBits = new()
    {
        [1]  = (0, 0),   // CarDoor
        [2]  = (0, 1),   // SafetyBelt
        [4]  = (0, 2),   // Stall
        [3]  = (0, 3),   // Firewitch
        [5]  = (0, 4),   // TurnLeft
        [6]  = (0, 5),   // TurnRight
        [7]  = (0, 6),   // LowBeamHeadlight
        [8]  = (0, 7),   // HighBeamHeadlight
        [9]  = (0, 8),   // HandBrake
        [10] = (0, 9),   // SpeedBrake
        [11] = (0, 10),  // Clutch
        [12] = (0, 11),  // AuxiliaryBrake
        [13] = (0, 12),  // Horn
        [14] = (0, 13),  // Wiper
        [15] = (0, 14),  // HazardWarningLamp
        [16] = (0, 15),  // PositionLamp
        [18] = (1, 3),   // LeftUnilateralBridgeSignal1
        [19] = (1, 4),   // LeftUnilateralBridgeSignal2
        [20] = (1, 5),   // RightUnilateralBridgeSignal1
        [21] = (1, 6),   // RightUnilateralBridgeSignal2
        [17] = (1, 7),   // FogLamp
        [23] = (1, 11),  // LeftRearDetour
        [24] = (1, 12),  // RightRearDetour
        [25] = (1, 13),  // RightFrontDetour
        [26] = (1, 14),  // LeftFrontDetour
    };

    /// <summary>
    /// 根据信号字典构造一整条 KSXT 帧。
    /// </summary>
    /// <param name="signals">信号编号(1-based) → 布尔值。仅传入布尔信号即可。</param>
    /// <param name="gear">挡位 (judgeSignal2 bit 0-2, 取值 0-7)。</param>
    /// <param name="pile">桩杆 (judgeSignal2 bit 8-10, 取值 0-7)。</param>
    /// <returns>完整的 KSXT 帧（不含结尾 CRLF，由传输层追加）。</returns>
    public static string Build(
        IReadOnlyDictionary<int, bool> signals,
        int gear = 0,
        int pile = 0)
    {
        int judge1 = 0;
        int judge2 = gear & 7; // bit 0-2 是挡位

        foreach (var kv in signals)
        {
            if (kv.Key < 1 || kv.Key > SignalCount) continue;
            if (!BoolSignalBits.TryGetValue(kv.Key, out var pos)) continue;
            if (!kv.Value) continue;

            if (pos.judge == 0)
                judge1 |= 1 << pos.bit;
            else
                judge2 |= 1 << pos.bit;
        }

        // Pile 桩杆占 bit 8-10
        judge2 |= (pile & PileMask) << 8;

        var now = DateTime.Now;
        var time = now.ToString("yyyyMMddHHmmss.ff", CultureInfo.InvariantCulture);

        // 固定空白 GPS 数据（来自实际数据样本的占位值）
        const string baseFrame =
            "$KSXT,{0},117.25322830,31.71717720,25.7050,115.84,0.09,270.00,0.1,0.80,3,3,18,21,67.575,35.774,-15.137,0.032,0.025,-0.029,{1:X4},{2:X4},0";

        var body = string.Format(CultureInfo.InvariantCulture, baseFrame, time, judge1, judge2);
        var checksum = ComputeNmeaChecksum(body.AsSpan(1)); // 从 $ 之后到 * 之前
        return $"{body}*{checksum:X2}";
    }

    /// <summary>
    /// 从 1-based 信号编号和布尔值字典快速构造一帧。
    /// </summary>
    public static string Build(Dictionary<int, bool> signals, int gear, int pile)
        => Build((IReadOnlyDictionary<int, bool>)signals, gear, pile);

    /// <summary>NMEA XOR 校验：对 $ 与 * 之间（不含 $,*）的所有字节做异或。</summary>
    public static byte ComputeNmeaChecksum(ReadOnlySpan<char> body)
    {
        byte xor = 0;
        foreach (var c in body)
            xor ^= (byte)c;
        return xor;
    }

    /// <summary>校验一整条 KSXT 帧的 checksum 是否匹配。</summary>
    public static bool Validate(string frame)
    {
        if (string.IsNullOrEmpty(frame)) return false;
        var star = frame.LastIndexOf('*');
        if (star <= 0) return false;

        var body = frame.AsSpan(0, star);
        if (!body.StartsWith("$KSXT", StringComparison.Ordinal)) return false;

        var checksumText = frame.AsSpan(star + 1);
        if (checksumText.Length != 2) return false;

        if (!byte.TryParse(checksumText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte expected))
            return false;

        return ComputeNmeaChecksum(body.Slice(1)) == expected;
    }
}
