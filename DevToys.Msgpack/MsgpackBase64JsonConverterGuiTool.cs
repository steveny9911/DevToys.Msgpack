using System.ComponentModel.Composition;
using DevToys.Api;
using DevToys.Msgpack.Helpers;
using DevToys.Msgpack.Models;
using MessagePack;
using Microsoft.Extensions.Logging;
using static DevToys.Api.GUI;

namespace DevToys.Msgpack;

[Export(typeof(IGuiTool))]
[Name("MsgpackJsonConverter")] // A unique, internal name of the tool.
[ToolDisplayInformation(
    IconFontName = "FluentSystemIcons",
    IconGlyph = '\uE7C6', // ic_fluent_mail_multiple_20_regular
    GroupName = PredefinedCommonToolGroupNames.EncodersDecoders,
    ResourceManagerAssemblyIdentifier = nameof(DevToysMsgpackResourceAssemblyIdentifier),
    ResourceManagerBaseName = "DevToys.Msgpack.DevToysMsgpackResources",
    ShortDisplayTitleResourceName = nameof(DevToysMsgpackResources.ShortDisplayTitle),
    LongDisplayTitleResourceName = nameof(DevToysMsgpackResources.LongDisplayTitle),
    DescriptionResourceName = nameof(DevToysMsgpackResources.Description),
    AccessibleNameResourceName = nameof(DevToysMsgpackResources.AccessibleName))]
internal sealed class MsgpackBase64JsonConverterGuiTool : IGuiTool, IDisposable
{
    private const string JsonLanguage = PredefinedCommonDataTypeNames.Json;
    private const string Base64Text = PredefinedCommonDataTypeNames.Base64Text;

    private static readonly SettingDefinition<MsgpackBase64JsonConversion> ConversionMode
        = new(name: $"{nameof(MsgpackBase64JsonConverterGuiTool)}.{nameof(ConversionMode)}",
            defaultValue: MsgpackBase64JsonConversion.MsgpackBase64ToJson);

    private static readonly SettingDefinition<Indentation> IndentationMode
        = new(name: $"{nameof(MsgpackBase64JsonConverterGuiTool)}.{nameof(IndentationMode)}",
            defaultValue: Indentation.TwoSpaces);

    private enum GridColumn
    {
        Content
    }

    private enum GridRow
    {
        Header,
        Content,
        Footer
    }

    private const Base64Encoding DefaultEncoding = Base64Encoding.Utf8;
    private readonly DisposableSemaphore _semaphore = new();
    private readonly ILogger _logger;
    private readonly ISettingsProvider _settingsProvider;
    private readonly IUIMultiLineTextInput _inputTextArea = MultiLineTextInput("msgpack-b64-json-input-text-area");
    private readonly IUIMultiLineTextInput _outputTextArea = MultiLineTextInput("msgpack-b64-json-output-text-area");

    private CancellationTokenSource? _cancellationTokenSource;
    internal Task? WorkTask { get; private set; }

    [ImportingConstructor]
    public MsgpackBase64JsonConverterGuiTool(ISettingsProvider settingsProvider)
    {
        _logger = this.Log();
        _settingsProvider = settingsProvider;

        OnConversionModeChanged(_settingsProvider.GetSetting(ConversionMode));
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _semaphore.Dispose();
    }

    public UIToolView View
        => new(
            isScrollable: true,
            Grid()
                .ColumnLargeSpacing()
                .RowLargeSpacing()
                .Rows(
                    (GridRow.Header, Auto),
                    (GridRow.Content, new UIGridLength(1, UIGridUnitType.Fraction))
                )
                .Columns(
                    (GridColumn.Content, new UIGridLength(1, UIGridUnitType.Fraction))
                )
                .Cells(
                    Cell(
                        GridRow.Header,
                        GridColumn.Content,
                        Stack().Vertical().WithChildren(
                            Label()
                                .Text(DevToysMsgpackResources.Configuration),
                            Setting("msgpack-b64-json-text-conversion-setting")
                                .Icon("FluentSystemIcons", '\uF18D')
                                .Title(DevToysMsgpackResources.ConversionTitle)
                                .Description(DevToysMsgpackResources.ConversionDescription)
                                .Handle(
                                    _settingsProvider,
                                    ConversionMode,
                                    OnConversionModeChanged,
                                    Item(DevToysMsgpackResources.MsgpackBase64ToJson,
                                        MsgpackBase64JsonConversion.MsgpackBase64ToJson),
                                    Item(DevToysMsgpackResources.JsonToMsgpackBase64,
                                        MsgpackBase64JsonConversion.JsonToMsgpackBase64)
                                ),
                            Setting("msgpack-b64-json-text-indentation-setting")
                                .Icon("FluentSystemIcons", '\uF6F8')
                                .Title(DevToysMsgpackResources.Indentation)
                                .Handle(
                                    _settingsProvider,
                                    IndentationMode,
                                    OnIndentationModelChanged,
                                    Item(DevToysMsgpackResources.TwoSpaces, Indentation.TwoSpaces),
                                    Item(DevToysMsgpackResources.FourSpaces, Indentation.FourSpaces),
                                    Item(DevToysMsgpackResources.OneTab, Indentation.OneTab),
                                    Item(DevToysMsgpackResources.Minified, Indentation.Minified)
                                )
                        )
                    ),
                    Cell(
                        GridRow.Content,
                        GridColumn.Content,
                        SplitGrid()
                            .Vertical()
                            .WithLeftPaneChild(
                                _inputTextArea
                                    .Title(DevToysMsgpackResources.Input)
                                    .OnTextChanged(OnInputTextChanged)
                            )
                            .WithRightPaneChild(
                                _outputTextArea
                                    .Title(DevToysMsgpackResources.Output)
                                    .ReadOnly()
                                    .Extendable()
                            )
                    )
                )
        );

