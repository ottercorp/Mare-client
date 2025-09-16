using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using MareSynchronos.FileCache;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.Localization;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.SignalR;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MareSynchronos.UI;

public partial class UiSharedService : DisposableMediatorSubscriberBase
{
    public const string TooltipSeparator = "------";
    public static readonly ImGuiWindowFlags PopupWindowFlags = ImGuiWindowFlags.NoResize |
                                               ImGuiWindowFlags.NoScrollbar |
                                           ImGuiWindowFlags.NoScrollWithMouse;

    public readonly FileDialogManager FileDialogManager;
    private const string _notesEnd = "##MARE_SYNCHRONOS_USER_NOTES_END##";
    private const string _notesStart = "##MARE_SYNCHRONOS_USER_NOTES_START##";
    private readonly ApiController _apiController;
    private readonly CacheMonitor _cacheMonitor;
    private readonly MareConfigService _configService;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly IpcManager _ipcManager;
    private readonly Dalamud.Localization _localization;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly Dictionary<string, object?> _selectedComboItems = new(StringComparer.Ordinal);
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly ITextureProvider _textureProvider;
    private readonly TokenProvider _tokenProvider;
    private bool _brioExists = false;
    private bool _chatTwoExists = false;
    private bool _cacheDirectoryHasOtherFilesThanCache = false;
    private bool _cacheDirectoryIsValidPath = true;
    private bool _customizePlusExists = false;
    private string _customServerName = "";
    private string _customServerUri = "";
    private Task<Uri?>? _discordOAuthCheck;
    private Task<string?>? _discordOAuthGetCode;
    private CancellationTokenSource _discordOAuthGetCts = new();
    private Task<Dictionary<string, string>>? _discordOAuthUIDs;
    private bool _glamourerExists = false;
    private bool _heelsExists = false;
    private bool _honorificExists = false;
    private bool _isDirectoryWritable = false;
    private bool _isOneDrive = false;
    private bool _isPenumbraDirectory = false;
    private bool _moodlesExists = false;
    private Dictionary<string, DateTime> _oauthTokenExpiry = new();
    private bool _penumbraExists = false;
    private bool _petNamesExists = false;
    private int _serverSelectionIndex = -1;
    private static List<string> _supporters = new();

    public UiSharedService(ILogger<UiSharedService> logger, IpcManager ipcManager, ApiController apiController,
        CacheMonitor cacheMonitor, FileDialogManager fileDialogManager,
        MareConfigService configService, DalamudUtilService dalamudUtil, IDalamudPluginInterface pluginInterface,
        ITextureProvider textureProvider,
        Dalamud.Localization localization,
        ServerConfigurationManager serverManager, TokenProvider tokenProvider, MareMediator mediator) : base(logger, mediator)
    {
        _ipcManager = ipcManager;
        _apiController = apiController;
        _cacheMonitor = cacheMonitor;
        FileDialogManager = fileDialogManager;
        _configService = configService;
        _dalamudUtil = dalamudUtil;
        _pluginInterface = pluginInterface;
        _textureProvider = textureProvider;
        _localization = localization;
        _serverConfigurationManager = serverManager;
        _tokenProvider = tokenProvider;
        _localization.SetupWithLangCode("en");

        _isDirectoryWritable = IsDirectoryWritable(_configService.Current.CacheFolder);

        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (_) =>
        {
            _penumbraExists = _ipcManager.Penumbra.APIAvailable;
            _glamourerExists = _ipcManager.Glamourer.APIAvailable;
            _customizePlusExists = _ipcManager.CustomizePlus.APIAvailable;
            _heelsExists = _ipcManager.Heels.APIAvailable;
            _honorificExists = _ipcManager.Honorific.APIAvailable;
            _moodlesExists = _ipcManager.Moodles.APIAvailable;
            _petNamesExists = _ipcManager.PetNames.APIAvailable;
            _brioExists = _ipcManager.Brio.APIAvailable;
            _chatTwoExists = _ipcManager.ChatTwo.APIAvailable;
        });

