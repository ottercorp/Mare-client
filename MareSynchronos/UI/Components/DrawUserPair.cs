using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Style;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.API.Dto.User;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;

namespace MareSynchronos.UI.Components;

public class DrawUserPair
{
    protected readonly ApiController _apiController;
    protected readonly IdDisplayHandler _displayHandler;
    protected readonly MareMediator _mediator;
    protected readonly List<GroupFullInfoDto> _syncedGroups;
    private readonly GroupFullInfoDto? _currentGroup;
    protected Pair _pair;
    private readonly string _id;
    private readonly SelectTagForPairUi _selectTagForPairUi;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly UiSharedService _uiSharedService;
    private readonly PlayerPerformanceConfigService _performanceConfigService;
    private readonly CharaDataManager _charaDataManager;
    private float _menuWidth = -1;
    private bool _wasHovered = false;

    public DrawUserPair(string id, Pair entry, List<GroupFullInfoDto> syncedGroups,
        GroupFullInfoDto? currentGroup,
        ApiController apiController, IdDisplayHandler uIDDisplayHandler,
        MareMediator mareMediator, SelectTagForPairUi selectTagForPairUi,
        ServerConfigurationManager serverConfigurationManager,
        UiSharedService uiSharedService, PlayerPerformanceConfigService performanceConfigService,
        CharaDataManager charaDataManager)
    {
        _id = id;
        _pair = entry;
        _syncedGroups = syncedGroups;
        _currentGroup = currentGroup;
        _apiController = apiController;
        _displayHandler = uIDDisplayHandler;
        _mediator = mareMediator;
        _selectTagForPairUi = selectTagForPairUi;
        _serverConfigurationManager = serverConfigurationManager;
        _uiSharedService = uiSharedService;
        _performanceConfigService = performanceConfigService;
        _charaDataManager = charaDataManager;
    }

    public Pair Pair => _pair;
    public UserFullPairDto UserPair => _pair.UserPair!;

    public void DrawPairedClient(bool isSupporter = false)
    {
        using var id = ImRaii.PushId(GetType() + _id);
        var color = ImRaii.PushColor(ImGuiCol.ChildBg, ImGui.GetColorU32(ImGuiCol.FrameBgHovered), _wasHovered);
        using (ImRaii.Child(GetType() + _id, new System.Numerics.Vector2(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImGui.GetFrameHeight())))
        {
            DrawLeftSide();
            ImGui.SameLine();
            var posX = ImGui.GetCursorPosX();
            var rightSide = DrawRightSide();
            DrawName(posX, rightSide, isSupporter);
        }
        _wasHovered = ImGui.IsItemHovered();
        color.Dispose();
    }