    // Smart detection handler.
    public void OnDataReceived(string dataTypeName, object? parsedData)
    {
        switch (dataTypeName)
        {
            case JsonLanguage when parsedData is string jsonString:
                SetJsonToMsgpackBase64Conversion();
                _inputTextArea.Text(jsonString);
                break;
            case Base64Text when parsedData is string base64String:
                SetMsgpackBase64ToJsonConversion();
                _inputTextArea.Text(base64String);
                break;
        }
    }

    private void OnConversionModeChanged(MsgpackBase64JsonConversion conversionMode)
    {
        switch (conversionMode)
        {
            case MsgpackBase64JsonConversion.JsonToMsgpackBase64:
                SetJsonToMsgpackBase64Conversion();
                break;
            case MsgpackBase64JsonConversion.MsgpackBase64ToJson:
                SetMsgpackBase64ToJsonConversion();
                break;
            default:
                throw new NotSupportedException();
        }

        _inputTextArea.Text(_outputTextArea.Text);
    }

    private void OnIndentationModelChanged(Indentation indentationMode)
    {
        StartConvert(_inputTextArea.Text);
    }

    private void OnInputTextChanged(string text)
    {
        StartConvert(text);
    }

    private void StartConvert(string input)
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();

        WorkTask = ConvertAsync(input, _cancellationTokenSource.Token);
    }

    private async Task ConvertAsync(string input, CancellationToken ct)
    {
        using (await _semaphore.WaitAsync(ct))
        {
            await TaskSchedulerAwaiter.SwitchOffMainThreadAsync(ct);

            string result = await Convert(input, ct);

            ct.ThrowIfCancellationRequested();
            _outputTextArea.Text(result);
        }
    }

    private async Task<string> Convert(string input, CancellationToken ct)
    {
        switch (_settingsProvider.GetSetting(ConversionMode))
        {
            case MsgpackBase64JsonConversion.JsonToMsgpackBase64:
            {
                byte[] bytes = MessagePackSerializer.ConvertFromJson(input, null, ct);
                return Base64Helper.FromBytesToBase64(bytes, DefaultEncoding, _logger, ct);
            }
            case MsgpackBase64JsonConversion.MsgpackBase64ToJson:
            {
                if (string.IsNullOrWhiteSpace(input) || string.IsNullOrEmpty(input))
                {
                    return string.Empty;
                }

                byte[]? bytes = Base64Helper.FromBase64ToBytes(input, DefaultEncoding, _logger, ct);
                if (bytes == null)
                {
                    return DevToysMsgpackResources.InvalidBase64;
                }

                string? rawJson;
                try
                {
                    rawJson = MessagePackSerializer.ConvertToJson(bytes, null, ct);
                }
                catch (MessagePackSerializationException)
                {
                    return DevToysMsgpackResources.InvalidJson;
                }

                string? formattedJson = await JsonHelpers.FormatAsync(rawJson,
                    _settingsProvider.GetSetting(IndentationMode), _logger, ct);
                return formattedJson ?? rawJson;
            }
            default:
                throw new NotSupportedException();
        }
    }

    private void SetJsonToMsgpackBase64Conversion()
    {
        _inputTextArea.Language(JsonLanguage).AutoWrap();
        _outputTextArea.Language(Base64Text).AlwaysWrap();
    }

    private void SetMsgpackBase64ToJsonConversion()
    {
        _inputTextArea.Language(Base64Text).AlwaysWrap();
        _outputTextArea.Language(JsonLanguage).AutoWrap();
    }
}