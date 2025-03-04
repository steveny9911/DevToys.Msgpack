using System.Text;
using DevToys.Msgpack.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DevToys.Msgpack.Helpers;

public static class JsonHelpers
{
    internal static async Task<string?> FormatAsync(string? input, Indentation indentationMode, ILogger logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        try
        {
            JsonLoadSettings jsonLoadSettings = new JsonLoadSettings()
            {
                CommentHandling = CommentHandling.Ignore,
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Ignore,
                LineInfoHandling = LineInfoHandling.Load
            };

            JToken jToken;
            await using (JsonTextReader jsonReader = new JsonTextReader(new StringReader(input)))
            {
                jsonReader.DateParseHandling = DateParseHandling.None;
                jsonReader.DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind;

                jToken = await JToken.LoadAsync(jsonReader, jsonLoadSettings, ct);
            }

            StringBuilder stringBuilder = new StringBuilder();
            await using (StringWriter stringWriter = new StringWriter(stringBuilder))
            await using (JsonTextWriter jsonTextWriter = new JsonTextWriter(stringWriter))
            {
                switch (indentationMode)
                {
                    case Indentation.TwoSpaces:
                        jsonTextWriter.Formatting = Formatting.Indented;
                        jsonTextWriter.IndentChar = ' ';
                        jsonTextWriter.Indentation = 2;
                        break;
                    case Indentation.FourSpaces:
                        jsonTextWriter.Formatting = Formatting.Indented;
                        jsonTextWriter.IndentChar = ' ';
                        jsonTextWriter.Indentation = 4;
                        break;
                    case Indentation.OneTab:
                        jsonTextWriter.Formatting = Formatting.Indented;
                        jsonTextWriter.IndentChar = '\t';
                        jsonTextWriter.Indentation = 1;
                        break;
                    case Indentation.Minified:
                        jsonTextWriter.Formatting = Formatting.None;
                        break;
                    default:
                        throw new NotSupportedException();
                }

                jsonTextWriter.DateFormatHandling = DateFormatHandling.IsoDateFormat;
                jsonTextWriter.DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind;

                await jToken.WriteToAsync(jsonTextWriter, ct);
            }

            return stringBuilder.ToString();
        }
        catch (JsonReaderException ex)
        {
            logger.LogError(ex, "Invalid JSON format '{indentationMode}'", indentationMode);
            return null;
        }
    }
}