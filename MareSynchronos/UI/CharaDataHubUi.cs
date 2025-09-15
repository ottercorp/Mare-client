using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Dto.CharaData;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.CharaData;
using MareSynchronos.Services.CharaData.Models;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI;

internal sealed partial class CharaDataHubUi : WindowMediatorSubscriberBase
{
    private const int maxPoses = 10;
    private readonly CharaDataManager _charaDataManager;
    private readonly CharaDataNearbyManager _charaDataNearbyManager;
    private readonly CharaDataConfigService _configService;
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly FileDialogManager _fileDialogManager;
    private readonly PairManager _pairManager;
    private readonly CharaDataGposeTogetherManager _charaDataGposeTogetherManager;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly UiSharedService _uiSharedService;
    private CancellationTokenSource _closalCts = new();
    private bool _disableUI = false;
    private CancellationTokenSource _disposalCts = new();
    private string _exportDescription = string.Empty;
    private string _filterCodeNote = string.Empty;
    private string _filterDescription = string.Empty;
    private Dictionary<string, List<CharaDataMetaInfoExtendedDto>>? _filteredDict;
    private Dictionary<string, (CharaDataFavorite Favorite, CharaDataMetaInfoExtendedDto? MetaInfo, bool DownloadedMetaInfo)> _filteredFavorites = [];
    private bool _filterPoseOnly = false;
    private bool _filterWorldOnly = false;
    private string _gposeTarget = string.Empty;
    private bool _hasValidGposeTarget;
    private string _importCode = string.Empty;
    private bool _isHandlingSelf = false;
    private DateTime _lastFavoriteUpdateTime = DateTime.UtcNow;
    private PoseEntryExtended? _nearbyHovered;
    private bool _openMcdOnlineOnNextRun = false;
    private bool _readExport;
    private string _selectedDtoId = string.Empty;
    private string SelectedDtoId
    {
        get => _selectedDtoId;
        set
        {
            if (!string.Equals(_selectedDtoId, value, StringComparison.Ordinal))
            {
                _charaDataManager.UploadTask = null;
                _selectedDtoId = value;
            }

        }
    }
    private string _selectedSpecificUserIndividual = string.Empty;
    private string _selectedSpecificGroupIndividual = string.Empty;
    private string _sharedWithYouDescriptionFilter = string.Empty;
    private bool _sharedWithYouDownloadableFilter = false;
    private string _sharedWithYouOwnerFilter = string.Empty;
    private string _specificIndividualAdd = string.Empty;
    private string _specificGroupAdd = string.Empty;
    private bool _abbreviateCharaName = false;
    private string? _openComboHybridId = null;
    private (string Id, string? Alias, string AliasOrId, string? Note)[]? _openComboHybridEntries = null;
    private bool _comboHybridUsedLastFrame = false;

    public CharaDataHubUi(ILogger<CharaDataHubUi> logger, MareMediator mediator, PerformanceCollectorService performanceCollectorService,
                         CharaDataManager charaDataManager, CharaDataNearbyManager charaDataNearbyManager, CharaDataConfigService configService,
                         UiSharedService uiSharedService, ServerConfigurationManager serverConfigurationManager,
                         DalamudUtilService dalamudUtilService, FileDialogManager fileDialogManager, PairManager pairManager,
                         CharaDataGposeTogetherManager charaDataGposeTogetherManager)
        : base(logger, mediator, "Mare角色数据中心###MareSynchronosCharaDataUI", performanceCollectorService)
    {
        SetWindowSizeConstraints();

        _charaDataManager = charaDataManager;
        _charaDataNearbyManager = charaDataNearbyManager;
        _configService = configService;
        _uiSharedService = uiSharedService;
        _serverConfigurationManager = serverConfigurationManager;
        _dalamudUtilService = dalamudUtilService;
        _fileDialogManager = fileDialogManager;
        _pairManager = pairManager;
        _charaDataGposeTogetherManager = charaDataGposeTogetherManager;
        Mediator.Subscribe<GposeStartMessage>(this, (_) => IsOpen |= _configService.Current.OpenMareHubOnGposeStart);
        Mediator.Subscribe<OpenCharaDataHubWithFilterMessage>(this, (msg) =>
        {
            IsOpen = true;
            _openDataApplicationShared = true;
            _sharedWithYouOwnerFilter = msg.UserData.AliasOrUID;
            UpdateFilteredItems();
        });
    }

    private bool _openDataApplicationShared = false;

    public string CharaName(string name)
    {
        if (_abbreviateCharaName)
        {
            var split = name.Split(" ");
            return split[0].First() + ". " + split[1].First() + ".";
        }

        return name;
    }

    public override void OnClose()
    {
        if (_disableUI)
        {
            IsOpen = true;
            return;
        }

        _closalCts.Cancel();
        SelectedDtoId = string.Empty;
        _filteredDict = null;
        _sharedWithYouOwnerFilter = string.Empty;
        _importCode = string.Empty;
        _charaDataNearbyManager.ComputeNearbyData = false;
        _openComboHybridId = null;
        _openComboHybridEntries = null;
    }

    public override void OnOpen()
    {
        _closalCts = _closalCts.CancelRecreate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _closalCts.CancelDispose();
            _disposalCts.CancelDispose();
        }