        UidFont = _pluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e =>
        {
            e.OnPreBuild(tk => tk.AddDalamudAssetFont(Dalamud.DalamudAsset.NotoSansJpMedium, new()
            {
                SizePx = 24
            }));
        });
        GameFont = _pluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(new(GameFontFamilyAndSize.Axis14));
        IconFont = _pluginInterface.UiBuilder.IconFontFixedWidthHandle;
    }

    public static string DoubleNewLine => Environment.NewLine + Environment.NewLine;
    public ApiController ApiController => _apiController;

    public bool EditTrackerPosition { get; set; }

    public IFontHandle GameFont { get; init; }
    public bool HasValidPenumbraModPath => !(_ipcManager.Penumbra.ModDirectory ?? string.Empty).IsNullOrEmpty() && Directory.Exists(_ipcManager.Penumbra.ModDirectory);

    public IFontHandle IconFont { get; init; }
    public bool IsInGpose => _dalamudUtil.IsInGpose;

    public Dictionary<uint, string> JobData => _dalamudUtil.JobData.Value;
    public string PlayerName => _dalamudUtil.GetPlayerName();

    public IFontHandle UidFont { get; init; }
    public Dictionary<ushort, string> WorldData => _dalamudUtil.WorldData.Value;
    public uint WorldId => _dalamudUtil.GetHomeWorldId();
    public bool ChatTwoExists => _chatTwoExists;

    public static void AttachToolTip(string text)
    {
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35f);
            if (text.Contains(TooltipSeparator, StringComparison.Ordinal))
            {
                var splitText = text.Split(TooltipSeparator, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < splitText.Length; i++)
                {
                    ImGui.TextUnformatted(splitText[i]);
                    if (i != splitText.Length - 1) ImGui.Separator();
                }
            }
            else
            {
                ImGui.TextUnformatted(text);
            }
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    public static string ByteToString(long bytes, bool addSuffix = true)
    {
        string[] suffix = ["B", "KiB", "MiB", "GiB", "TiB"];
        int i;
        double dblSByte = bytes;
        for (i = 0; i < suffix.Length && bytes >= 1024; i++, bytes /= 1024)
        {
            dblSByte = bytes / 1024.0;
        }

        return addSuffix ? $"{dblSByte:0.00} {suffix[i]}" : $"{dblSByte:0.00}";
    }

    public static void CenterNextWindow(float width, float height, ImGuiCond cond = ImGuiCond.None)
    {
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(new Vector2(center.X - width / 2, center.Y - height / 2), cond);
    }

    public static uint Color(byte r, byte g, byte b, byte a)
    { uint ret = a; ret <<= 8; ret += b; ret <<= 8; ret += g; ret <<= 8; ret += r; return ret; }

    public static uint Color(Vector4 color)
    {
        uint ret = (byte)(color.W * 255);
        ret <<= 8;
        ret += (byte)(color.Z * 255);
        ret <<= 8;
        ret += (byte)(color.Y * 255);
        ret <<= 8;
        ret += (byte)(color.X * 255);
        return ret;
    }

    public static void ColorText(string text, Vector4 color)
    {
        using var raiicolor = ImRaii.PushColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
    }

    public static void ColorTextWrapped(string text, Vector4 color, float wrapPos = 0)
    {
        using var raiicolor = ImRaii.PushColor(ImGuiCol.Text, color);
        TextWrapped(text, wrapPos);
    }

    public static bool CtrlPressed() => (GetKeyState(0xA2) & 0x8000) != 0 || (GetKeyState(0xA3) & 0x8000) != 0;

    public static void DrawGrouped(Action imguiDrawAction, float rounding = 5f, float? expectedWidth = null)
    {
        var cursorPos = ImGui.GetCursorPos();
        using (ImRaii.Group())
        {
            if (expectedWidth != null)
            {
                ImGui.Dummy(new(expectedWidth.Value, 0));
                ImGui.SetCursorPos(cursorPos);
            }

            imguiDrawAction.Invoke();
        }

        ImGui.GetWindowDrawList().AddRect(
            ImGui.GetItemRectMin() - ImGui.GetStyle().ItemInnerSpacing,
            ImGui.GetItemRectMax() + ImGui.GetStyle().ItemInnerSpacing,
            Color(ImGuiColors.DalamudGrey2), rounding);
    }

    public static void DrawGroupedCenteredColorText(string text, Vector4 color, float? maxWidth = null)
    {
        var availWidth = ImGui.GetContentRegionAvail().X;
        var textWidth = ImGui.CalcTextSize(text, wrapWidth: availWidth).X;
        if (maxWidth != null && textWidth > maxWidth * ImGuiHelpers.GlobalScale) textWidth = maxWidth.Value * ImGuiHelpers.GlobalScale;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availWidth / 2f) - (textWidth / 2f));
        DrawGrouped(() =>
        {
            ColorTextWrapped(text, color, ImGui.GetCursorPosX() + textWidth);
        }, expectedWidth: maxWidth == null ? null : maxWidth * ImGuiHelpers.GlobalScale);
    }

    public static void DrawOutlinedFont(string text, Vector4 fontColor, Vector4 outlineColor, int thickness)
    {
        var original = ImGui.GetCursorPos();

        using (ImRaii.PushColor(ImGuiCol.Text, outlineColor))
        {
            ImGui.SetCursorPos(original with { Y = original.Y - thickness });
            ImGui.TextUnformatted(text);
            ImGui.SetCursorPos(original with { X = original.X - thickness });
            ImGui.TextUnformatted(text);
            ImGui.SetCursorPos(original with { Y = original.Y + thickness });
            ImGui.TextUnformatted(text);
            ImGui.SetCursorPos(original with { X = original.X + thickness });
            ImGui.TextUnformatted(text);
            ImGui.SetCursorPos(original with { X = original.X - thickness, Y = original.Y - thickness });
            ImGui.TextUnformatted(text);
            ImGui.SetCursorPos(original with { X = original.X + thickness, Y = original.Y + thickness });
            ImGui.TextUnformatted(text);
            ImGui.SetCursorPos(original with { X = original.X - thickness, Y = original.Y + thickness });
            ImGui.TextUnformatted(text);
            ImGui.SetCursorPos(original with { X = original.X + thickness, Y = original.Y - thickness });
            ImGui.TextUnformatted(text);
        }

        using (ImRaii.PushColor(ImGuiCol.Text, fontColor))
        {
            ImGui.SetCursorPos(original);
            ImGui.TextUnformatted(text);
            ImGui.SetCursorPos(original);
            ImGui.TextUnformatted(text);
        }
    }

    public static void DrawOutlinedFont(ImDrawListPtr drawList, string text, Vector2 textPos, uint fontColor, uint outlineColor, int thickness)
    {
        drawList.AddText(textPos with { Y = textPos.Y - thickness },
            outlineColor, text);
        drawList.AddText(textPos with { X = textPos.X - thickness },
            outlineColor, text);
        drawList.AddText(textPos with { Y = textPos.Y + thickness },
            outlineColor, text);
        drawList.AddText(textPos with { X = textPos.X + thickness },
            outlineColor, text);
        drawList.AddText(new Vector2(textPos.X - thickness, textPos.Y - thickness),
            outlineColor, text);
        drawList.AddText(new Vector2(textPos.X + thickness, textPos.Y + thickness),
            outlineColor, text);
        drawList.AddText(new Vector2(textPos.X - thickness, textPos.Y + thickness),
            outlineColor, text);
        drawList.AddText(new Vector2(textPos.X + thickness, textPos.Y - thickness),
            outlineColor, text);

        drawList.AddText(textPos, fontColor, text);
        drawList.AddText(textPos, fontColor, text);
    }

    public static void DrawTree(string leafName, Action drawOnOpened, ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.None)
    {
        using var tree = ImRaii.TreeNode(leafName, flags);
        if (tree)
        {
            drawOnOpened();
        }
    }

    public static Vector4 GetBoolColor(bool input) => input ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;

    public static string GetNotes(List<Pair> pairs)
    {
        StringBuilder sb = new();
        sb.AppendLine(_notesStart);
        foreach (var entry in pairs)
        {
            var note = entry.GetNote();
            if (note.IsNullOrEmpty()) continue;

            sb.Append(entry.UserData.UID).Append(":\"").Append(entry.GetNote()).AppendLine("\"");
        }
        sb.AppendLine(_notesEnd);

        return sb.ToString();
    }

    public static float GetWindowContentRegionWidth()
    {
        return ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
    }

    public static bool IsDirectoryWritable(string dirPath, bool throwIfFails = false)
    {
        try
        {
            using FileStream fs = File.Create(
                       Path.Combine(
                           dirPath,
                           Path.GetRandomFileName()
                       ),
                       1,
                       FileOptions.DeleteOnClose);
            return true;
        }
        catch
        {
            if (throwIfFails)
                throw;

            return false;
        }
    }

    public static void ScaledNextItemWidth(float width)
    {
        ImGui.SetNextItemWidth(width * ImGuiHelpers.GlobalScale);
    }

    public static void ScaledSameLine(float offset)
    {
        ImGui.SameLine(offset * ImGuiHelpers.GlobalScale);
    }

    public static void SetScaledWindowSize(float width, bool centerWindow = true)
    {
        var newLineHeight = ImGui.GetCursorPosY();
        ImGui.NewLine();
        newLineHeight = ImGui.GetCursorPosY() - newLineHeight;
        var y = ImGui.GetCursorPos().Y + ImGui.GetWindowContentRegionMin().Y - newLineHeight * 2 - ImGui.GetStyle().ItemSpacing.Y;

        SetScaledWindowSize(width, y, centerWindow, scaledHeight: true);
    }

    public static void SetScaledWindowSize(float width, float height, bool centerWindow = true, bool scaledHeight = false)
    {
        ImGui.SameLine();
        var x = width * ImGuiHelpers.GlobalScale;
        var y = scaledHeight ? height : height * ImGuiHelpers.GlobalScale;

        if (centerWindow)
        {
            CenterWindow(x, y);
        }

        ImGui.SetWindowSize(new Vector2(x, y));
    }

    public static bool ShiftPressed() => (GetKeyState(0xA1) & 0x8000) != 0 || (GetKeyState(0xA0) & 0x8000) != 0;

    public static void TextWrapped(string text, float wrapPos = 0)
    {
        ImGui.PushTextWrapPos(wrapPos);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }

    public static Vector4 UploadColor((long, long) data) => data.Item1 == 0 ? ImGuiColors.DalamudGrey :
        data.Item1 == data.Item2 ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudYellow;

    public bool ApplyNotesFromClipboard(string notes, bool overwrite)
    {
        var splitNotes = notes.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ToList();
        var splitNotesStart = splitNotes.FirstOrDefault();
        var splitNotesEnd = splitNotes.LastOrDefault();
        if (!string.Equals(splitNotesStart, _notesStart, StringComparison.Ordinal) || !string.Equals(splitNotesEnd, _notesEnd, StringComparison.Ordinal))
        {
            return false;
        }

        splitNotes.RemoveAll(n => string.Equals(n, _notesStart, StringComparison.Ordinal) || string.Equals(n, _notesEnd, StringComparison.Ordinal));

        foreach (var note in splitNotes)
        {
            try
            {
                var splittedEntry = note.Split(":", 2, StringSplitOptions.RemoveEmptyEntries);
                var uid = splittedEntry[0];
                var comment = splittedEntry[1].Trim('"');
                if (_serverConfigurationManager.GetNoteForUid(uid) != null && !overwrite) continue;
                _serverConfigurationManager.SetNoteForUid(uid, comment);
            }
            catch
            {
                Logger.LogWarning("Could not parse {note}", note);
            }
        }

        _serverConfigurationManager.SaveNotes();

        return true;
    }

    public void BigText(string text, Vector4? color = null)
    {
        FontText(text, UidFont, color);
    }

    public void BooleanToColoredIcon(bool value, bool inline = true)
    {
        using var colorgreen = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen, value);
        using var colorred = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed, !value);

        if (inline) ImGui.SameLine();

        if (value)
        {
            IconText(FontAwesomeIcon.Check);
        }
        else
        {
            IconText(FontAwesomeIcon.Times);
        }
    }

    public void DrawCacheDirectorySetting()
    {
        ColorTextWrapped("注意：存储文件夹应位于新建的空白文件夹中，并且路径靠近根目录越短越好（比如C:\\MareStorage）。路径不要有中文。不要将路径指定到游戏文件夹。不要将路径指定到Penumbra文件夹。", ImGuiColors.DalamudYellow);
        var cacheDirectory = _configService.Current.CacheFolder;
        ImGui.InputText("存储文件夹##cache", ref cacheDirectory, 255, ImGuiInputTextFlags.ReadOnly);

        ImGui.SameLine();
        using (ImRaii.Disabled(_cacheMonitor.MareWatcher != null))
        {
            if (IconButton(FontAwesomeIcon.Folder))
            {
                FileDialogManager.OpenFolderDialog("选择星海同步器存储文件夹", (success, path) =>
                {
                    if (!success) return;

                    _isOneDrive = path.Contains("onedrive", StringComparison.OrdinalIgnoreCase);
                    _isPenumbraDirectory = string.Equals(path.ToLowerInvariant(), _ipcManager.Penumbra.ModDirectory?.ToLowerInvariant(), StringComparison.Ordinal);
                    var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                    _cacheDirectoryHasOtherFilesThanCache = false;
                    foreach (var file in files)
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        if (fileName.Length != 40 && !string.Equals(fileName, "desktop", StringComparison.OrdinalIgnoreCase))
                        {
                            _cacheDirectoryHasOtherFilesThanCache = true;
                            Logger.LogWarning("Found illegal file in {path}: {file}", path, file);
                            break;
                        }
                    }
                    var dirs = Directory.GetDirectories(path);
                    if (dirs.Any())
                    {
                        _cacheDirectoryHasOtherFilesThanCache = true;
                        Logger.LogWarning("Found folders in {path} not belonging to Mare: {dirs}", path, string.Join(", ", dirs));
                    }

                    _isDirectoryWritable = IsDirectoryWritable(path);
                    _cacheDirectoryIsValidPath = PathRegex().IsMatch(path);

                    if (!string.IsNullOrEmpty(path)
                        && Directory.Exists(path)
                        && _isDirectoryWritable
                        && !_isPenumbraDirectory
                        && !_isOneDrive
                        && !_cacheDirectoryHasOtherFilesThanCache
                        && _cacheDirectoryIsValidPath)
                    {
                        _configService.Current.CacheFolder = path;
                        _configService.Save();
                        _cacheMonitor.StartMareWatcher(path);
                        _cacheMonitor.InvokeScan();
                    }
                }, _dalamudUtil.IsWine ? @"Z:\" : @"C:\");
            }
        }
        if (_cacheMonitor.MareWatcher != null)
        {
            AttachToolTip("关闭文件监控后再尝试改变储存路径. 只要监控还在运行, 你就无法改变储存路径.");
        }

        if (_isPenumbraDirectory)
        {
            ColorTextWrapped("不要将存储路径直接指向Penumbra文件夹。如果一定要指向这里，在其中创建一个子文件夹。", ImGuiColors.DalamudRed);
        }
        else if (_isOneDrive)
        {
            ColorTextWrapped("不要将存储路径直接指向使用 OneDrive 的文件夹. 也不要对储存路径使用任何同步工具.", ImGuiColors.DalamudRed);
        }
        else if (!_isDirectoryWritable)
        {
            ColorTextWrapped("您选择的文件夹不存在或无法写入。请提供一个有效的路径。", ImGuiColors.DalamudRed);
        }
        else if (_cacheDirectoryHasOtherFilesThanCache)
        {
            ColorTextWrapped("您选择的文件夹中有与月海同步器无关的文件。仅使用空目录或以前的Mare存储目录.", ImGuiColors.DalamudRed);
        }
        else if (!_cacheDirectoryIsValidPath)
        {
            ColorTextWrapped("您选择的文件夹路径包含FF14无法读取的非法字符。" +
                             "请仅使用拉丁字母（A-Z）、下划线（_）、短划线（-）和阿拉伯数字（0-9）。", ImGuiColors.DalamudRed);
        }

        float maxCacheSize = (float)_configService.Current.MaxLocalCacheInGiB;
        if (ImGui.SliderFloat("最大存储大小（GiB）", ref maxCacheSize, 1f, 200f, "%.2f GiB"))
        {
            _configService.Current.MaxLocalCacheInGiB = maxCacheSize;
            _configService.Save();
        }
        DrawHelpText("存储由月海同步器自动管理。一旦达到设置的容量，它将通过删除最旧的未使用文件自动清除。\n您通常不需要自己清理。");
    }

    public T? DrawCombo<T>(string comboName, IEnumerable<T> comboItems, Func<T?, string> toName,
        Action<T?>? onSelected = null, T? initialSelectedItem = default)
    {
        if (!comboItems.Any()) return default;

        if (!_selectedComboItems.TryGetValue(comboName, out var selectedItem) && selectedItem == null)
        {
            selectedItem = initialSelectedItem;
            _selectedComboItems[comboName] = selectedItem;
        }

        if (ImGui.BeginCombo(comboName, selectedItem == null ? "未设置" : toName((T?)selectedItem)))
        {
            foreach (var item in comboItems)
            {
                bool isSelected = EqualityComparer<T>.Default.Equals(item, (T?)selectedItem);
                if (ImGui.Selectable(toName(item), isSelected))
                {
                    _selectedComboItems[comboName] = item!;
                    onSelected?.Invoke(item!);
                }
            }

            ImGui.EndCombo();
        }

        return (T?)_selectedComboItems[comboName];
    }

    public void DrawFileScanState()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("文件扫描状态");
        ImGui.SameLine();
        if (_cacheMonitor.IsScanRunning)
        {
            ImGui.AlignTextToFramePadding();

            ImGui.TextUnformatted("扫描进行中");
            ImGui.TextUnformatted("当前进度:");
            ImGui.SameLine();
            ImGui.TextUnformatted(_cacheMonitor.TotalFiles == 1
                ? "正在计算文件数量"
                : $"从存储中处理 {_cacheMonitor.CurrentFileProgress}/{_cacheMonitor.TotalFilesStorage} (已扫描{_cacheMonitor.TotalFiles})");
            AttachToolTip("注意：存储的文件可能比扫描的文件多，这是因为扫描器通常会忽略这些文件，" +
                "但游戏会加载这些文件并在你的角色上使用它们，所以它们会被添加到本地存储中。");
        }
        else if (_cacheMonitor.HaltScanLocks.Any(f => f.Value > 0))
        {
            ImGui.AlignTextToFramePadding();

            ImGui.TextUnformatted("Halted (" + string.Join(", ", _cacheMonitor.HaltScanLocks.Where(f => f.Value > 0).Select(locker => locker.Key + ": " + locker.Value + " halt requests")) + ")");
            ImGui.SameLine();
            if (ImGui.Button("重置暂停需求##clearlocks"))
            {
                _cacheMonitor.ResetLocks();
            }
        }
        else
        {
            ImGui.TextUnformatted("空闲");
            if (_configService.Current.InitialScanComplete)
            {
                ImGui.SameLine();
                if (IconTextButton(FontAwesomeIcon.Play, "强制扫描"))
                {
                    _cacheMonitor.InvokeScan();
                }
            }
        }
    }

    public void DrawHelpText(string helpText)
    {
        ImGui.SameLine();
        IconText(FontAwesomeIcon.QuestionCircle, ImGui.GetColorU32(ImGuiCol.TextDisabled));
        AttachToolTip(helpText);
    }

    public void DrawOAuth(ServerStorage selectedServer)
    {
        var oauthToken = selectedServer.OAuthToken;
        _ = ImRaii.PushIndent(10f);
        if (oauthToken == null)
        {
            if (_discordOAuthCheck == null)
            {
                if (IconTextButton(FontAwesomeIcon.QuestionCircle, "检查服务器是否支持Discord OAuth2"))
                {
                    _discordOAuthCheck = _serverConfigurationManager.CheckDiscordOAuth(selectedServer.ServerUri);
                }
            }
            else
            {
                if (!_discordOAuthCheck.IsCompleted)
                {
                    ColorTextWrapped($"正在检查服务器 {selectedServer.ServerUri}", ImGuiColors.DalamudYellow);
                }
                else
                {
                    if (_discordOAuthCheck.Result != null)
                    {
                        ColorTextWrapped("服务器支持Discord OAuth2", ImGuiColors.HealerGreen);
                    }
                    else
                    {
                        ColorTextWrapped("服务器不支持Discord OAuth2 或网络连接失败", ImGuiColors.DalamudRed);
                    }
                }
            }

            if (_discordOAuthCheck != null && _discordOAuthCheck.IsCompleted && _discordOAuthCheck.Result != null)
            {
                if (IconTextButton(FontAwesomeIcon.ArrowRight, "进行验证"))
                {
                    _discordOAuthGetCode = _serverConfigurationManager.GetDiscordOAuthToken(_discordOAuthCheck.Result, selectedServer.ServerUri, _discordOAuthGetCts.Token);
                }
                else if (_discordOAuthGetCode != null && !_discordOAuthGetCode.IsCompleted)
                {
                    TextWrapped("浏览器窗口已打开，请按照窗口进行身份验证。如果您不小心关闭了窗口并需要重新进行身份验证，请点击下面的按钮。");
                    if (IconTextButton(FontAwesomeIcon.Ban, "取消验证"))
                    {
                        _discordOAuthGetCts = _discordOAuthGetCts.CancelRecreate();
                        _discordOAuthGetCode = null;
                    }
                }
                else if (_discordOAuthGetCode != null && _discordOAuthGetCode.IsCompleted)
                {
                    TextWrapped("Discord OAuth2 设置完成, 状态: ");
                    ImGui.SameLine();
                    if (_discordOAuthGetCode?.Result != null)
                    {
                        selectedServer.OAuthToken = _discordOAuthGetCode.Result;
                        _discordOAuthGetCode = null;
                        _serverConfigurationManager.Save();
                        ColorTextWrapped("成功", ImGuiColors.HealerGreen);
                    }
                    else
                    {
                        ColorTextWrapped("失败, 请检查 /xllog 获取更多信息", ImGuiColors.DalamudRed);
                    }
                }
            }
        }

        if (oauthToken != null)
        {
            if (!_oauthTokenExpiry.TryGetValue(oauthToken, out DateTime tokenExpiry))
            {
                try
                {
                    var handler = new JwtSecurityTokenHandler();
                    var jwt = handler.ReadJwtToken(oauthToken);
                    tokenExpiry = _oauthTokenExpiry[oauthToken] = jwt.ValidTo;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Could not parse OAuth token, deleting");
                    selectedServer.OAuthToken = null;
                    _serverConfigurationManager.Save();
                }
            }

            if (tokenExpiry > DateTime.UtcNow)
            {
                ColorTextWrapped($"OAuth2已启用, 连接到: Discord用户 {_serverConfigurationManager.GetDiscordUserFromToken(selectedServer)}", ImGuiColors.HealerGreen);
                TextWrapped($"OAuth2令牌将于 {tokenExpiry:yyyy-MM-dd} 过期, 将于 {(tokenExpiry - TimeSpan.FromDays(7)):yyyy-MM-dd} 以及之后登陆时自动更新.");
                using (ImRaii.Disabled(!CtrlPressed()))
                {
                    if (IconTextButton(FontAwesomeIcon.Exclamation, "手动刷新OAuth2令牌") && CtrlPressed())
                    {
                        _ = _tokenProvider.TryUpdateOAuth2LoginTokenAsync(selectedServer, forced: true)
                            .ContinueWith((_) => _apiController.CreateConnectionsAsync());
                    }
                }
                DrawHelpText("按住CTRL并点击来手动刷新你的OAuth2令牌. 你一般不需要这么做.");
                ImGuiHelpers.ScaledDummy(10f);

                if ((_discordOAuthUIDs == null || _discordOAuthUIDs.IsCompleted)
                    && IconTextButton(FontAwesomeIcon.Question, "检查Discord连接"))
                {
                    _discordOAuthUIDs = _serverConfigurationManager.GetUIDsWithDiscordToken(selectedServer.ServerUri, oauthToken);
                }
                else if (_discordOAuthUIDs != null)
                {
                    if (!_discordOAuthUIDs.IsCompleted)
                    {
                        ColorTextWrapped("正在查找服务器上的UID", ImGuiColors.DalamudYellow);
                    }
                    else
                    {
                        var foundUids = _discordOAuthUIDs.Result?.Count ?? 0;
                        var primaryUid = _discordOAuthUIDs.Result?.FirstOrDefault() ?? new KeyValuePair<string, string>(string.Empty, string.Empty);
                        var vanity = string.IsNullOrEmpty(primaryUid.Value) ? "-" : primaryUid.Value;
                        if (foundUids > 0)
                        {
                            ColorTextWrapped($"在服务器上找到 {foundUids} 个UID, 主UID: {primaryUid.Key} (个性UID: {vanity})",
                                ImGuiColors.HealerGreen);
                        }
                        else
                        {
                            ColorTextWrapped($"未找到与OAuth2关联的UID", ImGuiColors.DalamudRed);
                        }
                    }
                }
            }
            else
            {
                ColorTextWrapped("OAuth2令牌已过期. 请更新OAuth2连接.", ImGuiColors.DalamudRed);
                if (IconTextButton(FontAwesomeIcon.Exclamation, "更新OAuth2连接"))
                {
                    selectedServer.OAuthToken = null;
                    _serverConfigurationManager.Save();
                    _ = _serverConfigurationManager.CheckDiscordOAuth(selectedServer.ServerUri)
                        .ContinueWith(async (urlTask) =>
                        {
                            var url = await urlTask.ConfigureAwait(false);
                            var token = await _serverConfigurationManager.GetDiscordOAuthToken(url!, selectedServer.ServerUri, CancellationToken.None).ConfigureAwait(false);
                            selectedServer.OAuthToken = token;
                            _serverConfigurationManager.Save();
                            await _apiController.CreateConnectionsAsync().ConfigureAwait(false);
                        });
                }
            }

            DrawUnlinkOAuthButton(selectedServer);
        }
    }

    public bool DrawOtherPluginState()
    {
        ImGui.TextUnformatted("相关插件:");

        ImGui.SameLine(150);
        ColorText("Penumbra", GetBoolColor(_penumbraExists));
        AttachToolTip($"Penumbra目前" + (_penumbraExists ? "已为最新." : "未安装或需要更新."));

        ImGui.SameLine();
        ColorText("Glamourer", GetBoolColor(_glamourerExists));
        AttachToolTip($"Glamourer目前" + (_glamourerExists ? "已为最新." : "未安装或需要更新."));

        ImGui.TextUnformatted("可选插件：");
        ImGui.SameLine(150);
        ColorText("SimpleHeels", GetBoolColor(_heelsExists));
        AttachToolTip($"SimpleHeels目前" + (_heelsExists ? "已为最新." : "未安装或需要更新."));

        ImGui.SameLine();
        ColorText("Customize+", GetBoolColor(_customizePlusExists));
        AttachToolTip($"Customize+目前" + (_customizePlusExists ? "已为最新." : "未安装或需要更新."));

        ImGui.SameLine();
        ColorText("Honorific", GetBoolColor(_honorificExists));
        AttachToolTip($"Honorific目前" + (_honorificExists ? "已为最新." : "未安装或需要更新."));

        ImGui.SameLine();
        ColorText("Moodles", GetBoolColor(_moodlesExists));
        AttachToolTip($"Moodles目前" + (_moodlesExists ? "已为最新." : "未安装或需要更新."));

        ImGui.SameLine();
        ColorText("PetNicknames", GetBoolColor(_petNamesExists));
        AttachToolTip($"PetNicknames目前" + (_petNamesExists ? "已为最新." : "未安装或需要更新."));

        ImGui.SameLine();
        ColorText("Brio", GetBoolColor(_brioExists));
        AttachToolTip($"Brio目前" + (_brioExists ? "已为最新." : "未安装或需要更新."));

        ImGui.SameLine();
        ColorText("ChatTwo", GetBoolColor(_chatTwoExists));
        AttachToolTip($"ChatTwo目前" + (_chatTwoExists ? "已为最新." : "未安装或需要更新."));

        if (!_penumbraExists || !_glamourerExists)
        {
            ImGui.TextColored(ImGuiColors.DalamudRed, "你需要安装 Penumbra 和 Glamourer 的最新版本才能使用Mare.");
            return false;
        }

        return true;
    }

    public int DrawServiceSelection(bool selectOnChange = false, bool showConnect = true)
    {
        string[] comboEntries = _serverConfigurationManager.GetServerNames();

        if (_serverSelectionIndex == -1)
        {
            _serverSelectionIndex = Array.IndexOf(_serverConfigurationManager.GetServerApiUrls(), _serverConfigurationManager.CurrentApiUrl);
        }
        if (_serverSelectionIndex == -1 || _serverSelectionIndex >= comboEntries.Length)
        {
            _serverSelectionIndex = 0;
        }
        for (int i = 0; i < comboEntries.Length; i++)
        {
            if (string.Equals(_serverConfigurationManager.CurrentServer?.ServerName, comboEntries[i], StringComparison.OrdinalIgnoreCase))
                comboEntries[i] += " [当前]";
        }
        if (ImGui.BeginCombo("选择服务器", comboEntries[_serverSelectionIndex]))
        {
            for (int i = 0; i < comboEntries.Length; i++)
            {
                bool isSelected = _serverSelectionIndex == i;
                if (ImGui.Selectable(comboEntries[i], isSelected))
                {
                    _serverSelectionIndex = i;
                    if (selectOnChange)
                    {
                        _serverConfigurationManager.SelectServer(i);
                    }
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        if (showConnect)
        {
            ImGui.SameLine();
            var text = "连接";
            if (_serverSelectionIndex == _serverConfigurationManager.CurrentServerIndex) text = "重新连接";
            if (IconTextButton(FontAwesomeIcon.Link, text))
            {
                _serverConfigurationManager.SelectServer(_serverSelectionIndex);
                _ = _apiController.CreateConnectionsAsync();
            }
        }

        if (ImGui.TreeNode("添加自定义服务器"))
        {
            ImGui.SetNextItemWidth(250);
            ImGui.InputText("自定义服务器 URI", ref _customServerUri, 255);
            ImGui.SetNextItemWidth(250);
            ImGui.InputText("自定义服务器 Name", ref _customServerName, 255);
            if (IconTextButton(FontAwesomeIcon.Plus, "添加自定义服务器")
                && !string.IsNullOrEmpty(_customServerUri)
                && !string.IsNullOrEmpty(_customServerName))
            {
                _serverConfigurationManager.AddServer(new ServerStorage()
                {
                    ServerName = _customServerName,
                    ServerUri = _customServerUri,
                    UseOAuth2 = true
                });
                _customServerName = string.Empty;
                _customServerUri = string.Empty;
                _configService.Save();
            }
            ImGui.TreePop();
        }

        return _serverSelectionIndex;
    }

    public void DrawUIDComboForAuthentication(int indexOffset, Authentication item, string serverUri, ILogger? logger = null)
    {
        using (ImRaii.Disabled(_discordOAuthUIDs == null))
        {
            var aliasPairs = _discordOAuthUIDs?.Result?.Select(t => new UIDAliasPair(t.Key, t.Value)).ToList() ?? [new UIDAliasPair(item.UID ?? null, null)];
            var uidComboName = "UID###" + item.CharacterName + item.WorldId + serverUri + indexOffset + aliasPairs.Count;
            DrawCombo(uidComboName, aliasPairs,
                (v) =>
                {
                    if (v is null)
                        return "未设置UID";

                    if (!string.IsNullOrEmpty(v.Alias))
                    {
                        return $"{v.UID} ({v.Alias})";
                    }

                    if (string.IsNullOrEmpty(v.UID))
                        return "未设置UID";

                    return $"{v.UID}";
                },
                (v) =>
                {
                    if (!string.Equals(v?.UID ?? null, item.UID, StringComparison.Ordinal))
                    {
                        item.UID = v?.UID ?? null;
                        _serverConfigurationManager.Save();
                    }
                },
                aliasPairs.Find(f => string.Equals(f.UID, item.UID, StringComparison.Ordinal)) ?? default);
        }

        if (_discordOAuthUIDs == null)
        {
            AttachToolTip("分配UID前请点击上方按钮从服务器获取最新UID列表.");
        }
    }

    public void DrawUnlinkOAuthButton(ServerStorage selectedServer)
    {
        using (ImRaii.Disabled(!CtrlPressed()))
        {
            if (IconTextButton(FontAwesomeIcon.Trash, "取消OAuth2连接") && UiSharedService.CtrlPressed())
            {
                selectedServer.OAuthToken = null;
                _serverConfigurationManager.Save();
                ResetOAuthTasksState();
            }
        }
        DrawHelpText("按住CTRL取消当前的OAuth2连接.");
    }

    public void DrawUpdateOAuthUIDsButton(ServerStorage selectedServer)
    {
        if (!selectedServer.UseOAuth2)
            return;

        using (ImRaii.Disabled(string.IsNullOrEmpty(selectedServer.OAuthToken)))
        {
            if ((_discordOAuthUIDs == null || _discordOAuthUIDs.IsCompleted)
                && IconTextButton(FontAwesomeIcon.ArrowsSpin, "从服务器获取UID列表")
                && !string.IsNullOrEmpty(selectedServer.OAuthToken))
            {
                _discordOAuthUIDs = _serverConfigurationManager.GetUIDsWithDiscordToken(selectedServer.ServerUri, selectedServer.OAuthToken);
            }
        }
        DateTime tokenExpiry = DateTime.MinValue;
        if (!string.IsNullOrEmpty(selectedServer.OAuthToken) && !_oauthTokenExpiry.TryGetValue(selectedServer.OAuthToken, out tokenExpiry))
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var jwt = handler.ReadJwtToken(selectedServer.OAuthToken);
                tokenExpiry = _oauthTokenExpiry[selectedServer.OAuthToken] = jwt.ValidTo;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Could not parse OAuth token, deleting");
                selectedServer.OAuthToken = null;
                _serverConfigurationManager.Save();
                tokenExpiry = DateTime.MinValue;
            }
        }
        if (string.IsNullOrEmpty(selectedServer.OAuthToken) || tokenExpiry < DateTime.UtcNow)
        {
            ColorTextWrapped("你尚未设置OAuth令牌或令牌已过期. 请在服务器设置中重新连接到Discord账号或更新令牌.", ImGuiColors.DalamudRed);
        }
    }

    public Vector2 GetIconButtonSize(FontAwesomeIcon icon)
    {
        using var font = IconFont.Push();
        return ImGuiHelpers.GetButtonSize(icon.ToIconString());
    }

    public Vector2 GetIconSize(FontAwesomeIcon icon)
    {
        using var font = IconFont.Push();
        return ImGui.CalcTextSize(icon.ToIconString());
    }

    public float GetIconTextButtonSize(FontAwesomeIcon icon, string text)
    {
        Vector2 vector;
        using (IconFont.Push())
            vector = ImGui.CalcTextSize(icon.ToIconString());

        Vector2 vector2 = ImGui.CalcTextSize(text);
        float num = 3f * ImGuiHelpers.GlobalScale;
        return vector.X + vector2.X + ImGui.GetStyle().FramePadding.X * 2f + num;
    }

    public bool IconButton(FontAwesomeIcon icon, float? height = null)
    {
        string text = icon.ToIconString();

        ImGui.PushID(text);
        Vector2 vector;
        using (IconFont.Push())
            vector = ImGui.CalcTextSize(text);
        ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
        Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
        float x = vector.X + ImGui.GetStyle().FramePadding.X * 2f;
        float frameHeight = height ?? ImGui.GetFrameHeight();
        bool result = ImGui.Button(string.Empty, new Vector2(x, frameHeight));
        Vector2 pos = new Vector2(cursorScreenPos.X + ImGui.GetStyle().FramePadding.X,
            cursorScreenPos.Y + (height ?? ImGui.GetFrameHeight()) / 2f - (vector.Y / 2f));
        using (IconFont.Push())
            windowDrawList.AddText(pos, ImGui.GetColorU32(ImGuiCol.Text), text);
        ImGui.PopID();

        return result;
    }

    public void IconText(FontAwesomeIcon icon, uint color)
    {
        FontText(icon.ToIconString(), IconFont, color);
    }

    public void IconText(FontAwesomeIcon icon, Vector4? color = null)
    {
        IconText(icon, color == null ? ImGui.GetColorU32(ImGuiCol.Text) : ImGui.GetColorU32(color.Value));
    }

    public bool IconTextButton(FontAwesomeIcon icon, string text, float? width = null, bool isInPopup = false)
    {
        return IconTextButtonInternal(icon, text,
            isInPopup ? ColorHelpers.RgbaUintToVector4(ImGui.GetColorU32(ImGuiCol.PopupBg)) : null,
            width <= 0 ? null : width);
    }

    public IDalamudTextureWrap LoadImage(byte[] imageData)
    {
        return _textureProvider.CreateFromImageAsync(imageData).Result;
    }

    public void LoadLocalization(string languageCode)
    {
        _localization.SetupWithLangCode(languageCode);
        Strings.ToS = new Strings.ToSStrings();
    }

    internal static void DistanceSeparator()
    {
        ImGuiHelpers.ScaledDummy(5);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(5);
    }

    [LibraryImport("user32")]
    internal static partial short GetKeyState(int nVirtKey);

    internal void ResetOAuthTasksState()
    {
        _discordOAuthCheck = null;
        _discordOAuthGetCts = _discordOAuthGetCts.CancelRecreate();
        _discordOAuthGetCode = null;
        _discordOAuthUIDs = null;
    }

    public static void UpdateSupporters(List<string> supporters)
    {
        _supporters = supporters;
    }

    public static bool IsSupporter(string supporter)
    {
        return _supporters.Contains(supporter);
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing) return;

        base.Dispose(disposing);

        UidFont.Dispose();
        GameFont.Dispose();
    }

    private static void CenterWindow(float width, float height, ImGuiCond cond = ImGuiCond.None)
    {
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetWindowPos(new Vector2(center.X - width / 2, center.Y - height / 2), cond);
    }

    [GeneratedRegex(@"^(?:[a-zA-Z]:\\[\w\s\-\\]+?|\/(?:[\w\s\-\/])+?)$", RegexOptions.ECMAScript, 5000)]
    private static partial Regex PathRegex();

    private static void FontText(string text, IFontHandle font, Vector4? color = null)
    {
        FontText(text, font, color == null ? ImGui.GetColorU32(ImGuiCol.Text) : ImGui.GetColorU32(color.Value));
    }

    private static void FontText(string text, IFontHandle font, uint color)
    {
        using var pushedFont = font.Push();
        using var pushedColor = ImRaii.PushColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
    }

    private bool IconTextButtonInternal(FontAwesomeIcon icon, string text, Vector4? defaultColor = null, float? width = null)
    {
        int num = 0;
        if (defaultColor.HasValue)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, defaultColor.Value);
            num++;
        }

        ImGui.PushID(text);
        Vector2 vector;
        using (IconFont.Push())
            vector = ImGui.CalcTextSize(icon.ToIconString());
        Vector2 vector2 = ImGui.CalcTextSize(text);
        ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
        Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
        float num2 = 3f * ImGuiHelpers.GlobalScale;
        float x = width ?? vector.X + vector2.X + ImGui.GetStyle().FramePadding.X * 2f + num2;
        float frameHeight = ImGui.GetFrameHeight();
        bool result = ImGui.Button(string.Empty, new Vector2(x, frameHeight));
        Vector2 pos = new Vector2(cursorScreenPos.X + ImGui.GetStyle().FramePadding.X, cursorScreenPos.Y + ImGui.GetStyle().FramePadding.Y);
        using (IconFont.Push())
            windowDrawList.AddText(pos, ImGui.GetColorU32(ImGuiCol.Text), icon.ToIconString());
        Vector2 pos2 = new Vector2(pos.X + vector.X + num2, cursorScreenPos.Y + ImGui.GetStyle().FramePadding.Y);
        windowDrawList.AddText(pos2, ImGui.GetColorU32(ImGuiCol.Text), text);
        ImGui.PopID();
        if (num > 0)
        {
            ImGui.PopStyleColor(num);
        }

        return result;
    }
    public sealed record IconScaleData(Vector2 IconSize, Vector2 NormalizedIconScale, float OffsetX, float IconScaling);
    private record UIDAliasPair(string? UID, string? Alias);
}