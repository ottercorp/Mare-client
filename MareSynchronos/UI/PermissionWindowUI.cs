using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI;

public class PermissionWindowUI : WindowMediatorSubscriberBase
{
    public Pair Pair { get; init; }

    private readonly UiSharedService _uiSharedService;
    private readonly ApiController _apiController;
    private UserPermissions _ownPermissions;

    public PermissionWindowUI(ILogger<PermissionWindowUI> logger, Pair pair, MareMediator mediator, UiSharedService uiSharedService,
        ApiController apiController, PerformanceCollectorService performanceCollectorService)
        : base(logger, mediator, "对 " + pair.UserData.AliasOrUID + " 的权限设置###MareSynchronosPermissions" + pair.UserData.UID, performanceCollectorService)
    {
        Pair = pair;
        _uiSharedService = uiSharedService;
        _apiController = apiController;
        _ownPermissions = pair.UserPair.OwnPermissions.DeepClone();
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoResize;
        SizeConstraints = new()
        {
            MinimumSize = new(450, 100),
            MaximumSize = new(450, 500)
        };
        IsOpen = true;
    }

    protected override void DrawInternal()
    {
        var sticky = _ownPermissions.IsSticky();
        var paused = _ownPermissions.IsPaused();
        var disableSounds = _ownPermissions.IsDisableSounds();
        var disableAnimations = _ownPermissions.IsDisableAnimations();
        var disableVfx = _ownPermissions.IsDisableVFX();
        var style = ImGui.GetStyle();
        var indentSize = ImGui.GetFrameHeight() + style.ItemSpacing.X;

        _uiSharedService.BigText("权限设置 " + Pair.UserData.AliasOrUID);
        ImGuiHelpers.ScaledDummy(1f);

        if (ImGui.Checkbox("首选权限", ref sticky))
        {
            _ownPermissions.SetSticky(sticky);
        }
        _uiSharedService.DrawHelpText("当使用首选权限时, 你对该用户的设置将覆盖你对同步贝的所有权限设置.");

        ImGuiHelpers.ScaledDummy(1f);


        if (ImGui.Checkbox("暂停同步", ref paused))
        {
            _ownPermissions.SetPaused(paused);
        }
        _uiSharedService.DrawHelpText("这将完全暂停与目标用户的同步." + UiSharedService.TooltipSeparator
            + "注意: 任意一方用户暂停同步都会使得双方的同步停止.");
        var otherPerms = Pair.UserPair.OtherPermissions;

        var otherIsPaused = otherPerms.IsPaused();
        var otherDisableSounds = otherPerms.IsDisableSounds();
        var otherDisableAnimations = otherPerms.IsDisableAnimations();
        var otherDisableVFX = otherPerms.IsDisableVFX();

        using (ImRaii.PushIndent(indentSize, false))
        {
            _uiSharedService.BooleanToColoredIcon(!otherIsPaused, false);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.Text(Pair.UserData.AliasOrUID + " 与你 " + (!otherIsPaused ? "未 " : string.Empty) + "暂停同步");
        }

        ImGuiHelpers.ScaledDummy(0.5f);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(0.5f);

        if (ImGui.Checkbox("暂停声音", ref disableSounds))
        {
            _ownPermissions.SetDisableSounds(disableSounds);
        }
        _uiSharedService.DrawHelpText("这将完全暂停与目标用户的声音同步." + UiSharedService.TooltipSeparator
            + "注意: 任意一方用户暂停声音同步都会使得双方的声音同步停止.");
        using (ImRaii.PushIndent(indentSize, false))
        {
            _uiSharedService.BooleanToColoredIcon(!otherDisableSounds, false);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.Text(Pair.UserData.AliasOrUID + " 与你 " + (!otherDisableSounds ? "未 " : string.Empty) + "暂停声音同步");
        }

        if (ImGui.Checkbox("暂停动画", ref disableAnimations))
        {
            _ownPermissions.SetDisableAnimations(disableAnimations);
        }
        _uiSharedService.DrawHelpText("这将完全暂停与目标用户的动画同步." + UiSharedService.TooltipSeparator
            + "注意: 任意一方用户暂停动画同步都会使得双方的动画同步停止.");
        using (ImRaii.PushIndent(indentSize, false))
        {
            _uiSharedService.BooleanToColoredIcon(!otherDisableAnimations, false);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.Text(Pair.UserData.AliasOrUID + " 与你 " + (!otherDisableAnimations ? "未 " : string.Empty) + "暂停动画同步");
        }

        if (ImGui.Checkbox("暂停VFX", ref disableVfx))
        {
            _ownPermissions.SetDisableVFX(disableVfx);
        }
        _uiSharedService.DrawHelpText("这将完全暂停与目标用户的VFX同步." + UiSharedService.TooltipSeparator
            + "注意: 任意一方用户暂停VFX同步都会使得双方的VFX同步停止.");
        using (ImRaii.PushIndent(indentSize, false))
        {
            _uiSharedService.BooleanToColoredIcon(!otherDisableVFX, false);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.Text(Pair.UserData.AliasOrUID + " 与你 " + (!otherDisableVFX ? "未 " : string.Empty) + "暂停VFX同步");
        }

        ImGuiHelpers.ScaledDummy(0.5f);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(0.5f);

        bool hasChanges = _ownPermissions != Pair.UserPair.OwnPermissions;

        using (ImRaii.Disabled(!hasChanges))
            if (_uiSharedService.IconTextButton(Dalamud.Interface.FontAwesomeIcon.Save, "保存"))
            {
                _ = _apiController.SetBulkPermissions(new(
                    new(StringComparer.Ordinal)
                    {
                        { Pair.UserData.UID, _ownPermissions }
                    },
                    new(StringComparer.Ordinal)
                ));
            }
        UiSharedService.AttachToolTip("保存并应用");

        var rightSideButtons = _uiSharedService.GetIconTextButtonSize(Dalamud.Interface.FontAwesomeIcon.Undo, "撤销") +
            _uiSharedService.GetIconTextButtonSize(Dalamud.Interface.FontAwesomeIcon.ArrowsSpin, "恢复默认设置");
        var availableWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;

        ImGui.SameLine(availableWidth - rightSideButtons);

        using (ImRaii.Disabled(!hasChanges))
            if (_uiSharedService.IconTextButton(Dalamud.Interface.FontAwesomeIcon.Undo, "撤销"))
            {
                _ownPermissions = Pair.UserPair.OwnPermissions.DeepClone();
            }
        UiSharedService.AttachToolTip("撤销所有改动");

        ImGui.SameLine();
        if (_uiSharedService.IconTextButton(Dalamud.Interface.FontAwesomeIcon.ArrowsSpin, "恢复默认设置"))
        {
            var defaultPermissions = _apiController.DefaultPermissions!;
            _ownPermissions.SetSticky(Pair.IsDirectlyPaired || defaultPermissions.IndividualIsSticky);
            _ownPermissions.SetPaused(false);
            _ownPermissions.SetDisableVFX(Pair.IsDirectlyPaired ? defaultPermissions.DisableIndividualVFX : defaultPermissions.DisableGroupVFX);
            _ownPermissions.SetDisableSounds(Pair.IsDirectlyPaired ? defaultPermissions.DisableIndividualSounds : defaultPermissions.DisableGroupSounds);
            _ownPermissions.SetDisableAnimations(Pair.IsDirectlyPaired ? defaultPermissions.DisableIndividualAnimations : defaultPermissions.DisableGroupAnimations);
            _ = _apiController.SetBulkPermissions(new(
                new(StringComparer.Ordinal)
                {
                    { Pair.UserData.UID, _ownPermissions }
                },
                new(StringComparer.Ordinal)
            ));
        }
        UiSharedService.AttachToolTip("这将把所有同步设置重置为你Mare设置中的默认同步设置");

        var ySize = ImGui.GetCursorPosY() + style.FramePadding.Y * ImGuiHelpers.GlobalScale + style.FrameBorderSize;
        ImGui.SetWindowSize(new(400, ySize));
    }

    public override void OnClose()
    {
        Mediator.Publish(new RemoveWindowMessage(this));
    }
}
