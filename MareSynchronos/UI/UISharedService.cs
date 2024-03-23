﻿using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Internal;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Utility;
using ImGuiNET;
using MareSynchronos.FileCache;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.Localization;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MareSynchronos.UI;

public partial class UiSharedService : DisposableMediatorSubscriberBase
{
    public const string TooltipSeparator = "--SEP--";

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
    private readonly DalamudPluginInterface _pluginInterface;
    private readonly Dictionary<string, object> _selectedComboItems = new(StringComparer.Ordinal);
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private bool _cacheDirectoryHasOtherFilesThanCache = false;

    private bool _cacheDirectoryIsValidPath = true;

    private bool _customizePlusExists = false;

    private string _customServerName = "";

    private string _customServerUri = "";

    private bool _glamourerExists = false;

    private bool _heelsExists = false;

    private bool _honorificExists = false;
    private bool _isDirectoryWritable = false;
    private bool _isOneDrive = false;
    private bool _isPenumbraDirectory = false;
    private bool _moodlesExists = false;
    private bool _penumbraExists = false;

    private int _serverSelectionIndex = -1;

    public UiSharedService(ILogger<UiSharedService> logger, IpcManager ipcManager, ApiController apiController,
        CacheMonitor cacheMonitor, FileDialogManager fileDialogManager,
        MareConfigService configService, DalamudUtilService dalamudUtil, DalamudPluginInterface pluginInterface, Dalamud.Localization localization,
        ServerConfigurationManager serverManager, MareMediator mediator) : base(logger, mediator)
    {
        _ipcManager = ipcManager;
        _apiController = apiController;
        _cacheMonitor = cacheMonitor;
        FileDialogManager = fileDialogManager;
        _configService = configService;
        _dalamudUtil = dalamudUtil;
        _pluginInterface = pluginInterface;
        _localization = localization;
        _serverConfigurationManager = serverManager;
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
        });

        UidFont = _pluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e =>
        {
            e.OnPreBuild(tk => tk.AddDalamudAssetFont(Dalamud.DalamudAsset.NotoSansJpMedium, new()
            {
                SizePx = 35
            }));
        });
        GameFont = _pluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(new(GameFontFamilyAndSize.Axis12));
        IconFont = _pluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e =>
        {
            e.OnPreBuild(tk => tk.AddFontAwesomeIconFont(new()
            {
                SizePx = _pluginInterface.UiBuilder.DefaultFontSpec.SizePx,
                GlyphMinAdvanceX = _pluginInterface.UiBuilder.DefaultFontSpec.SizePx,
                GlyphMaxAdvanceX = _pluginInterface.UiBuilder.DefaultFontSpec.SizePx
            }));
            e.OnPostBuild(tk =>
            {
                var font = tk.Font;
                var nsize = font.FontSize;
                var glyphs = font.GlyphsWrapped();
                foreach (ref var glyph in glyphs.DataSpan)
                {
                    var ratio = 1f;
                    if (glyph.X1 - glyph.X0 > nsize)
                        ratio = Math.Max(ratio, (glyph.X1 - glyph.X0) / nsize);
                    if (glyph.Y1 - glyph.Y0 > nsize)
                        ratio = Math.Max(ratio, (glyph.Y1 - glyph.Y0) / nsize);
                    var w = MathF.Round((glyph.X1 - glyph.X0) / ratio, MidpointRounding.ToZero);
                    var h = MathF.Round((glyph.Y1 - glyph.Y0) / ratio, MidpointRounding.AwayFromZero);
                    glyph.X0 = MathF.Round((nsize - w) / 2f, MidpointRounding.ToZero);
                    glyph.Y0 = MathF.Round((nsize - h) / 2f, MidpointRounding.AwayFromZero);
                    glyph.X1 = glyph.X0 + w;
                    glyph.Y1 = glyph.Y0 + h;
                    glyph.AdvanceX = nsize;
                }

                tk.BuildLookupTable(font);
            });
        });
    }

    public ApiController ApiController => _apiController;

    public bool EditTrackerPosition { get; set; }

    public IFontHandle GameFont { get; init; }
    public bool HasValidPenumbraModPath => !(_ipcManager.Penumbra.ModDirectory ?? string.Empty).IsNullOrEmpty() && Directory.Exists(_ipcManager.Penumbra.ModDirectory);

    public IFontHandle IconFont { get; init; }
    public bool IsInGpose => _dalamudUtil.IsInCutscene;

    public string PlayerName => _dalamudUtil.GetPlayerName();

    public IFontHandle UidFont { get; init; }
    public Dictionary<ushort, string> WorldData => _dalamudUtil.WorldData.Value;

    public uint WorldId => _dalamudUtil.GetHomeWorldId();

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

    public static void ColorTextWrapped(string text, Vector4 color)
    {
        using var raiicolor = ImRaii.PushColor(ImGuiCol.Text, color);
        TextWrapped(text);
    }

    public static bool CtrlPressed() => (GetKeyState(0xA2) & 0x8000) != 0 || (GetKeyState(0xA3) & 0x8000) != 0;

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

    public static Vector4 GetBoolColor(bool input) => input ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;

    public float GetIconTextButtonSize(FontAwesomeIcon icon, string text)
    {
        Vector2 vector;
        using (IconFont.Push())
            vector = ImGui.CalcTextSize(icon.ToIconString());

        Vector2 vector2 = ImGui.CalcTextSize(text);
        float num = 3f * ImGuiHelpers.GlobalScale;
        return vector.X + vector2.X + ImGui.GetStyle().FramePadding.X * 2f + num;
    }

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

    public bool IconTextButton(FontAwesomeIcon icon, string text, float? width = null, bool isInPopup = false)
    {
        return IconTextButtonInternal(icon, text,
            isInPopup ? ColorHelpers.RgbaUintToVector4(ImGui.GetColorU32(ImGuiCol.PopupBg)) : null,
            width <= 0 ? null : width);
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

    public static void TextWrapped(string text)
    {
        ImGui.PushTextWrapPos(0);
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
                    _isDirectoryWritable = IsDirectoryWritable(path);
                    _cacheDirectoryHasOtherFilesThanCache = Directory.GetFiles(path, "*", SearchOption.AllDirectories).Any(f => Path.GetFileNameWithoutExtension(f).Length != 40)
                        || Directory.GetDirectories(path).Any();
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
            AttachToolTip("Stop the Monitoring before changing the Storage folder. As long as monitoring is active, you cannot change the Storage folder location.");
        }

        if (_isPenumbraDirectory)
        {
            ColorTextWrapped("不要将存储路径直接指向Penumbra文件夹。如果一定要指向这里，在其中创建一个子文件夹。", ImGuiColors.DalamudRed);
        }
        else if (_isOneDrive)
        {
            ColorTextWrapped("Do not point the storage path to a folder in OneDrive. Do not use OneDrive folders for any Mod related functionality.", ImGuiColors.DalamudRed);
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

    public T? DrawCombo<T>(string comboName, IEnumerable<T> comboItems, Func<T, string> toName,
        Action<T?>? onSelected = null, T? initialSelectedItem = default)
    {
        if (!comboItems.Any()) return default;

        if (!_selectedComboItems.TryGetValue(comboName, out var selectedItem) && selectedItem == null)
        {
            if (!EqualityComparer<T>.Default.Equals(initialSelectedItem, default))
            {
                selectedItem = initialSelectedItem;
                _selectedComboItems[comboName] = selectedItem!;
                if (!EqualityComparer<T>.Default.Equals(initialSelectedItem, default))
                    onSelected?.Invoke(initialSelectedItem);
            }
            else
            {
                selectedItem = comboItems.First();
                _selectedComboItems[comboName] = selectedItem!;
            }
        }

        if (ImGui.BeginCombo(comboName, toName((T)selectedItem!)))
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

        return (T)_selectedComboItems[comboName];
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

    public bool DrawOtherPluginState()
    {
        var check = FontAwesomeIcon.Check;
        var cross = FontAwesomeIcon.SquareXmark;
        ImGui.TextUnformatted("Mandatory Plugins:");

        ImGui.SameLine();
        ImGui.TextUnformatted("Penumbra");
        ImGui.SameLine();
        IconText(_penumbraExists ? check : cross, GetBoolColor(_penumbraExists));
        ImGui.SameLine();
        AttachToolTip($"Penumbra " + (_penumbraExists ? "可用." : "不可用或需要更新."));
        ImGui.Spacing();

        ImGui.SameLine();
        ImGui.TextUnformatted("Glamourer");
        ImGui.SameLine();
        IconText(_glamourerExists ? check : cross, GetBoolColor(_glamourerExists));
        ImGui.SameLine();
        AttachToolTip($"Glamourer " + (_glamourerExists ? "可用." : "不可用或需要更新."));
        ImGui.Spacing();

        ImGui.TextUnformatted("可选插件:");
        ImGui.SameLine();
        ImGui.TextUnformatted("SimpleHeels");
        ImGui.SameLine();
        IconText(_heelsExists ? check : cross, GetBoolColor(_heelsExists));
        ImGui.SameLine();
        AttachToolTip($"SimpleHeels " + (_heelsExists ? "可用." : "不可用或需要更新."));
        ImGui.Spacing();

        ImGui.SameLine();
        ImGui.TextUnformatted("Customize+");
        ImGui.SameLine();
        IconText(_customizePlusExists ? check : cross, GetBoolColor(_customizePlusExists));
        ImGui.SameLine();
        AttachToolTip($"Customize+ " + (_customizePlusExists ? "可用." : "不可用或需要更新."));
        ImGui.Spacing();

        ImGui.SameLine();
        ImGui.TextUnformatted("Honorific");
        ImGui.SameLine();
        IconText(_honorificExists ? check : cross, GetBoolColor(_honorificExists));
        ImGui.SameLine();
        AttachToolTip($"Honorific " + (_honorificExists ? "可用." : "不可用或需要更新."));
        ImGui.Spacing();

        ImGui.SameLine();
        ImGui.TextUnformatted("Moodles");
        ImGui.SameLine();
        IconText(_moodlesExists ? check : cross, GetBoolColor(_moodlesExists));
        ImGui.SameLine();
        AttachToolTip($"Moodles " + (_moodlesExists ? "可用." : "不可用或需要更新."));
        ImGui.Spacing();

        if (!_penumbraExists || !_glamourerExists)
        {
            ImGui.TextColored(ImGuiColors.DalamudRed, "您至少需要同时安装Penumbra和Glamourer，并更新到它们的最新版本才能使用月海同步器。");
            return false;
        }

        return true;
    }

    public int DrawServiceSelection(bool selectOnChange = false)
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
                comboEntries[i] += " [Current]";
        }
        if (ImGui.BeginCombo("选择服务", comboEntries[_serverSelectionIndex]))
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

        if (_serverConfigurationManager.GetSecretKey(_serverSelectionIndex) != null)
        {
            ImGui.SameLine();
            var text = "连接";
            if (_serverSelectionIndex == _serverConfigurationManager.CurrentServerIndex) text = "重新连接";
            if (IconTextButton(FontAwesomeIcon.Link, text))
            {
                _serverConfigurationManager.SelectServer(_serverSelectionIndex);
                _ = _apiController.CreateConnections();
            }
        }

        if (ImGui.TreeNode("添加自定义服务"))
        {
            ImGui.SetNextItemWidth(250);
            ImGui.InputText("自定义服务URI", ref _customServerUri, 255);
            ImGui.SetNextItemWidth(250);
            ImGui.InputText("自定义服务名称", ref _customServerName, 255);
            if (IconTextButton(FontAwesomeIcon.Plus, "添加自定义服务")
                && !string.IsNullOrEmpty(_customServerUri)
                && !string.IsNullOrEmpty(_customServerName))
            {
                _serverConfigurationManager.AddServer(new ServerStorage()
                {
                    ServerName = _customServerName,
                    ServerUri = _customServerUri,
                });
                _customServerName = string.Empty;
                _customServerUri = string.Empty;
                _configService.Save();
            }
            ImGui.TreePop();
        }

        return _serverSelectionIndex;
    }

    public Vector2 GetIconButtonSize(FontAwesomeIcon icon)
    {
        using var font = IconFont.Push();
        var buttonSize = ImGuiHelpers.GetButtonSize(icon.ToIconString());
        return buttonSize;
    }

    public Vector2 GetIconData(FontAwesomeIcon icon)
    {
        using var font = IconFont.Push();
        return ImGui.CalcTextSize(icon.ToIconString());
    }

    public Vector2 IconButtonSize(FontAwesomeIcon icon)
    {
        using var font = IconFont.Push();
        return ImGuiHelpers.GetButtonSize(icon.ToIconString());
    }

    public void IconText(FontAwesomeIcon icon, uint color)
    {
        FontText(icon.ToIconString(), IconFont, color);
    }

    public void IconText(FontAwesomeIcon icon, Vector4? color = null)
    {
        IconText(icon, color == null ? ImGui.GetColorU32(ImGuiCol.Text) : ImGui.GetColorU32(color.Value));
    }

    public IDalamudTextureWrap LoadImage(byte[] imageData)
    {
        return _pluginInterface.UiBuilder.LoadImage(imageData);
    }

    public void LoadLocalization(string languageCode)
    {
        _localization.SetupWithLangCode(languageCode);
        Strings.ToS = new Strings.ToSStrings();
    }

    [LibraryImport("user32")]
    internal static partial short GetKeyState(int nVirtKey);

    private static void CenterWindow(float width, float height, ImGuiCond cond = ImGuiCond.None)
    {
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetWindowPos(new Vector2(center.X - width / 2, center.Y - height / 2), cond);
    }

    [GeneratedRegex(@"^(?:[a-zA-Z]:\\[\w\s\-\\]+?|\/(?:[\w\s\-\/])+?)$", RegexOptions.ECMAScript, 5000)]
    private static partial Regex PathRegex();

    private void FontText(string text, IFontHandle font, Vector4? color = null)
    {
        FontText(text, font, color == null ? ImGui.GetColorU32(ImGuiCol.Text) : ImGui.GetColorU32(color.Value));
    }

    private void FontText(string text, IFontHandle font, uint color)
    {
        using var pushedFont = font.Push();
        using var pushedColor = ImRaii.PushColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
    }

    public sealed record IconScaleData(Vector2 IconSize, Vector2 NormalizedIconScale, float OffsetX, float IconScaling);

    protected override void Dispose(bool disposing)
    {
        if (!disposing) return;

        base.Dispose(disposing);

        IconFont.Dispose();
        UidFont.Dispose();
        GameFont.Dispose();
    }
}