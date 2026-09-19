using System.Text;

namespace MinGo.MyBillBook.Core.Parsing;

/// <summary>
/// 文件字符编码检测工具。
/// 优先通过 BOM 判断，无 BOM 时通过启发式方法区分 UTF-8 与 GBK。
/// </summary>
public static class EncodingDetector
{
    private static readonly Encoding GBK;

    static EncodingDetector()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        GBK = Encoding.GetEncoding("GBK");
    }

    /// <summary>
    /// 检测流的字符编码。检测后流位置会重置到起始位置。
    /// </summary>
    public static Encoding Detect(Stream stream)
    {
        if (!stream.CanSeek)
            throw new ArgumentException("流必须支持 Seek 以进行编码检测", nameof(stream));

        var originalPos = stream.Position;
        try
        {
            // 读取前 8KB 用于检测
            var buffer = new byte[Math.Min(8192, stream.Length - stream.Position)];
            _ = stream.Read(buffer, 0, buffer.Length);

            if (buffer.Length == 0)
                return Encoding.UTF8;

            // 1. BOM 检测
            if (buffer.Length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
                return Encoding.UTF8;
            if (buffer.Length >= 2 && buffer[0] == 0xFF && buffer[1] == 0xFE)
                return Encoding.Unicode; // UTF-16 LE
            if (buffer.Length >= 2 && buffer[0] == 0xFE && buffer[1] == 0xFF)
                return Encoding.BigEndianUnicode; // UTF-16 BE

            // 2. 启发式检测：尝试按 UTF-8 解码，如果出现替换字符则判定为 GBK
            if (IsValidUtf8(buffer))
                return Encoding.UTF8;

            return GBK;
        }
        finally
        {
            stream.Position = originalPos;
        }
    }

    /// <summary>
    /// 判断字节数组是否为合法的 UTF-8 编码。
    /// 纯 ASCII 内容也视为合法 UTF-8。
    /// </summary>
    private static bool IsValidUtf8(byte[] buffer)
    {
        var decoder = Encoding.UTF8.GetDecoder();
        decoder.Fallback = DecoderExceptionFallback.ExceptionFallback;

        try
        {
            // UTF-8 解码后字符数不会超过字节数，分配等大的 char 缓冲区
            var chars = new char[buffer.Length];
            int byteIndex = 0;
            while (byteIndex < buffer.Length)
            {
                int byteCount = Math.Min(buffer.Length - byteIndex, 4096);
                bool flush = (byteIndex + byteCount >= buffer.Length);
                decoder.Convert(buffer, byteIndex, byteCount, chars, 0, chars.Length, flush, out int used, out _, out _);
                byteIndex += used;
            }
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
