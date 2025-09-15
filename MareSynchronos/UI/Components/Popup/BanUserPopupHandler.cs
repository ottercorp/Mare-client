using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI;
using System.Numerics;

namespace MareSynchronos.UI.Components.Popup;

public class BanUserPopupHandler : IPopupHandler
{
    private readonly ApiController _apiController;
    private readonly UiSharedService _uiSharedService;
    private string _banReason = string.Empty;
    private GroupFullInfoDto _group = null!;
    private Pair _reportedPair = null!;

    public BanUserPopupHandler(ApiController apiController, UiSharedService uiSharedService)
    {
        _apiController = apiController;
        _uiSharedService = uiSharedService;
    }

    public Vector2 PopupSize => new(500, 140);

    public bool ShowClose => true;

    public void DrawContent()
    {
        UiSharedService.TextWrapped("用户 " + (_reportedPair.UserData.AliasOrUID) + " 将被从本同步贝封禁.");
        ImGui.InputTextWithHint("##banreason", "封禁原因", ref _banReason, 255);

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.UserSlash, "封禁用户"))
        {
            ImGui.CloseCurrentPopup();
            var reason = _banReason;
            _ = _apiController.GroupBanUser(new GroupPairDto(_group.Group, _reportedPair.UserData), reason);
            _banReason = string.Empty;
        }
        UiSharedService.TextWrapped("封禁原因将显示在封禁列表中. 个性化UID也将附在其中.");
    }

    public void Open(OpenBanUserPopupMessage message)
    {
        _reportedPair = message.PairToBan;
        _group = message.GroupFullInfoDto;
        _banReason = string.Empty;
    }
}