        base.Dispose(disposing);
    }

    protected override void DrawInternal()
    {
        if (!_comboHybridUsedLastFrame)
        {
            _openComboHybridId = null;
            _openComboHybridEntries = null;
        }
        _comboHybridUsedLastFrame = false;

        _disableUI = !(_charaDataManager.UiBlockingComputation?.IsCompleted ?? true);
        if (DateTime.UtcNow.Subtract(_lastFavoriteUpdateTime).TotalSeconds > 2)
        {
            _lastFavoriteUpdateTime = DateTime.UtcNow;
            UpdateFilteredFavorites();
        }

        (_hasValidGposeTarget, _gposeTarget) = _charaDataManager.CanApplyInGpose().GetAwaiter().GetResult();

        if (!_charaDataManager.BrioAvailable)
        {
            ImGuiHelpers.ScaledDummy(3);
            UiSharedService.DrawGroupedCenteredColorText("要使用生成角色或姿势相关功能, 需要安装Brio.", ImGuiColors.DalamudRed);
            UiSharedService.DistanceSeparator();
        }

        using var disabled = ImRaii.Disabled(_disableUI);

        DisableDisabled(() =>
        {
            if (_charaDataManager.DataApplicationTask != null)
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted("应用数据到角色");
                ImGui.SameLine();
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Ban, "取消应用"))
                {
                    _charaDataManager.CancelDataApplication();
                }
            }
            if (!string.IsNullOrEmpty(_charaDataManager.DataApplicationProgress))
            {
                UiSharedService.ColorTextWrapped(_charaDataManager.DataApplicationProgress, ImGuiColors.DalamudYellow);
            }
            if (_charaDataManager.DataApplicationTask != null)
            {
                UiSharedService.ColorTextWrapped("警告: 应用数据过程中应避免与该对象交互, 以防止游戏崩溃.", ImGuiColors.DalamudRed);
                ImGuiHelpers.ScaledDummy(5);
                ImGui.Separator();
            }
        });

        using var tabs = ImRaii.TabBar("TabsTopLevel");
        bool smallUi = false;

        _isHandlingSelf = _charaDataManager.HandledCharaData.Any(c => c.IsSelf);
        if (_isHandlingSelf) _openMcdOnlineOnNextRun = false;

        using (var gposeTogetherTabItem = ImRaii.TabItem("在线GPose"))
        {
            if (gposeTogetherTabItem)
            {
                smallUi = true;

                DrawGposeTogether();
            }
        }

        using (var applicationTabItem = ImRaii.TabItem("应用数据", _openDataApplicationShared ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
        {
            if (applicationTabItem)
            {
                smallUi = true;
                using var appTabs = ImRaii.TabBar("TabsApplicationLevel");

                using (ImRaii.Disabled(!_uiSharedService.IsInGpose))
                {
                    using (var gposeTabItem = ImRaii.TabItem("GPose角色"))
                    {
                        if (gposeTabItem)
                        {
                            using var id = ImRaii.PushId("gposeControls");
                            DrawGposeControls();
                        }
                    }
                }
                if (!_uiSharedService.IsInGpose)
                    UiSharedService.AttachToolTip("仅在GPose中可用");

                using (var nearbyPosesTabItem = ImRaii.TabItem("附近的姿势"))
                {
                    if (nearbyPosesTabItem)
                    {
                        using var id = ImRaii.PushId("nearbyPoseControls");
                        _charaDataNearbyManager.ComputeNearbyData = true;

                        DrawNearbyPoses();
                    }
                    else
                    {
                        _charaDataNearbyManager.ComputeNearbyData = false;
                    }
                }

                using (var gposeTabItem = ImRaii.TabItem("应用数据", _openDataApplicationShared ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
                {
                    if (gposeTabItem)
                    {
                        smallUi |= true;
                        using var id = ImRaii.PushId("applyData");
                        DrawDataApplication();
                    }
                }
            }
            else
            {
                _charaDataNearbyManager.ComputeNearbyData = false;
            }
        }

        using (ImRaii.Disabled(_isHandlingSelf))
        {
            ImGuiTabItemFlags flagsTopLevel = ImGuiTabItemFlags.None;
            if (_openMcdOnlineOnNextRun)
            {
                flagsTopLevel = ImGuiTabItemFlags.SetSelected;
                _openMcdOnlineOnNextRun = false;
            }

            using (var creationTabItem = ImRaii.TabItem("创建数据", flagsTopLevel))
            {
                if (creationTabItem)
                {
                    using var creationTabs = ImRaii.TabBar("TabsCreationLevel");

                    ImGuiTabItemFlags flags = ImGuiTabItemFlags.None;
                    if (_openMcdOnlineOnNextRun)
                    {
                        flags = ImGuiTabItemFlags.SetSelected;
                        _openMcdOnlineOnNextRun = false;
                    }
                    using (var mcdOnlineTabItem = ImRaii.TabItem("在线MCD", flags))
                    {
                        if (mcdOnlineTabItem)
                        {
                            using var id = ImRaii.PushId("mcdOnline");
                            DrawMcdOnline();
                        }
                    }

                    using (var mcdfTabItem = ImRaii.TabItem("导出MCDF"))
                    {
                        if (mcdfTabItem)
                        {
                            using var id = ImRaii.PushId("mcdfExport");
                            DrawMcdfExport();
                        }
                    }
                }
            }
        }
        if (_isHandlingSelf)
        {
            UiSharedService.AttachToolTip("将角色数据应用于自身时无法使用创作工具.");
        }

        using (var settingsTabItem = ImRaii.TabItem("设置"))
        {
            if (settingsTabItem)
            {
                using var id = ImRaii.PushId("settings");
                DrawSettings();
            }
        }


        SetWindowSizeConstraints(smallUi);
    }

    private void DrawAddOrRemoveFavorite(CharaDataFullDto dto)
    {
        DrawFavorite(dto.Uploader.UID + ":" + dto.Id);
    }

    private void DrawAddOrRemoveFavorite(CharaDataMetaInfoExtendedDto? dto)
    {
        if (dto == null) return;
        DrawFavorite(dto.FullId);
    }

    private void DrawFavorite(string id)
    {
        bool isFavorite = _configService.Current.FavoriteCodes.TryGetValue(id, out var favorite);
        if (_configService.Current.FavoriteCodes.ContainsKey(id))
        {
            _uiSharedService.IconText(FontAwesomeIcon.Star, ImGuiColors.ParsedGold);
            UiSharedService.AttachToolTip($"自定义描述: {favorite?.CustomDescription ?? string.Empty}" + UiSharedService.TooltipSeparator
                + "单击以从收藏夹删除");
        }
        else
        {
            _uiSharedService.IconText(FontAwesomeIcon.Star, ImGuiColors.DalamudGrey);
            UiSharedService.AttachToolTip("单击以添加到收藏夹");
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            if (isFavorite) _configService.Current.FavoriteCodes.Remove(id);
            else _configService.Current.FavoriteCodes[id] = new();
            _configService.Save();
        }
    }

    private void DrawGposeControls()
    {
        _uiSharedService.BigText("GPose 角色");
        ImGuiHelpers.ScaledDummy(5);
        using var indent = ImRaii.PushIndent(10f);

        foreach (var actor in _dalamudUtilService.GetGposeCharactersFromObjectTable())
        {
            if (actor == null) continue;
            using var actorId = ImRaii.PushId(actor.Name.TextValue);
            UiSharedService.DrawGrouped(() =>
            {
                if (_uiSharedService.IconButton(FontAwesomeIcon.Crosshairs))
                {
                    unsafe
                    {
                        _dalamudUtilService.GposeTarget = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)actor.Address;
                    }
                }
                ImGui.SameLine();
                UiSharedService.AttachToolTip($"选中GPose角色 {CharaName(actor.Name.TextValue)}");
                ImGui.AlignTextToFramePadding();
                var pos = ImGui.GetCursorPosX();
                using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen, actor.Address == (_dalamudUtilService.GetGposeTargetGameObjectAsync().GetAwaiter().GetResult()?.Address ?? nint.Zero)))
                {
                    ImGui.TextUnformatted(CharaName(actor.Name.TextValue));
                }
                ImGui.SameLine(250);
                var handled = _charaDataManager.HandledCharaData.FirstOrDefault(c => string.Equals(c.Name, actor.Name.TextValue, StringComparison.Ordinal));
                using (ImRaii.Disabled(handled == null))
                {
                    _uiSharedService.IconText(FontAwesomeIcon.InfoCircle);
                    var id = string.IsNullOrEmpty(handled?.MetaInfo.Uploader.UID) ? handled?.MetaInfo.Id : handled.MetaInfo.FullId;
                    UiSharedService.AttachToolTip($"已应用的数据: {id ?? "无数据"}");

                    ImGui.SameLine();
                    // maybe do this better, check with brio for handled charas or sth
                    using (ImRaii.Disabled(!actor.Name.TextValue.StartsWith("Brio ", StringComparison.Ordinal)))
                    {
                        if (_uiSharedService.IconButton(FontAwesomeIcon.Trash))
                        {
                            _charaDataManager.RemoveChara(actor.Name.TextValue);
                        }
                        UiSharedService.AttachToolTip($"移除角色 {CharaName(actor.Name.TextValue)}");
                    }
                    ImGui.SameLine();
                    if (_uiSharedService.IconButton(FontAwesomeIcon.Undo))
                    {
                        _charaDataManager.RevertChara(handled);
                    }
                    UiSharedService.AttachToolTip($"撤销 {CharaName(actor.Name.TextValue)} 的变更");
                    ImGui.SetCursorPosX(pos);
                    DrawPoseData(handled?.MetaInfo, actor.Name.TextValue, true);
                }
            });

            ImGuiHelpers.ScaledDummy(2);
        }
    }

    private void DrawDataApplication()
    {
        _uiSharedService.BigText("应用角色外貌");

        ImGuiHelpers.ScaledDummy(5);

        if (_uiSharedService.IsInGpose)
        {
            ImGui.TextUnformatted("GPose 目标");
            ImGui.SameLine(200);
            UiSharedService.ColorText(CharaName(_gposeTarget), UiSharedService.GetBoolColor(_hasValidGposeTarget));
        }

        if (!_hasValidGposeTarget)
        {
            ImGuiHelpers.ScaledDummy(3);
            UiSharedService.DrawGroupedCenteredColorText("仅在选中了有效的Gpose目标时才能使用本功能.", ImGuiColors.DalamudYellow, 350);
        }

        ImGuiHelpers.ScaledDummy(10);

        using var tabs = ImRaii.TabBar("Tabs");

        using (var byFavoriteTabItem = ImRaii.TabItem("收藏夹"))
        {
            if (byFavoriteTabItem)
            {
                using var id = ImRaii.PushId("byFavorite");

                ImGuiHelpers.ScaledDummy(5);

                var max = ImGui.GetWindowContentRegionMax();
                UiSharedService.DrawTree("过滤", () =>
                {
                    var maxIndent = ImGui.GetWindowContentRegionMax();
                    ImGui.SetNextItemWidth(maxIndent.X - ImGui.GetCursorPosX());
                    ImGui.InputTextWithHint("##ownFilter", "代码/拥有者", ref _filterCodeNote, 100);
                    ImGui.SetNextItemWidth(maxIndent.X - ImGui.GetCursorPosX());
                    ImGui.InputTextWithHint("##descFilter", "自定义描述", ref _filterDescription, 100);
                    ImGui.Checkbox("仅显示有姿势数据的条目", ref _filterPoseOnly);
                    ImGui.Checkbox("仅显示有位置数据的条目", ref _filterWorldOnly);
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.Ban, "重置"))
                    {
                        _filterCodeNote = string.Empty;
                        _filterDescription = string.Empty;
                        _filterPoseOnly = false;
                        _filterWorldOnly = false;
                    }
                });

                ImGuiHelpers.ScaledDummy(5);
                ImGui.Separator();
                using var scrollableChild = ImRaii.Child("favorite");
                ImGuiHelpers.ScaledDummy(5);
                using var totalIndent = ImRaii.PushIndent(5f);
                var cursorPos = ImGui.GetCursorPos();
                max = ImGui.GetWindowContentRegionMax();
                foreach (var favorite in _filteredFavorites.OrderByDescending(k => k.Value.Favorite.LastDownloaded))
                {
                    UiSharedService.DrawGrouped(() =>
                    {
                        using var tableid = ImRaii.PushId(favorite.Key);
                        ImGui.AlignTextToFramePadding();
                        DrawFavorite(favorite.Key);
                        using var innerIndent = ImRaii.PushIndent(25f);
                        ImGui.SameLine();
                        var xPos = ImGui.GetCursorPosX();
                        var maxPos = (max.X - cursorPos.X);

                        bool metaInfoDownloaded = favorite.Value.DownloadedMetaInfo;
                        var metaInfo = favorite.Value.MetaInfo;

                        ImGui.AlignTextToFramePadding();
                        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey, !metaInfoDownloaded))
                        using (ImRaii.PushColor(ImGuiCol.Text, UiSharedService.GetBoolColor(metaInfo != null), metaInfoDownloaded))
                            ImGui.TextUnformatted(favorite.Key);

                        var iconSize = _uiSharedService.GetIconSize(FontAwesomeIcon.Check);
                        var refreshButtonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.ArrowsSpin);
                        var applyButtonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.ArrowRight);
                        var addButtonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Plus);
                        var offsetFromRight = maxPos - (iconSize.X + refreshButtonSize.X + applyButtonSize.X + addButtonSize.X + (ImGui.GetStyle().ItemSpacing.X * 3.5f));

                        ImGui.SameLine();
                        ImGui.SetCursorPosX(offsetFromRight);
                        if (metaInfoDownloaded)
                        {
                            _uiSharedService.BooleanToColoredIcon(metaInfo != null, false);
                            if (metaInfo != null)
                            {
                                UiSharedService.AttachToolTip("元数据" + UiSharedService.TooltipSeparator
                                    + $"最后更新于: {metaInfo!.UpdatedDate}" + Environment.NewLine
                                    + $"描述: {metaInfo!.Description}" + Environment.NewLine
                                    + $"姿势: {metaInfo!.PoseData.Count}");
                            }
                            else
                            {
                                UiSharedService.AttachToolTip("无法下载元数据." + UiSharedService.TooltipSeparator
                                    + "数据不存在或你没有足够的权限进行访问");
                            }
                        }
                        else
                        {
                            _uiSharedService.IconText(FontAwesomeIcon.QuestionCircle, ImGuiColors.DalamudGrey);
                            UiSharedService.AttachToolTip("未知的访问状态. 点击右侧按钮刷新.");
                        }

                        ImGui.SameLine();
                        bool isInTimeout = _charaDataManager.IsInTimeout(favorite.Key);
                        using (ImRaii.Disabled(isInTimeout))
                        {
                            if (_uiSharedService.IconButton(FontAwesomeIcon.ArrowsSpin))
                            {
                                _charaDataManager.DownloadMetaInfo(favorite.Key, false);
                                UpdateFilteredItems();
                            }
                        }
                        UiSharedService.AttachToolTip(isInTimeout ? "刷新超时, 请稍后重试."
                            : "从服务器刷新本条目的数据.");

                        ImGui.SameLine();
                        GposeMetaInfoAction((meta) =>
                        {
                            if (_uiSharedService.IconButton(FontAwesomeIcon.ArrowRight))
                            {
                                _ = _charaDataManager.ApplyCharaDataToGposeTarget(metaInfo!);
                            }
                        }, "对GPose目标应用数据", metaInfo, _hasValidGposeTarget, false);
                        ImGui.SameLine();
                        GposeMetaInfoAction((meta) =>
                        {
                            if (_uiSharedService.IconButton(FontAwesomeIcon.Plus))
                            {
                                _ = _charaDataManager.SpawnAndApplyData(meta!);
                            }
                        }, "使用Brio生成角色并应用数据", metaInfo, _hasValidGposeTarget, true);

                        string uidText = string.Empty;
                        var uid = favorite.Key.Split(":")[0];
                        if (metaInfo != null)
                        {
                            uidText = metaInfo.Uploader.AliasOrUID;
                        }
                        else
                        {
                            uidText = uid;
                        }

                        var note = _serverConfigurationManager.GetNoteForUid(uid);
                        if (note != null)
                        {
                            uidText = $"{note} ({uidText})";
                        }
                        ImGui.TextUnformatted(uidText);

                        ImGui.TextUnformatted("最后使用于: ");
                        ImGui.SameLine();
                        ImGui.TextUnformatted(favorite.Value.Favorite.LastDownloaded == DateTime.MaxValue ? "从未" : favorite.Value.Favorite.LastDownloaded.ToString());

                        var desc = favorite.Value.Favorite.CustomDescription;
                        ImGui.SetNextItemWidth(maxPos - xPos);
                        if (ImGui.InputTextWithHint("##desc", "收藏夹自定义描述", ref desc, 100))
                        {
                            favorite.Value.Favorite.CustomDescription = desc;
                            _configService.Save();
                        }

                        DrawPoseData(metaInfo, _gposeTarget, _hasValidGposeTarget);
                    });

                    ImGuiHelpers.ScaledDummy(5);
                }

                if (_configService.Current.FavoriteCodes.Count == 0)
                {
                    UiSharedService.ColorTextWrapped("收藏夹中没有数据. 请先添加数据.", ImGuiColors.DalamudYellow);
                }
            }
        }

        using (var byCodeTabItem = ImRaii.TabItem("代码"))
        {
            using var id = ImRaii.PushId("byCodeTab");
            if (byCodeTabItem)
            {
                using var child = ImRaii.Child("sharedWithYouByCode", new(0, 0), false, ImGuiWindowFlags.AlwaysAutoResize);
                DrawHelpFoldout("你可以在本标签中应用代码中的数据. 按照 \"所有者UID:数据Id\" 的格式将代码填入下方并点击 " +
                                "\"获取代码数据\". 这将从服务器获取代码的基本数据. 之后在GPose中选择一个目标并点击 \"下载并应用到 <actor>\"." + Environment.NewLine + Environment.NewLine
                                + "描述: 由所有者填写的描述文字." + Environment.NewLine
                                + "最后更新于: 数据最后更新的时间." + Environment.NewLine
                                + "可下载: 数据是否可被下载. 若代码中的数据无法下载, 请联系其所有者请他们进行修复." + Environment.NewLine + Environment.NewLine
                                + "你需要拥有所有者给予的对应权限才能下载数据. 如果无法获取代码基本数据, 请联系所有者并确认他们正确的设置了访问权限.");

                ImGuiHelpers.ScaledDummy(5);
                ImGui.InputTextWithHint("##importCode", "输入数据代码", ref _importCode, 100);
                using (ImRaii.Disabled(string.IsNullOrEmpty(_importCode)))
                {
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowCircleDown, "获取代码数据"))
                    {
                        _charaDataManager.DownloadMetaInfo(_importCode);
                    }
                }
                GposeMetaInfoAction((meta) =>
                {
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowRight, $"下载并应用"))
                    {
                        _ = _charaDataManager.ApplyCharaDataToGposeTarget(meta!);
                    }
                }, "应用数据到当前的GPose目标", _charaDataManager.LastDownloadedMetaInfo, _hasValidGposeTarget, false);
                ImGui.SameLine();
                GposeMetaInfoAction((meta) =>
                {
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, $"下载并生成"))
                    {
                        _ = _charaDataManager.SpawnAndApplyData(meta!);
                    }
                }, "使用Brio生成一个角色并应用数据", _charaDataManager.LastDownloadedMetaInfo, _hasValidGposeTarget, true);
                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();
                DrawAddOrRemoveFavorite(_charaDataManager.LastDownloadedMetaInfo);

                ImGui.NewLine();
                if (!_charaDataManager.DownloadMetaInfoTask?.IsCompleted ?? false)
                {
                    UiSharedService.ColorTextWrapped("正在下载元数据. 请稍后.", ImGuiColors.DalamudYellow);
                }
                if ((_charaDataManager.DownloadMetaInfoTask?.IsCompleted ?? false) && !_charaDataManager.DownloadMetaInfoTask.Result.Success)
                {
                    UiSharedService.ColorTextWrapped(_charaDataManager.DownloadMetaInfoTask.Result.Result, ImGuiColors.DalamudRed);
                }

                using (ImRaii.Disabled(_charaDataManager.LastDownloadedMetaInfo == null))
                {
                    ImGuiHelpers.ScaledDummy(5);
                    var metaInfo = _charaDataManager.LastDownloadedMetaInfo;
                    ImGui.TextUnformatted("描述");
                    ImGui.SameLine(150);
                    UiSharedService.TextWrapped(string.IsNullOrEmpty(metaInfo?.Description) ? "-" : metaInfo.Description);
                    ImGui.TextUnformatted("最后更新于");
                    ImGui.SameLine(150);
                    ImGui.TextUnformatted(metaInfo?.UpdatedDate.ToLocalTime().ToString() ?? "-");
                    ImGui.TextUnformatted("可下载");
                    ImGui.SameLine(150);
                    _uiSharedService.BooleanToColoredIcon(metaInfo?.CanBeDownloaded ?? false, inline: false);
                    ImGui.TextUnformatted("姿势");
                    ImGui.SameLine(150);
                    if (metaInfo?.HasPoses ?? false)
                        DrawPoseData(metaInfo, _gposeTarget, _hasValidGposeTarget);
                    else
                        _uiSharedService.BooleanToColoredIcon(false, false);
                }
            }
        }

        using (var yourOwnTabItem = ImRaii.TabItem("你拥有的"))
        {
            using var id = ImRaii.PushId("yourOwnTab");
            if (yourOwnTabItem)
            {
                DrawHelpFoldout("你可以在本标签页应用你拥有的角色数据. 如果列表未能完全显示, 请点击 \"下载你的角色数据\"." + Environment.NewLine + Environment.NewLine
                                 + "要新建或管理角色数据, 请使用 \"在线MCD\" 标签.");

                ImGuiHelpers.ScaledDummy(5);

                using (ImRaii.Disabled(_charaDataManager.GetAllDataTask != null
                    || (_charaDataManager.DataGetTimeoutTask != null && !_charaDataManager.DataGetTimeoutTask.IsCompleted)))
                {
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowCircleDown, "下载你的角色数据"))
                    {
                        _ = _charaDataManager.GetAllData(_disposalCts.Token);
                    }
                }
                if (_charaDataManager.DataGetTimeoutTask != null && !_charaDataManager.DataGetTimeoutTask.IsCompleted)
                {
                    UiSharedService.AttachToolTip("每分钟仅能刷新角色数据一次. 请稍后.");
                }

                ImGuiHelpers.ScaledDummy(5);
                ImGui.Separator();

                using var child = ImRaii.Child("ownDataChild", new(0, 0), false, ImGuiWindowFlags.AlwaysAutoResize);
                using var indent = ImRaii.PushIndent(10f);
                foreach (var data in _charaDataManager.OwnCharaData.Values)
                {
                    var hasMetaInfo = _charaDataManager.TryGetMetaInfo(data.FullId, out var metaInfo);
                    if (!hasMetaInfo) continue;
                    DrawMetaInfoData(_gposeTarget, _hasValidGposeTarget, metaInfo!, true);
                }

                ImGuiHelpers.ScaledDummy(5);
            }
        }

        using (var sharedWithYouTabItem = ImRaii.TabItem("与你共享的", _openDataApplicationShared ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
        {
            using var id = ImRaii.PushId("sharedWithYouTab");
            if (sharedWithYouTabItem)
            {
                DrawHelpFoldout("你可以在本标签中应用其他人与你共享的角色数据. 你仅可以查看 \"共享权限\" 为 \"共享\" 且你有访问权限的数据, " +
                                "举例: 你与所有者直接配对或所有者在角色数据设置中添加了你的UID." + Environment.NewLine + Environment.NewLine
                                + "你可以通过过滤器筛选数据, 点击 \"应用到 <目标>\" 将下载数据并将数据应用到Gpose目标." + Environment.NewLine + Environment.NewLine
                                + "注意: 被暂停的配对的数据不会显示.");

                ImGuiHelpers.ScaledDummy(5);

                DrawUpdateSharedDataButton();

                int activeFilters = 0;
                if (!string.IsNullOrEmpty(_sharedWithYouOwnerFilter)) activeFilters++;
                if (!string.IsNullOrEmpty(_sharedWithYouDescriptionFilter)) activeFilters++;
                if (_sharedWithYouDownloadableFilter) activeFilters++;
                string filtersText = activeFilters == 0 ? "过滤" : $"正在使用 ({activeFilters} 过滤器)";
                UiSharedService.DrawTree($"{filtersText}##filters", () =>
                {
                    var filterWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
                    ImGui.SetNextItemWidth(filterWidth);
                    if (ImGui.InputTextWithHint("##filter", "UID/笔记", ref _sharedWithYouOwnerFilter, 30))
                    {
                        UpdateFilteredItems();
                    }
                    ImGui.SetNextItemWidth(filterWidth);
                    if (ImGui.InputTextWithHint("##filterDesc", "描述", ref _sharedWithYouDescriptionFilter, 50))
                    {
                        UpdateFilteredItems();
                    }
                    if (ImGui.Checkbox("仅显示可下载", ref _sharedWithYouDownloadableFilter))
                    {
                        UpdateFilteredItems();
                    }
                });

                if (_filteredDict == null && _charaDataManager.GetSharedWithYouTask == null)
                {
                    _filteredDict = _charaDataManager.SharedWithYouData
                        .ToDictionary(k =>
                        {
                            var note = _serverConfigurationManager.GetNoteForUid(k.Key.UID);
                            if (note == null) return k.Key.AliasOrUID;
                            return $"{note} ({k.Key.AliasOrUID})";
                        }, k => k.Value, StringComparer.OrdinalIgnoreCase)
                        .Where(k => string.IsNullOrEmpty(_sharedWithYouOwnerFilter) || k.Key.Contains(_sharedWithYouOwnerFilter))
                        .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase).ToDictionary();
                }

                ImGuiHelpers.ScaledDummy(5);
                ImGui.Separator();
                using var child = ImRaii.Child("sharedWithYouChild", new(0, 0), false, ImGuiWindowFlags.AlwaysAutoResize);

                ImGuiHelpers.ScaledDummy(5);
                foreach (var entry in _filteredDict ?? [])
                {
                    bool isFilteredAndHasToBeOpened = entry.Key.Contains(_sharedWithYouOwnerFilter) && _openDataApplicationShared;
                    if (isFilteredAndHasToBeOpened)
                        ImGui.SetNextItemOpen(isFilteredAndHasToBeOpened);
                    UiSharedService.DrawTree($"{entry.Key} - [{entry.Value.Count} 角色数据集]##{entry.Key}", () =>
                    {
                        foreach (var data in entry.Value)
                        {
                            DrawMetaInfoData(_gposeTarget, _hasValidGposeTarget, data);
                        }
                        ImGuiHelpers.ScaledDummy(5);
                    });
                    if (isFilteredAndHasToBeOpened)
                        _openDataApplicationShared = false;
                }
            }
        }

        using (var mcdfTabItem = ImRaii.TabItem("MCDF导入"))
        {
            using var id = ImRaii.PushId("applyMcdfTab");
            if (mcdfTabItem)
            {
                using var child = ImRaii.Child("applyMcdf", new(0, 0), false, ImGuiWindowFlags.AlwaysAutoResize);
                DrawHelpFoldout("你可以在本标签中应用MDCF文件的角色数据." + Environment.NewLine + Environment.NewLine
                                + "点击 \"加载MCDF\" 按钮, 将显示MDCF制作者添加的描述内容." + Environment.NewLine
                                + "你可以将其应用到任何有效的GPose对象." + Environment.NewLine + Environment.NewLine
                                + "点击创建数据标签中的 \"导出MCDF\" 子标签以导出可分享的MDCF文件.");

                ImGuiHelpers.ScaledDummy(5);

                if (_charaDataManager.LoadedMcdfHeader == null || _charaDataManager.LoadedMcdfHeader.IsCompleted)
                {
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.FolderOpen, "加载MCDF"))
                    {
                        _fileDialogManager.OpenFileDialog("选择MCDF文件", ".mcdf", (success, paths) =>
                        {
                            if (!success) return;
                            if (paths.FirstOrDefault() is not string path) return;

                            _configService.Current.LastSavedCharaDataLocation = Path.GetDirectoryName(path) ?? string.Empty;
                            _configService.Save();

                            _charaDataManager.LoadMcdf(path);
                        }, 1, Directory.Exists(_configService.Current.LastSavedCharaDataLocation) ? _configService.Current.LastSavedCharaDataLocation : null);
                    }
                    UiSharedService.AttachToolTip("加载MCDF元数据");
                    if ((_charaDataManager.LoadedMcdfHeader?.IsCompleted ?? false))
                    {
                        ImGui.TextUnformatted("加载的文件");
                        ImGui.SameLine(200);
                        UiSharedService.TextWrapped(_charaDataManager.LoadedMcdfHeader.Result.LoadedFile.FilePath);
                        ImGui.Text("描述");
                        ImGui.SameLine(200);
                        UiSharedService.TextWrapped(_charaDataManager.LoadedMcdfHeader.Result.LoadedFile.CharaFileData.Description);

                        ImGuiHelpers.ScaledDummy(5);

                        using (ImRaii.Disabled(!_hasValidGposeTarget))
                        {
                            if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowRight, "应用"))
                            {
                                _ = _charaDataManager.McdfApplyToGposeTarget();
                            }
                            UiSharedService.AttachToolTip($"应用到 {_gposeTarget}");
                            ImGui.SameLine();
                            using (ImRaii.Disabled(!_charaDataManager.BrioAvailable))
                            {
                                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "生成角色并应用"))
                                {
                                    _charaDataManager.McdfSpawnApplyToGposeTarget();
                                }
                            }
                        }
                    }
                    if ((_charaDataManager.LoadedMcdfHeader?.IsFaulted ?? false) || (_charaDataManager.McdfApplicationTask?.IsFaulted ?? false))
                    {
                        UiSharedService.ColorTextWrapped("读取MCDF文件失败. MCDF文件可能已损坏. 请重新导入文件并重试.",
                            ImGuiColors.DalamudRed);
                        UiSharedService.ColorTextWrapped("注意: 如果这是你的MCDF, 请尝试重绘自己, 然后重新导出文件. " +
                            "如果是其他人的文件请让他们重绘后重新导出.", ImGuiColors.DalamudYellow);
                    }
                }
                else
                {
                    UiSharedService.ColorTextWrapped("正在加载角色...", ImGuiColors.DalamudYellow);
                }
            }
        }
    }

    private void DrawMcdfExport()
    {
        _uiSharedService.BigText("Mare角色数据导出");

        DrawHelpFoldout("此功能允许您将角色导出到MCDF文件中, 并手动将其发送给其他人. MCDF文件只能在集体动作期间通过月海同步器导入." +
            "请注意, 存在他人自制非官方的导出工具来提取其中包含的数据的可能. ");

        ImGuiHelpers.ScaledDummy(5);

        ImGui.Checkbox("##readExport", ref _readExport);
        ImGui.SameLine();
        UiSharedService.TextWrapped("我已了解, 导出我的角色数据并将其发送给其他人会不可避免地泄露我当前的角色外观. 与我共享数据的人可以不受限制地与其他人共享我的数据. ");

        if (_readExport)
        {
            ImGui.Indent();

            ImGui.InputTextWithHint("导出描述", "描述将在文件加载时显示", ref _exportDescription, 255);
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Save, "导出为MCDF"))
            {
                string defaultFileName = string.IsNullOrEmpty(_exportDescription)
                    ? "export.mcdf"
                    : string.Join('_', $"{_exportDescription}.mcdf".Split(Path.GetInvalidFileNameChars()));
                _uiSharedService.FileDialogManager.SaveFileDialog("导出至文件", ".mcdf", defaultFileName, ".mcdf", (success, path) =>
                {
                    if (!success) return;

                    _configService.Current.LastSavedCharaDataLocation = Path.GetDirectoryName(path) ?? string.Empty;
                    _configService.Save();

                    _charaDataManager.SaveMareCharaFile(_exportDescription, path);
                    _exportDescription = string.Empty;
                }, Directory.Exists(_configService.Current.LastSavedCharaDataLocation) ? _configService.Current.LastSavedCharaDataLocation : null);
            }
            UiSharedService.ColorTextWrapped("注意：为了获得最佳效果, 请确保您拥有想要共享的所有内容以及正确的角色外观, " +
                " 并在导出之前重新绘制角色. ", ImGuiColors.DalamudYellow);

            ImGui.Unindent();
        }
    }

    private void DrawMetaInfoData(string selectedGposeActor, bool hasValidGposeTarget, CharaDataMetaInfoExtendedDto data, bool canOpen = false)
    {
        ImGuiHelpers.ScaledDummy(5);
        using var entryId = ImRaii.PushId(data.FullId);

        var startPos = ImGui.GetCursorPosX();
        var maxPos = ImGui.GetWindowContentRegionMax().X;
        var availableWidth = maxPos - startPos;
        UiSharedService.DrawGrouped(() =>
        {
            ImGui.AlignTextToFramePadding();
            DrawAddOrRemoveFavorite(data);

            ImGui.SameLine();
            var favPos = ImGui.GetCursorPosX();
            ImGui.AlignTextToFramePadding();
            UiSharedService.ColorText(data.FullId, UiSharedService.GetBoolColor(data.CanBeDownloaded));
            if (!data.CanBeDownloaded)
            {
                UiSharedService.AttachToolTip("服务器上的文件不完整. 请联系所有者修复. 如果你就是所有者, 在在线MCD标签查看数据.");
            }

            var offsetFromRight = availableWidth - _uiSharedService.GetIconSize(FontAwesomeIcon.Calendar).X - _uiSharedService.GetIconButtonSize(FontAwesomeIcon.ArrowRight).X
                - _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Plus).X - ImGui.GetStyle().ItemSpacing.X * 2;

            ImGui.SameLine();
            ImGui.SetCursorPosX(offsetFromRight);
            _uiSharedService.IconText(FontAwesomeIcon.Calendar);
            UiSharedService.AttachToolTip($"最后更新于: {data.UpdatedDate}");

            ImGui.SameLine();
            GposeMetaInfoAction((meta) =>
            {
                if (_uiSharedService.IconButton(FontAwesomeIcon.ArrowRight))
                {
                    _ = _charaDataManager.ApplyCharaDataToGposeTarget(meta!);
                }
            }, $"应用数据到 {CharaName(selectedGposeActor)}", data, hasValidGposeTarget, false);
            ImGui.SameLine();
            GposeMetaInfoAction((meta) =>
            {
                if (_uiSharedService.IconButton(FontAwesomeIcon.Plus))
                {
                    _ = _charaDataManager.SpawnAndApplyData(meta!);
                }
            }, "生成并应用", data, hasValidGposeTarget, true);

            using var indent = ImRaii.PushIndent(favPos - startPos);

            if (canOpen)
            {
                using (ImRaii.Disabled(_isHandlingSelf))
                {
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.Edit, "在在线MCD编辑器打开"))
                    {
                        SelectedDtoId = data.Id;
                        _openMcdOnlineOnNextRun = true;
                    }
                }
                if (_isHandlingSelf)
                {
                    UiSharedService.AttachToolTip("无法在对自身应用了数据的情况下使用在线MCD功能.");
                }
            }

            if (string.IsNullOrEmpty(data.Description))
            {
                UiSharedService.ColorTextWrapped("无描述", ImGuiColors.DalamudGrey, availableWidth);
            }
            else
            {
                UiSharedService.TextWrapped(data.Description, availableWidth);
            }

            DrawPoseData(data, selectedGposeActor, hasValidGposeTarget);
        });
    }


    private void DrawPoseData(CharaDataMetaInfoExtendedDto? metaInfo, string actor, bool hasValidGposeTarget)
    {
        if (metaInfo == null || !metaInfo.HasPoses) return;

        bool isInGpose = _uiSharedService.IsInGpose;
        var start = ImGui.GetCursorPosX();
        foreach (var item in metaInfo.PoseExtended)
        {
            if (!item.HasPoseData) continue;

            float DrawIcon(float s)
            {
                ImGui.SetCursorPosX(s);
                var posX = ImGui.GetCursorPosX();
                _uiSharedService.IconText(item.HasWorldData ? FontAwesomeIcon.Circle : FontAwesomeIcon.Running);
                if (item.HasWorldData)
                {
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(posX);
                    using var col = ImRaii.PushColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.WindowBg));
                    _uiSharedService.IconText(FontAwesomeIcon.Running);
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(posX);
                    _uiSharedService.IconText(FontAwesomeIcon.Running);
                }
                ImGui.SameLine();
                return ImGui.GetCursorPosX();
            }

            string tooltip = string.IsNullOrEmpty(item.Description) ? "无描述" : "姿势描述: " + item.Description;
            if (!isInGpose)
            {
                start = DrawIcon(start);
                UiSharedService.AttachToolTip(tooltip + UiSharedService.TooltipSeparator + (item.HasWorldData ? GetWorldDataTooltipText(item) + UiSharedService.TooltipSeparator + "点击以在地图上显示" : string.Empty));
                if (item.HasWorldData && ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    _dalamudUtilService.SetMarkerAndOpenMap(item.Position, item.Map);
                }
            }
            else
            {
                tooltip += UiSharedService.TooltipSeparator + $"左键点击: 应用姿势到 {CharaName(actor)}";
                if (item.HasWorldData) tooltip += Environment.NewLine + $"CTRL+右键点击: 应用位置到 {CharaName(actor)}."
                        + UiSharedService.TooltipSeparator + "!!! 警告: 应用位置数据可能会让角色处于错误状态. 风险自负 !!!";
                GposePoseAction(() =>
                {
                    start = DrawIcon(start);
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                    {
                        _ = _charaDataManager.ApplyPoseData(item, actor);
                    }
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && UiSharedService.CtrlPressed())
                    {
                        _ = _charaDataManager.ApplyWorldDataToTarget(item, actor);
                    }
                }, tooltip, hasValidGposeTarget);
                ImGui.SameLine();
            }
        }
        if (metaInfo.PoseExtended.Any()) ImGui.NewLine();
    }

    private void DrawSettings()
    {
        ImGuiHelpers.ScaledDummy(5);
        _uiSharedService.BigText("设置");
        ImGuiHelpers.ScaledDummy(5);
        bool openInGpose = _configService.Current.OpenMareHubOnGposeStart;
        if (ImGui.Checkbox("进入GPose时打开角色数据中心", ref openInGpose))
        {
            _configService.Current.OpenMareHubOnGposeStart = openInGpose;
            _configService.Save();
        }
        _uiSharedService.DrawHelpText("这将在加载到Gpose时自动打开导入菜单. 如果未选中, 您可以使用“/mare gpose”手动打开菜单");
        bool downloadDataOnConnection = _configService.Current.DownloadMcdDataOnConnection;
        if (ImGui.Checkbox("自动下载MCD数据", ref downloadDataOnConnection))
        {
            _configService.Current.DownloadMcdDataOnConnection = downloadDataOnConnection;
            _configService.Save();
        }
        _uiSharedService.DrawHelpText("这将在你连接到服务器时自动下载所有你拥有访问权限的角色数据.");

        bool showHelpTexts = _configService.Current.ShowHelpTexts;
        if (ImGui.Checkbox("显示 \"这是什么? (说明 / 帮助)\" 内容", ref showHelpTexts))
        {
            _configService.Current.ShowHelpTexts = showHelpTexts;
            _configService.Save();
        }

        // ImGui.Checkbox("Abbreviate Chara Names", ref _abbreviateCharaName);
        // _uiSharedService.DrawHelpText("This setting will abbreviate displayed names. This setting is not persistent and will reset between restarts.");

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("最近导出目录");
        ImGui.SameLine(300);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(string.IsNullOrEmpty(_configService.Current.LastSavedCharaDataLocation) ? "无" : _configService.Current.LastSavedCharaDataLocation);
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Ban, "清除最近导出目录"))
        {
            _configService.Current.LastSavedCharaDataLocation = string.Empty;
            _configService.Save();
        }
        _uiSharedService.DrawHelpText("如果加载/保存MCDF文件对话框无法显示, 点击这里");
    }

    private void DrawHelpFoldout(string text)
    {
        if (_configService.Current.ShowHelpTexts)
        {
            ImGuiHelpers.ScaledDummy(5);
            UiSharedService.DrawTree("这是什么? (说明 / 帮助)", () =>
            {
                UiSharedService.TextWrapped(text);
            });
        }
    }

    private void DisableDisabled(Action drawAction)
    {
        if (_disableUI) ImGui.EndDisabled();
        drawAction();
        if (_disableUI) ImGui.BeginDisabled();
    }
}