    private void DrawCommonClientMenu()
    {
        if (!_pair.IsPaused)
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.User, "打开月海档案", _menuWidth, true))
            {
                _displayHandler.OpenProfile(_pair);
                ImGui.CloseCurrentPopup();
            }
            UiSharedService.AttachToolTip("在新窗口中打开此用户的档案");
        }
        if (_pair.IsVisible)
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Sync, "重新加载最后一次数据", _menuWidth, true))
            {
                _pair.ApplyLastReceivedData(forced: true);
                ImGui.CloseCurrentPopup();
            }
            UiSharedService.AttachToolTip("这将上次接收的角色数据重新应用到此角色");
        }

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.PlayCircle, "暂停循环状态", _menuWidth, true))
        {
            _ = _apiController.CyclePauseAsync(_pair.UserData);
            ImGui.CloseCurrentPopup();
        }
        ImGui.Separator();

        ImGui.TextUnformatted("配对权限功能");
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.WindowMaximize, "打开权限设置窗口", _menuWidth, true))
        {
            _mediator.Publish(new OpenPermissionWindow(_pair));
            ImGui.CloseCurrentPopup();
        }
        UiSharedService.AttachToolTip("打开权限设置窗口来便捷的修改各种配对权限.");

        var isSticky = _pair.UserPair!.OwnPermissions.IsSticky();
        string stickyText = isSticky ? "禁用首选权限配置" : "启用首选权限配置";
        var stickyIcon = isSticky ? FontAwesomeIcon.ArrowCircleDown : FontAwesomeIcon.ArrowCircleUp;
        if (_uiSharedService.IconTextButton(stickyIcon, stickyText, _menuWidth, true))
        {
            var permissions = _pair.UserPair.OwnPermissions;
            permissions.SetSticky(!isSticky);
            _ = _apiController.UserSetPairPermissions(new(_pair.UserData, permissions));
        }
        UiSharedService.AttachToolTip("首选权限配置将不会被配对贝的设置影响.");

        string individualText = Environment.NewLine + Environment.NewLine + "注意: 修改权限会将对该用户的设置设置为"
            + Environment.NewLine + "默认首选配置. 你可以在权限设置中"
            + Environment.NewLine + "修改本设置.";
        bool individual = !_pair.IsDirectlyPaired && _apiController.DefaultPermissions!.IndividualIsSticky;

        var isDisableSounds = _pair.UserPair!.OwnPermissions.IsDisableSounds();
        string disableSoundsText = isDisableSounds ? "启用声音同步" : "禁用声音同步";
        var disableSoundsIcon = isDisableSounds ? FontAwesomeIcon.VolumeUp : FontAwesomeIcon.VolumeMute;
        if (_uiSharedService.IconTextButton(disableSoundsIcon, disableSoundsText, _menuWidth, true))
        {
            var permissions = _pair.UserPair.OwnPermissions;
            permissions.SetDisableSounds(!isDisableSounds);
            _ = _apiController.UserSetPairPermissions(new UserPermissionsDto(_pair.UserData, permissions));
        }
        UiSharedService.AttachToolTip("修改与该用户的声音同步权限设置." + (individual ? individualText : string.Empty));

        var isDisableAnims = _pair.UserPair!.OwnPermissions.IsDisableAnimations();
        string disableAnimsText = isDisableAnims ? "启用动画同步" : "禁用动画同步";
        var disableAnimsIcon = isDisableAnims ? FontAwesomeIcon.Running : FontAwesomeIcon.Stop;
        if (_uiSharedService.IconTextButton(disableAnimsIcon, disableAnimsText, _menuWidth, true))
        {
            var permissions = _pair.UserPair.OwnPermissions;
            permissions.SetDisableAnimations(!isDisableAnims);
            _ = _apiController.UserSetPairPermissions(new UserPermissionsDto(_pair.UserData, permissions));
        }
        UiSharedService.AttachToolTip("修改与该用户的动画同步权限设置." + (individual ? individualText : string.Empty));

        var isDisableVFX = _pair.UserPair!.OwnPermissions.IsDisableVFX();
        string disableVFXText = isDisableVFX ? "启用视效VFX同步" : "禁用视效VFX同步";
        var disableVFXIcon = isDisableVFX ? FontAwesomeIcon.Sun : FontAwesomeIcon.Circle;
        if (_uiSharedService.IconTextButton(disableVFXIcon, disableVFXText, _menuWidth, true))
        {
            var permissions = _pair.UserPair.OwnPermissions;
            permissions.SetDisableVFX(!isDisableVFX);
            _ = _apiController.UserSetPairPermissions(new UserPermissionsDto(_pair.UserData, permissions));
        }
        UiSharedService.AttachToolTip("修改与该用户的VFX同步权限设置." + (individual ? individualText : string.Empty));

        var isShareingLocation = _pair.UserPair!.OwnPermissions.IsEnabledShareLocation();
        string isShareingLocationText = isShareingLocation ? "禁用位置共享" : "启用位置共享";
        var isShareingLocationIcon = isShareingLocation ? FontAwesomeIcon.StopCircle : FontAwesomeIcon.Globe;
        if (_uiSharedService.IconTextButton(isShareingLocationIcon, isShareingLocationText, _menuWidth, true))
        {
            var permissions = _pair.UserPair.OwnPermissions;
            permissions.SetShareLocation(!isShareingLocation);
            _ = _apiController.UserSetPairPermissions(new UserPermissionsDto(_pair.UserData, permissions));
        }
        UiSharedService.AttachToolTip("修改与该用户的位置同步设置." + (individual ? individualText : string.Empty));

        if (!_pair.IsPaused)
        {
            ImGui.Separator();
            ImGui.TextUnformatted("举报用户");
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.ExclamationTriangle, "举报用户", _menuWidth, true))
            {
                ImGui.CloseCurrentPopup();
                _mediator.Publish(new OpenReportPopupMessage(_pair));
            }
            UiSharedService.AttachToolTip("向管理团队举报用户.");
        }
    }

    private void DrawIndividualMenu()
    {
        ImGui.TextUnformatted("独立配对功能");
        var entryUID = _pair.UserData.AliasOrUID;

        if (_pair.IndividualPairStatus != API.Data.Enum.IndividualPairStatus.None)
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Folder, "配对组", _menuWidth, true))
            {
                _selectTagForPairUi.Open(_pair);
            }
            UiSharedService.AttachToolTip("为 " + entryUID + "选择配对组");
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "永久取消独立配对", _menuWidth, true) && UiSharedService.CtrlPressed())
            {
                _ = _apiController.UserRemovePair(new(_pair.UserData));
            }
            UiSharedService.AttachToolTip("按住CTRL并点击来与 " + entryUID + "取消配对");
        }
        else
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "独立配对", _menuWidth, true))
            {
                _ = _apiController.UserAddPair(new(_pair.UserData));
            }
            UiSharedService.AttachToolTip("与 " + entryUID + " 独立配对");
        }
    }

    private void DrawLeftSide()
    {
        string userPairText = string.Empty;

        ImGui.AlignTextToFramePadding();

        if (_pair.IsPaused)
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
            _uiSharedService.IconText(FontAwesomeIcon.PauseCircle);
            userPairText = _pair.UserData.AliasOrUID + " 已暂停";
        }
        else if (!_pair.IsOnline)
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
            _uiSharedService.IconText(_pair.IndividualPairStatus == API.Data.Enum.IndividualPairStatus.OneSided
                ? FontAwesomeIcon.ArrowsLeftRight
                : (_pair.IndividualPairStatus == API.Data.Enum.IndividualPairStatus.Bidirectional
                    ? FontAwesomeIcon.User : FontAwesomeIcon.Users));
            userPairText = _pair.UserData.AliasOrUID + " 离线";
        }
        else if (_pair.IsVisible)
        {
            _uiSharedService.IconText(FontAwesomeIcon.Eye, ImGuiColors.ParsedGreen);
            userPairText = _pair.UserData.AliasOrUID + " 可见: " + _pair.PlayerName + Environment.NewLine + "点击以选中角色";
            if (ImGui.IsItemClicked())
            {
                _mediator.Publish(new TargetPairMessage(_pair));
            }
        }
        else
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen);
            _uiSharedService.IconText(_pair.IndividualPairStatus == API.Data.Enum.IndividualPairStatus.Bidirectional
                ? FontAwesomeIcon.User : FontAwesomeIcon.Users);
            userPairText = _pair.UserData.AliasOrUID + " 在线";
        }

        if (_pair.IndividualPairStatus == API.Data.Enum.IndividualPairStatus.OneSided)
        {
            userPairText += UiSharedService.TooltipSeparator + "用户还没有添加你";
        }
        else if (_pair.IndividualPairStatus == API.Data.Enum.IndividualPairStatus.Bidirectional)
        {
            userPairText += UiSharedService.TooltipSeparator + "已独立配对";
        }

        if (_pair.LastAppliedDataBytes >= 0)
        {
            userPairText += UiSharedService.TooltipSeparator;
            userPairText += ((!_pair.IsPaired) ? "(最近) " : string.Empty) + "Mod信息" + Environment.NewLine;
            userPairText += "文件大小: " + UiSharedService.ByteToString(_pair.LastAppliedDataBytes, true);
            if (_pair.LastAppliedApproximateVRAMBytes >= 0)
            {
                userPairText += Environment.NewLine + "预计VRAM占用: " + UiSharedService.ByteToString(_pair.LastAppliedApproximateVRAMBytes, true);
            }
            if (_pair.LastAppliedDataTris >= 0)
            {
                userPairText += Environment.NewLine + "预计模型面数 (非原生): "
                    + (_pair.LastAppliedDataTris > 1000 ? (_pair.LastAppliedDataTris / 1000d).ToString("0.0'k'") : _pair.LastAppliedDataTris);
            }
        }

        if (_syncedGroups.Any())
        {
            userPairText += UiSharedService.TooltipSeparator + string.Join(Environment.NewLine,
                _syncedGroups.Select(g =>
                {
                    var groupNote = _serverConfigurationManager.GetNoteForGid(g.GID);
                    var groupString = string.IsNullOrEmpty(groupNote) ? g.GroupAliasOrGID : $"{groupNote} ({g.GroupAliasOrGID})";
                    return "通过 " + groupString + " 配对";
                }));
        }

        UiSharedService.AttachToolTip(userPairText);

        if (_performanceConfigService.Current.ShowPerformanceIndicator
            && !_performanceConfigService.Current.UIDsToIgnore
                .Exists(uid => string.Equals(uid, UserPair.User.Alias, StringComparison.Ordinal) || string.Equals(uid, UserPair.User.UID, StringComparison.Ordinal))
            && ((_performanceConfigService.Current.VRAMSizeWarningThresholdMiB > 0 && _performanceConfigService.Current.VRAMSizeWarningThresholdMiB * 1024 * 1024 < _pair.LastAppliedApproximateVRAMBytes)
                || (_performanceConfigService.Current.TrisWarningThresholdThousands > 0 && _performanceConfigService.Current.TrisWarningThresholdThousands * 1000 < _pair.LastAppliedDataTris))
            && (!_pair.UserPair.OwnPermissions.IsSticky()
                || _performanceConfigService.Current.WarnOnPreferredPermissionsExceedingThresholds))
        {
            ImGui.SameLine();

            _uiSharedService.IconText(FontAwesomeIcon.ExclamationTriangle, ImGuiColors.DalamudYellow);

            string userWarningText = "警告: 该用户超过了你设置的警告阈值:" + UiSharedService.TooltipSeparator;
            bool shownVram = false;
            if (_performanceConfigService.Current.VRAMSizeWarningThresholdMiB > 0
                && _performanceConfigService.Current.VRAMSizeWarningThresholdMiB * 1024 * 1024 < _pair.LastAppliedApproximateVRAMBytes)
            {
                shownVram = true;
                userWarningText += $"预计. VRAM 用量: 已使用: {UiSharedService.ByteToString(_pair.LastAppliedApproximateVRAMBytes)}, 阈值: {_performanceConfigService.Current.VRAMSizeWarningThresholdMiB} MiB";
            }
            if (_performanceConfigService.Current.TrisWarningThresholdThousands > 0
                && _performanceConfigService.Current.TrisWarningThresholdThousands * 1024 < _pair.LastAppliedDataTris)
            {
                if (shownVram) userWarningText += Environment.NewLine;
                userWarningText += $"预计. 面数: 已使用: {_pair.LastAppliedDataTris}, 阈值: {_performanceConfigService.Current.TrisWarningThresholdThousands * 1000}";
            }

            UiSharedService.AttachToolTip(userWarningText);
        }

        ImGui.SameLine();
    }

    private void DrawName(float leftSide, float rightSide, bool isSupporter = false)
    {
        _displayHandler.DrawPairText(_id, _pair, leftSide, () => rightSide - leftSide, isSupporter);
    }

    private void DrawPairedClientMenu()
    {
        DrawIndividualMenu();

        if (_syncedGroups.Any()) ImGui.Separator();
        foreach (var entry in _syncedGroups)
        {
            bool selfIsOwner = string.Equals(_apiController.UID, entry.Owner.UID, StringComparison.Ordinal);
            bool selfIsModerator = entry.GroupUserInfo.IsModerator();
            bool userIsModerator = entry.GroupPairUserInfos.TryGetValue(_pair.UserData.UID, out var modinfo) && modinfo.IsModerator();
            bool userIsPinned = entry.GroupPairUserInfos.TryGetValue(_pair.UserData.UID, out var info) && info.IsPinned();
            if (selfIsOwner || selfIsModerator)
            {
                var groupNote = _serverConfigurationManager.GetNoteForGid(entry.GID);
                var groupString = string.IsNullOrEmpty(groupNote) ? entry.GroupAliasOrGID : $"{groupNote} ({entry.GroupAliasOrGID})";

                if (ImGui.BeginMenu(groupString + " 管理功能"))
                {
                    DrawSyncshellMenu(entry, selfIsOwner, selfIsModerator, userIsPinned, userIsModerator);
                    ImGui.EndMenu();
                }
            }
        }
    }

    private float DrawRightSide()
    {
        var pauseIcon = _pair.UserPair!.OwnPermissions.IsPaused() ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;
        var pauseButtonSize = _uiSharedService.GetIconButtonSize(pauseIcon);
        var barButtonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.EllipsisV);
        var spacingX = ImGui.GetStyle().ItemSpacing.X;
        var windowEndX = ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth();
        float currentRightSide = windowEndX - barButtonSize.X;

        ImGui.SameLine(currentRightSide);
        ImGui.AlignTextToFramePadding();
        if (_uiSharedService.IconButton(FontAwesomeIcon.EllipsisV))
        {
            ImGui.OpenPopup("User Flyout Menu");
        }

        currentRightSide -= (pauseButtonSize.X + spacingX);
        ImGui.SameLine(currentRightSide);
        if (_uiSharedService.IconButton(pauseIcon))
        {
            var perm = _pair.UserPair!.OwnPermissions;

            if (UiSharedService.CtrlPressed() && !perm.IsPaused())
            {
                perm.SetSticky(true);
            }
            perm.SetPaused(!perm.IsPaused());
            _ = _apiController.UserSetPairPermissions(new(_pair.UserData, perm));
        }
        UiSharedService.AttachToolTip(!_pair.UserPair!.OwnPermissions.IsPaused()
            ? ("暂停与 " + _pair.UserData.AliasOrUID + " 的配对"
                + (_pair.UserPair!.OwnPermissions.IsSticky()
                    ? string.Empty
                    : UiSharedService.TooltipSeparator + "按住CTRL以应用独立配对设置." + Environment.NewLine + "这将暂停与目标的配对, 即使配对贝中存在目标角色."))
            : "恢复与" + _pair.UserData.AliasOrUID + " 的配对");

        if (_pair.IsPaired)
        {
            var individualSoundsDisabled = (_pair.UserPair?.OwnPermissions.IsDisableSounds() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableSounds() ?? false);
            var individualAnimDisabled = (_pair.UserPair?.OwnPermissions.IsDisableAnimations() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableAnimations() ?? false);
            var individualVFXDisabled = (_pair.UserPair?.OwnPermissions.IsDisableVFX() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableVFX() ?? false);
            var individualIsSticky = _pair.UserPair!.OwnPermissions.IsSticky();
            var individualIcon = individualIsSticky ? FontAwesomeIcon.ArrowCircleUp : FontAwesomeIcon.InfoCircle;

            var shareLocationIcon = FontAwesomeIcon.Globe;
            var shareLocation = _pair.UserPair?.OwnPermissions.IsEnabledShareLocation() ?? false;
            var shareLocationOther =  _pair.UserPair?.OtherPermissions.IsEnabledShareLocation() ?? false;
            var shareColor = shareLocation switch
            {
                true when shareLocationOther => ImGuiColors.HealerGreen,
                false when shareLocationOther => ImGuiColors.TankBlue,
                _ => ImGuiColors.DalamudYellow
            };

            if (shareLocation || shareLocationOther)
            {
                currentRightSide -= (_uiSharedService.GetIconSize(shareLocationIcon).X + spacingX);
                ImGui.SameLine(currentRightSide);
                using (ImRaii.PushColor(ImGuiCol.Text, shareColor, shareLocation || shareLocationOther))
                    _uiSharedService.IconText(shareLocationIcon);

                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    if (shareLocationOther)
                    {
                        var location = _apiController.GetUserLocation(_pair.UserPair!.User.UID);
                        if (_pair.IsOnline)
                        {
                            if (!string.IsNullOrEmpty(location))
                            {
                                _uiSharedService.IconText(FontAwesomeIcon.LocationArrow);
                                ImGui.SameLine();
                                ImGui.TextUnformatted(location);
                            }
                            else
                            {
                                ImGui.TextUnformatted("对方未更新位置信息, 请重新连接服务器以刷新或等待对方切换区域.");
                            }
                        }
                        else
                        {
                            ImGui.TextUnformatted("该用户不在线ㄟ( ▔, ▔ )ㄏ");
                        }
                    }
                    else
                    {
                        ImGui.TextUnformatted("该用户未对你共享位置(⊙x⊙;)");
                    }
                    ImGui.Separator();

                    if (shareLocation)
                    {
                        ImGui.TextUnformatted("正在与该用户共享你的位置ヾ(•ω•`)o");
                    }
                    ImGui.EndTooltip();
                }

            }

            if (individualAnimDisabled || individualSoundsDisabled || individualVFXDisabled || individualIsSticky)
            {
                currentRightSide -= (_uiSharedService.GetIconSize(individualIcon).X + spacingX);

                ImGui.SameLine(currentRightSide);
                using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow, individualAnimDisabled || individualSoundsDisabled || individualVFXDisabled))
                    _uiSharedService.IconText(individualIcon);
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();

                    ImGui.TextUnformatted("独立配对设置");
                    ImGui.Separator();

                    if (individualIsSticky)
                    {
                        _uiSharedService.IconText(individualIcon);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted("首选权限设置已启用");
                        if (individualAnimDisabled || individualSoundsDisabled || individualVFXDisabled)
                            ImGui.Separator();
                    }

                    if (individualSoundsDisabled)
                    {
                        var userSoundsText = "同步声音";
                        _uiSharedService.IconText(FontAwesomeIcon.VolumeOff);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted(userSoundsText);
                        ImGui.NewLine();
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted("你");
                        _uiSharedService.BooleanToColoredIcon(!_pair.UserPair!.OwnPermissions.IsDisableSounds());
                        ImGui.SameLine();
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted("他们");
                        _uiSharedService.BooleanToColoredIcon(!_pair.UserPair!.OtherPermissions.IsDisableSounds());
                    }

                    if (individualAnimDisabled)
                    {
                        var userAnimText = "同步动画";
                        _uiSharedService.IconText(FontAwesomeIcon.Stop);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.AlignTextToFramePadding();
                        ImGui.NewLine();
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.SameLine();
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted("他们");
                        _uiSharedService.BooleanToColoredIcon(!_pair.UserPair!.OtherPermissions.IsDisableAnimations());
                    }

                    if (individualVFXDisabled)
                    {
                        var userVFXText = "同步VFX";
                        _uiSharedService.IconText(FontAwesomeIcon.Circle);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted(userVFXText);
                        ImGui.NewLine();
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted("你");
                        _uiSharedService.BooleanToColoredIcon(!_pair.UserPair!.OwnPermissions.IsDisableVFX());
                        ImGui.SameLine();
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted("他们");
                        _uiSharedService.BooleanToColoredIcon(!_pair.UserPair!.OtherPermissions.IsDisableVFX());
                    }

                    ImGui.EndTooltip();
                }
            }
        }

        if (_charaDataManager.SharedWithYouData.TryGetValue(_pair.UserData, out var sharedData))
        {
            currentRightSide -= (_uiSharedService.GetIconSize(FontAwesomeIcon.Running).X + (spacingX / 2f));
            ImGui.SameLine(currentRightSide);
            _uiSharedService.IconText(FontAwesomeIcon.Running);
            UiSharedService.AttachToolTip($"该用户分享了 {sharedData.Count} 个角色数据." + UiSharedService.TooltipSeparator
                + "点击打开角色数据界面来查看.");
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                _mediator.Publish(new OpenCharaDataHubWithFilterMessage(_pair.UserData));
            }
        }

        if (_currentGroup != null)
        {
            var icon = FontAwesomeIcon.None;
            var text = string.Empty;
            if (string.Equals(_currentGroup.OwnerUID, _pair.UserData.UID, StringComparison.Ordinal))
            {
                icon = FontAwesomeIcon.Crown;
                text = "用户是本配对贝的所有者";
            }
            else if (_currentGroup.GroupPairUserInfos.TryGetValue(_pair.UserData.UID, out var userinfo))
            {
                if (userinfo.IsModerator())
                {
                    icon = FontAwesomeIcon.UserShield;
                    text = "用户是本配对贝的管理员";
                }
                else if (userinfo.IsPinned())
                {
                    icon = FontAwesomeIcon.Thumbtack;
                    text = "用户在本配对贝中被置顶";
                }
            }

            if (!string.IsNullOrEmpty(text))
            {
                currentRightSide -= (_uiSharedService.GetIconSize(icon).X + spacingX);
                ImGui.SameLine(currentRightSide);
                _uiSharedService.IconText(icon);
                UiSharedService.AttachToolTip(text);
            }
        }

        if (ImGui.BeginPopup("User Flyout Menu"))
        {
            using (ImRaii.PushId($"buttons-{_pair.UserData.UID}"))
            {
                ImGui.TextUnformatted("通常配对设置");
                DrawCommonClientMenu();
                ImGui.Separator();
                DrawPairedClientMenu();
                if (_menuWidth <= 0)
                {
                    _menuWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
                }
            }

            ImGui.EndPopup();
        }

        return currentRightSide - spacingX;
    }

    private void DrawSyncshellMenu(GroupFullInfoDto group, bool selfIsOwner, bool selfIsModerator, bool userIsPinned, bool userIsModerator)
    {
        if (selfIsOwner || ((selfIsModerator) && (!userIsModerator)))
        {
            ImGui.TextUnformatted("配对贝管理设置");
            var pinText = userIsPinned ? "取消置顶用户" : "置顶用户";
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Thumbtack, pinText, _menuWidth, true))
            {
                ImGui.CloseCurrentPopup();
                if (!group.GroupPairUserInfos.TryGetValue(_pair.UserData.UID, out var userinfo))
                {
                    userinfo = API.Data.Enum.GroupPairUserInfo.IsPinned;
                }
                else
                {
                    userinfo.SetPinned(!userinfo.IsPinned());
                }
                _ = _apiController.GroupSetUserInfo(new GroupPairUserInfoDto(group.Group, _pair.UserData, userinfo));
            }
            UiSharedService.AttachToolTip("在同步贝中置顶用户. 置顶用户不会在手动清理中被删除");

            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "移除用户", _menuWidth, true) && UiSharedService.CtrlPressed())
            {
                ImGui.CloseCurrentPopup();
                _ = _apiController.GroupRemoveUser(new(group.Group, _pair.UserData));
            }
            UiSharedService.AttachToolTip("按住CTRL并点击,从贝中移除 " + (_pair.UserData.AliasOrUID));

            if (_uiSharedService.IconTextButton(FontAwesomeIcon.UserSlash, "封禁用户", _menuWidth, true))
            {
                _mediator.Publish(new OpenBanUserPopupMessage(_pair, group));
                ImGui.CloseCurrentPopup();
            }
            UiSharedService.AttachToolTip("从同步贝中封禁用户");

            ImGui.Separator();
        }

        if (selfIsOwner)
        {
            ImGui.TextUnformatted("配对贝所有者设置");
            string modText = userIsModerator ? "取消管理员" : "设为管理员";
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.UserShield, modText, _menuWidth, true) && UiSharedService.CtrlPressed())
            {
                ImGui.CloseCurrentPopup();
                if (!group.GroupPairUserInfos.TryGetValue(_pair.UserData.UID, out var userinfo))
                {
                    userinfo = API.Data.Enum.GroupPairUserInfo.IsModerator;
                }
                else
                {
                    userinfo.SetModerator(!userinfo.IsModerator());
                }

                _ = _apiController.GroupSetUserInfo(new GroupPairUserInfoDto(group.Group, _pair.UserData, userinfo));
            }
            UiSharedService.AttachToolTip("按住CTRL修改 " + (_pair.UserData.AliasOrUID) + " 的管理员权限" + Environment.NewLine +
                "管理员可以踢出, 封禁/取消封禁, 置顶/取消置顶用户或清空同步贝.");

            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Crown, "转移所有权", _menuWidth, true) && UiSharedService.CtrlPressed() && UiSharedService.ShiftPressed())
            {
                ImGui.CloseCurrentPopup();
                _ = _apiController.GroupChangeOwnership(new(group.Group, _pair.UserData));
            }
            UiSharedService.AttachToolTip("按住CTRL+SHIFT并点击,将通讯贝的所有权转移给 "
                + (_pair.UserData.AliasOrUID) + Environment.NewLine + "注意: 这个操作无法取消.");
        }
    }
}