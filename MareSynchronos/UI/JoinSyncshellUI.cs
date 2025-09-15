using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI;

internal class JoinSyncshellUI : WindowMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly UiSharedService _uiSharedService;
    private string _desiredSyncshellToJoin = string.Empty;
    private GroupJoinInfoDto? _groupJoinInfo = null;
    private DefaultPermissionsDto _ownPermissions = null!;
    private string _previousPassword = string.Empty;
    private string _syncshellPassword = string.Empty;

    public JoinSyncshellUI(ILogger<JoinSyncshellUI> logger, MareMediator mediator,
        UiSharedService uiSharedService, ApiController apiController, PerformanceCollectorService performanceCollectorService) 
        : base(logger, mediator, "加入已有同步贝###MareSynchronosJoinSyncshell", performanceCollectorService)
    {
        _uiSharedService = uiSharedService;
        _apiController = apiController;
        SizeConstraints = new()
        {
            MinimumSize = new(700, 400),
            MaximumSize = new(700, 400)
        };

        Mediator.Subscribe<DisconnectedMessage>(this, (_) => IsOpen = false);

        Flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize;
    }

    public override void OnOpen()
    {
        _desiredSyncshellToJoin = string.Empty;
        _syncshellPassword = string.Empty;
        _previousPassword = string.Empty;
        _groupJoinInfo = null;
        _ownPermissions = _apiController.DefaultPermissions.DeepClone()!;
    }

    protected override void DrawInternal()
    {
        using (_uiSharedService.UidFont.Push())
            ImGui.TextUnformatted(_groupJoinInfo == null || !_groupJoinInfo.Success ? "加入同步贝" : "尝试加入同步贝 " + _groupJoinInfo.GroupAliasOrGID);
        ImGui.Separator();

        if (_groupJoinInfo == null || !_groupJoinInfo.Success)
        {
            UiSharedService.TextWrapped("你可以在此加入已有的同步贝. " +
                "请注意你最多只能加入 " + _apiController.ServerInfo.MaxGroupsJoinedByUser + " 个同步贝." + Environment.NewLine +
                "加入同步贝会让你与所有同步贝中的成员同步." + Environment.NewLine +
                "同步贝中所有用户的同步权限会被设置为该同步贝的首选权限, 除了已经被你设置过权限的用户.");
            ImGui.Separator();
            ImGui.TextUnformatted("注意: 同步贝ID和密码区分大小写. MSS- 是同步贝ID的一部分, 使用个性化ID的除外.");

            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("同步贝ID");
            ImGui.SameLine(200);
            ImGui.InputTextWithHint("##syncshellId", "同步贝ID", ref _desiredSyncshellToJoin, 20);

            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("同步贝密码");
            ImGui.SameLine(200);
            ImGui.InputTextWithHint("##syncshellpw", "密码", ref _syncshellPassword, 50, ImGuiInputTextFlags.Password);
            using (ImRaii.Disabled(string.IsNullOrEmpty(_desiredSyncshellToJoin) || string.IsNullOrEmpty(_syncshellPassword)))
            {
                if (_uiSharedService.IconTextButton(Dalamud.Interface.FontAwesomeIcon.Plus, "加入同步贝"))
                {
                    _groupJoinInfo = _apiController.GroupJoin(new GroupPasswordDto(new API.Data.GroupData(_desiredSyncshellToJoin), _syncshellPassword)).Result;
                    _previousPassword = _syncshellPassword;
                    _syncshellPassword = string.Empty;
                }
            }
            if (_groupJoinInfo != null && !_groupJoinInfo.Success)
            {
                UiSharedService.ColorTextWrapped("加入同步贝失败. 以下是可能的原因:" + Environment.NewLine +
                    "- 同步贝不存在或密码错误" + Environment.NewLine +
                    "- 你已经加入了该同步贝或已被该同步贝封禁" + Environment.NewLine +
                    "- 同步贝人数已达上限或关闭了邀请功能" + Environment.NewLine, ImGuiColors.DalamudYellow);
            }
        }
        else
        {
            ImGui.TextUnformatted("你即将加入同步贝 " + _groupJoinInfo.GroupAliasOrGID + " 所有者为 " + _groupJoinInfo.OwnerAliasOrUID);
            ImGuiHelpers.ScaledDummy(2f);
            ImGui.TextUnformatted("同步贝管理员设置了如下的默认同步权限:");
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("- 声音 ");
            _uiSharedService.BooleanToColoredIcon(!_groupJoinInfo.GroupPermissions.IsPreferDisableSounds());
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("- 动画");
            _uiSharedService.BooleanToColoredIcon(!_groupJoinInfo.GroupPermissions.IsPreferDisableAnimations());
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("- VFX");
            _uiSharedService.BooleanToColoredIcon(!_groupJoinInfo.GroupPermissions.IsPreferDisableVFX());

            if (_groupJoinInfo.GroupPermissions.IsPreferDisableSounds() != _ownPermissions.DisableGroupSounds
                || _groupJoinInfo.GroupPermissions.IsPreferDisableVFX() != _ownPermissions.DisableGroupVFX
                || _groupJoinInfo.GroupPermissions.IsPreferDisableAnimations() != _ownPermissions.DisableGroupAnimations)
            {
                ImGuiHelpers.ScaledDummy(2f);
                UiSharedService.ColorText("你当前的首选权限设置与贝的默认设置不同:", ImGuiColors.DalamudYellow);
                if (_groupJoinInfo.GroupPermissions.IsPreferDisableSounds() != _ownPermissions.DisableGroupSounds)
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("- 声音");
                    _uiSharedService.BooleanToColoredIcon(!_ownPermissions.DisableGroupSounds);
                    ImGui.SameLine(200);
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("推荐的设置");
                    _uiSharedService.BooleanToColoredIcon(!_groupJoinInfo.GroupPermissions.IsPreferDisableSounds());
                    ImGui.SameLine();
                    using var id = ImRaii.PushId("suggestedSounds");
                    if (_uiSharedService.IconTextButton(Dalamud.Interface.FontAwesomeIcon.ArrowRight, "使用推荐的设置"))
                    {
                        _ownPermissions.DisableGroupSounds = _groupJoinInfo.GroupPermissions.IsPreferDisableSounds();
                    }
                }
                if (_groupJoinInfo.GroupPermissions.IsPreferDisableAnimations() != _ownPermissions.DisableGroupAnimations)
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("- 动画");
                    _uiSharedService.BooleanToColoredIcon(!_ownPermissions.DisableGroupAnimations);
                    ImGui.SameLine(200);
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("推荐的设置");
                    _uiSharedService.BooleanToColoredIcon(!_groupJoinInfo.GroupPermissions.IsPreferDisableAnimations());
                    ImGui.SameLine();
                    using var id = ImRaii.PushId("suggestedAnims");
                    if (_uiSharedService.IconTextButton(Dalamud.Interface.FontAwesomeIcon.ArrowRight, "使用推荐的设置"))
                    {
                        _ownPermissions.DisableGroupAnimations = _groupJoinInfo.GroupPermissions.IsPreferDisableAnimations();
                    }
                }
                if (_groupJoinInfo.GroupPermissions.IsPreferDisableVFX() != _ownPermissions.DisableGroupVFX)
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("- VFX");
                    _uiSharedService.BooleanToColoredIcon(!_ownPermissions.DisableGroupVFX);
                    ImGui.SameLine(200);
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("推荐的设置");
                    _uiSharedService.BooleanToColoredIcon(!_groupJoinInfo.GroupPermissions.IsPreferDisableVFX());
                    ImGui.SameLine();
                    using var id = ImRaii.PushId("suggestedVfx");
                    if (_uiSharedService.IconTextButton(Dalamud.Interface.FontAwesomeIcon.ArrowRight, "使用推荐的设置"))
                    {
                        _ownPermissions.DisableGroupVFX = _groupJoinInfo.GroupPermissions.IsPreferDisableVFX();
                    }
                }
                UiSharedService.TextWrapped("注意: 你并非一定要修改以上的同步设置, 这只是同步贝管理们的推荐设置.");
            }
            else
            {
                UiSharedService.TextWrapped("你对当前同步贝的同步设置将使用同步贝的推荐设置.");
            }
            ImGuiHelpers.ScaledDummy(2f);
            if (_uiSharedService.IconTextButton(Dalamud.Interface.FontAwesomeIcon.Plus, "确认并加入 " + _groupJoinInfo.GroupAliasOrGID))
            {
                GroupUserPreferredPermissions joinPermissions = GroupUserPreferredPermissions.NoneSet;
                joinPermissions.SetDisableSounds(_ownPermissions.DisableGroupSounds);
                joinPermissions.SetDisableAnimations(_ownPermissions.DisableGroupAnimations);
                joinPermissions.SetDisableVFX(_ownPermissions.DisableGroupVFX);
                _ = _apiController.GroupJoinFinalize(new GroupJoinDto(_groupJoinInfo.Group, _previousPassword, joinPermissions));
                IsOpen = false;
            }
        }
    }
}