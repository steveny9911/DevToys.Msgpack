using System.Text.RegularExpressions;
using DevToys.Msgpack.Models;
using Microsoft.Extensions.Logging;

namespace DevToys.Msgpack.Helpers;

internal static partial class Base64Helper
{
    internal static byte[]? FromBase64ToBytes(string data, Base64Encoding encoding, ILogger logger, CancellationToken ct)
    {
        data = data!.Trim();
        if (data.Length % 4 != 0)
        {
            return null;
        }
        
        if (Base64Regex().IsMatch(data))
        {
            return null;
        }
        
        int equalIndex = data.IndexOf('=');
        int length = data.Length;
        if (!(equalIndex == -1 || equalIndex == length - 1 || equalIndex == length - 2 && data[length - 1] == '='))
        {
            return null;
        }

        int remainder = data!.Length % 4;
        if (remainder > 0)
        {
            int padding = 4 - remainder;
            data = data.PadRight(data.Length + padding, '=');
        }

        try
        {
            byte[] bytes = Convert.FromBase64String(data);
            ct.ThrowIfCancellationRequested();
            return bytes;
        }
        catch (Exception ex) when (ex is OperationCanceledException or FormatException)
        {
            // ignore;
        }
        catch (Exception ex)
        {
            LogFailDecodeBase64(logger, ex, encoding);
        }

        return null;
    }
    
    internal static string FromBytesToBase64(byte[]? data, Base64Encoding encoding, ILogger logger, CancellationToken cancellationToken)
    {
        if (data == null || data.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            string encodedData = Convert.ToBase64String(data);
            cancellationToken.ThrowIfCancellationRequested();
            return encodedData;
        }
        catch (Exception ex) when (ex is OperationCanceledException)
        {
            // ignore;
        }
        catch (Exception ex)
        {
            LogFailEncodeBase64(logger, ex, encoding);
        }

        return string.Empty;
    }

    private static void LogFailEncodeBase64(ILogger logger, Exception exception, Base64Encoding encoding)
    {
        logger.LogError(0, exception, "Failed to encode text to Base64. Encoding mode: '{encoding}'", encoding);
    }

    private static void LogFailDecodeBase64(ILogger logger, Exception exception, Base64Encoding encoding)
    {
        logger.LogError(1, exception, "Failed to decode Base64t to text. Encoding mode: '{encoding}'", encoding);
    }

    [GeneratedRegex("[^A-Z0-9+/=]", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex Base64Regex();
}