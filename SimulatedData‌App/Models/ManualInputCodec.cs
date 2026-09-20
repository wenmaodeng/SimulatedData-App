using System.Globalization;
using System.Text;

namespace SimulatedDataApp.Models;

/// <summary>
/// 手动发送输入的编解码：字符串模式按 UTF-8 编码原样发送；
/// HEX 模式解析 16 进制文本为原始字节（字节间允许空格 / 制表符等空白分隔）。
/// </summary>
public static class ManualInputCodec
{
    /// <summary>
    /// 尝试把手动输入编码为待发送字节。
    /// </summary>
    /// <param name="input">用户输入文本。</param>
    /// <param name="isHex">true=16 进制输入；false=字符串输入。</param>
    /// <param name="payload">解析成功时的字节负载。</param>
    /// <param name="error">解析失败时的错误说明。</param>
    public static bool TryEncode(string input, bool isHex, out byte[] payload, out string error)
    {
        if (isHex)
            return TryParseHex(input, out payload, out error);

        payload = Encoding.UTF8.GetBytes(input);
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// 把字节数组格式化为空格分隔的 HEX 文本，用于日志展示，如 "48 65 6C 6C 6F"。
    /// </summary>
    public static string ToHexString(byte[] payload)
        => string.Join(" ", payload.Select(static b => b.ToString("X2", CultureInfo.InvariantCulture)));

    private static bool TryParseHex(string input, out byte[] payload, out string error)
    {
        // 允许空格等空白作为字节分隔符，发送前统一清洗（不在输入阶段阻止用户键入）。
        var hex = new string(input.Where(static c => !char.IsWhiteSpace(c)).ToArray());

        if (hex.Length == 0)
        {
            payload = [];
            error = "没有有效的 16 进制字符。";
            return false;
        }

        if (hex.Length % 2 != 0)
        {
            payload = [];
            error = $"16 进制字符数必须为偶数，当前为 {hex.Length} 个（请检查是否漏写了一位）。";
            return false;
        }

        payload = new byte[hex.Length / 2];
        for (var i = 0; i < payload.Length; i++)
        {
            var segment = hex.AsSpan(i * 2, 2);
            if (!byte.TryParse(segment, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out payload[i]))
            {
                error = $"不是有效的 16 进制字节：\"{segment.ToString()}\"（只允许 0-9、A-F）。";
                payload = [];
                return false;
            }
        }

        error = string.Empty;
        return true;
    }
}
