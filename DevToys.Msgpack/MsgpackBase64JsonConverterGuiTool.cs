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
    IconFontName = "FluentSystemIcons", // This font is available by default in DevToys
    IconGlyph = '\uE670', // An icon that represents a pizza
    GroupName = PredefinedCommonToolGroupNames.Converters, // The group in which the tool will appear in the side bar.
    ResourceManagerAssemblyIdentifier =
        nameof(DevToysMsgpackResourceAssemblyIdentifier), // The Resource Assembly Identifier to use
    ResourceManagerBaseName =
        "DevToys.Msgpack.DevToysMsgpackResources", // The full name (including namespace) of the resource file containing our localized texts
    ShortDisplayTitleResourceName =
        nameof(DevToysMsgpackResources
            .ShortDisplayTitle), // The name of the resource to use for the short display title
    LongDisplayTitleResourceName = nameof(DevToysMsgpackResources.LongDisplayTitle),
    DescriptionResourceName = nameof(DevToysMsgpackResources.Description),
    AccessibleNameResourceName = nameof(DevToysMsgpackResources.AccessibleName))]
internal sealed class MsgpackBase64JsonConverterGuiTool : IGuiTool
{
    private const string JsonLanguage = "json";
    private const string Base64Text = "b64";

    /// <summary>
    /// </summary>
    private static readonly SettingDefinition<MsgpackBase64ToJsonConversion> ConversionMode
        = new(name: $"{nameof(MsgpackBase64JsonConverterGuiTool)}.{nameof(ConversionMode)}",
            defaultValue: MsgpackBase64ToJsonConversion.MsgpackBase64ToJson);

    /// <summary>
    /// </summary>
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
    private readonly IUIMultiLineTextInput _inputTextArea = MultiLineTextInput("msgpack-b64-to-json-input-text-area");
    private readonly IUIMultiLineTextInput _outputTextArea = MultiLineTextInput("msgpack-b64-to-json-output-text-area");

    private CancellationTokenSource? _cancellationTokenSource;

    [ImportingConstructor]
    public MsgpackBase64JsonConverterGuiTool(ISettingsProvider settingsProvider)
    {
        _logger = this.Log();
        _settingsProvider = settingsProvider;

        switch (_settingsProvider.GetSetting(ConversionMode))
        {
            case MsgpackBase64ToJsonConversion.JsonToMsgpackBase64:
                SetJsonToMsgpackBase64Conversion();
                break;
            case MsgpackBase64ToJsonConversion.MsgpackBase64ToJson:
                SetMsgpackBase64ToJsonConversion();
                break;
            default:
                throw new NotSupportedException();
        }
    }

    internal Task? WorkTask { get; private set; }

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
                                .Text("Configuration"),
                            Setting("msgpack-b64-to-json-text-conversion-setting")
                                .Icon("FluentSystemIcons", '\uF18D')
                                .Title(DevToysMsgpackResources.ConversionTitle)
                                .Description(DevToysMsgpackResources.ConversionDescription)
                                .Handle(
                                    _settingsProvider,
                                    ConversionMode,
                                    OnConversionModeChanged,
                                    Item(DevToysMsgpackResources.MsgpackBase64ToJson,
                                        MsgpackBase64ToJsonConversion.MsgpackBase64ToJson),
                                    Item(DevToysMsgpackResources.JsonToMsgpackBase64,
                                        MsgpackBase64ToJsonConversion.JsonToMsgpackBase64)
                                ),
                            Setting("msgpack-b64-to-json-text-indentation-setting")
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
                                    .OnTextChanged(OnInputTextChanged))
                            .WithRightPaneChild(
                                _outputTextArea
                                    .Title(DevToysMsgpackResources.Output)
                                    .ReadOnly()
                                    .Extendable())
                    )
                )
        );

    // Smart detection handler.
    public void OnDataReceived(string dataTypeName, object? parsedData)
    {
        if (dataTypeName == PredefinedCommonDataTypeNames.Json &&
            parsedData is string jsonStrongTypedParsedData)
        {
            _inputTextArea.Language(JsonLanguage);
            _outputTextArea.Language(Base64Text);
            _inputTextArea.Text(jsonStrongTypedParsedData);
        }

        if (dataTypeName == PredefinedCommonDataTypeNames.Base64Text &&
            parsedData is string yamlStrongTypedParsedData)
        {
            _inputTextArea.Language(Base64Text);
            _outputTextArea.Language(JsonLanguage);
            _inputTextArea.Text(yamlStrongTypedParsedData);
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _semaphore.Dispose();
    }

    private void OnConversionModeChanged(MsgpackBase64ToJsonConversion conversionMode)
    {
        switch (conversionMode)
        {
            case MsgpackBase64ToJsonConversion.JsonToMsgpackBase64:
                SetJsonToMsgpackBase64Conversion();
                break;
            case MsgpackBase64ToJsonConversion.MsgpackBase64ToJson:
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

    private void StartConvert(string text)
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();

        WorkTask = ConvertAsync(text, _cancellationTokenSource.Token);
    }

    private async Task ConvertAsync(string input, CancellationToken cancellationToken)
    {
        using (await _semaphore.WaitAsync(cancellationToken))
        {
            await TaskSchedulerAwaiter.SwitchOffMainThreadAsync(cancellationToken);

            string conversionResult;

            switch (_settingsProvider.GetSetting(ConversionMode))
            {
                case MsgpackBase64ToJsonConversion.JsonToMsgpackBase64:
                    conversionResult
                        = Base64Helper.FromTextToBase64(
                            input,
                            DefaultEncoding,
                            _logger,
                            cancellationToken);

                    break;

                case MsgpackBase64ToJsonConversion.MsgpackBase64ToJson:
                    // if (!string.IsNullOrEmpty(input) && !Base64Helper.IsBase64DataStrict(input))
                    if (string.IsNullOrEmpty(input) && Base64Helper.IsBase64DataStrict(input))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        _outputTextArea.Text("Invalid Base64");
                        return;
                    }
                   
                    byte[] bytes = Base64Helper.FromBase64ToBytes(input, DefaultEncoding, _logger, cancellationToken);
                    conversionResult = MessagePackSerializer.ConvertToJson(bytes, null, cancellationToken);
                    break;

                default:
                    throw new NotSupportedException();
            }

            cancellationToken.ThrowIfCancellationRequested();
            _outputTextArea.Text(conversionResult);
        }
    }

    private void SetJsonToMsgpackBase64Conversion()
    {
        _inputTextArea.Language(JsonLanguage);
        _outputTextArea.Language(Base64Text);
    }

    private void SetMsgpackBase64ToJsonConversion()
    {
        _inputTextArea.Language(Base64Text);
        _outputTextArea.Language(JsonLanguage);
    }
}