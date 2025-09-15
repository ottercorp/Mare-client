using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI;
using System.Numerics;

namespace MareSynchronos.UI.Components.Popup;

internal class ReportPopupHandler : IPopupHandler
{
    private readonly ApiController _apiController;
    private readonly UiSharedService _uiSharedService;
    private Pair? _reportedPair;
    private string _reportReason = string.Empty;

    public ReportPopupHandler(ApiController apiController, UiSharedService uiSharedService)
    {
        _apiController = apiController;
        _uiSharedService = uiSharedService;
    }

    public Vector2 PopupSize => new(500, 500);

    public bool ShowClose => true;

    public void DrawContent()
    {
        using (_uiSharedService.UidFont.Push())
            UiSharedService.TextWrapped("举报 " + _reportedPair!.UserData.AliasOrUID);

        ImGui.InputTextMultiline("##reportReason", ref _reportReason, 500, new Vector2(500 - ImGui.GetStyle().ItemSpacing.X * 2, 280));
        UiSharedService.TextWrapped($"注意: 发送举报会使得该用户的档案被标记为 被举报.{Environment.NewLine}" +
            $"举报会被发送给你当前连接到服务器的管理团队.{Environment.NewLine}" +
            $"举报会包含你的UID 和 Discord 用户名.{Environment.NewLine}" +
            $"视严重程度不同, 会处以不同处理.");
        UiSharedService.ColorTextWrapped("滥用举报和虚假举报可能会使你自己的账号被封禁.", ImGuiColors.DalamudRed);
        UiSharedService.ColorTextWrapped("举报可以包含骚扰和不适当的使用Mare, 但必须在Discord附上相关材料. ", ImGuiColors.DalamudYellow);

        using (ImRaii.Disabled(string.IsNullOrEmpty(_reportReason)))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.ExclamationTriangle, "发送举报"))
            {
                ImGui.CloseCurrentPopup();
                var reason = _reportReason;
                _ = _apiController.UserReportProfile(new(_reportedPair.UserData, reason));
            }
        }
    }

    public void Open(OpenReportPopupMessage msg)
    {
        _reportedPair = msg.PairToReport;
        _reportReason = string.Empty;
    }
}