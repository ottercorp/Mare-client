using FFXIVClientStructs.FFXIV.Client.UI;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Dto;
using MareSynchronos.API.Dto.CharaData;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.API.Dto.User;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.Services.Mediator;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using static FFXIVClientStructs.FFXIV.Client.Game.UI.MapMarkerData.Delegates;

namespace MareSynchronos.WebAPI;

public partial class ApiController
{
    public Task Client_DownloadReady(Guid requestId)
    {
        Logger.LogDebug("Server sent {requestId} ready", requestId);
        Mediator.Publish(new DownloadReadyMessage(requestId));
        return Task.CompletedTask;
    }

    public Task Client_GroupChangePermissions(GroupPermissionDto groupPermission)
    {
        Logger.LogTrace("Client_GroupChangePermissions: {perm}", groupPermission);
        ExecuteSafely(() => _pairManager.SetGroupPermissions(groupPermission));
        return Task.CompletedTask;
    }

    public Task Client_GroupChangeUserPairPermissions(GroupPairUserPermissionDto dto)
    {
        Logger.LogDebug("Client_GroupChangeUserPairPermissions: {dto}", dto);
        ExecuteSafely(() => _pairManager.UpdateGroupPairPermissions(dto));
        return Task.CompletedTask;
    }

    public Task Client_GroupDelete(GroupDto groupDto)
    {
        Logger.LogTrace("Client_GroupDelete: {dto}", groupDto);
        ExecuteSafely(() => _pairManager.RemoveGroup(groupDto.Group));
        return Task.CompletedTask;
    }

    public Task Client_GroupPairChangeUserInfo(GroupPairUserInfoDto userInfo)
    {
        Logger.LogTrace("Client_GroupPairChangeUserInfo: {dto}", userInfo);
        ExecuteSafely(() =>
        {
            if (string.Equals(userInfo.UID, UID, StringComparison.Ordinal)) _pairManager.SetGroupStatusInfo(userInfo);
            else _pairManager.SetGroupPairStatusInfo(userInfo);
        });
        return Task.CompletedTask;
    }

    public Task Client_GroupPairJoined(GroupPairFullInfoDto groupPairInfoDto)
    {
        Logger.LogTrace("Client_GroupPairJoined: {dto}", groupPairInfoDto);
        ExecuteSafely(() => _pairManager.AddGroupPair(groupPairInfoDto));
        return Task.CompletedTask;
    }

    public Task Client_GroupPairLeft(GroupPairDto groupPairDto)
    {
        Logger.LogTrace("Client_GroupPairLeft: {dto}", groupPairDto);
        ExecuteSafely(() => _pairManager.RemoveGroupPair(groupPairDto));
        return Task.CompletedTask;
    }

    public Task Client_GroupSendFullInfo(GroupFullInfoDto groupInfo)
    {
        Logger.LogTrace("Client_GroupSendFullInfo: {dto}", groupInfo);
        ExecuteSafely(() => _pairManager.AddGroup(groupInfo));
        return Task.CompletedTask;
    }

    public Task Client_GroupSendInfo(GroupInfoDto groupInfo)
    {
        Logger.LogTrace("Client_GroupSendInfo: {dto}", groupInfo);
        ExecuteSafely(() => _pairManager.SetGroupInfo(groupInfo));
        return Task.CompletedTask;
    }

    public Task Client_ReceiveServerMessage(MessageSeverity messageSeverity, string message)
    {
        switch (messageSeverity)
        {
            case MessageSeverity.Error:
                Mediator.Publish(new NotificationMessage("Warning from " + _serverManager.CurrentServer!.ServerName, message, NotificationType.Error, TimeSpan.FromSeconds(7.5)));
                break;

            case MessageSeverity.Warning:
                Mediator.Publish(new NotificationMessage("Warning from " + _serverManager.CurrentServer!.ServerName, message, NotificationType.Warning, TimeSpan.FromSeconds(7.5)));
                break;

            case MessageSeverity.Information:
                if (_doNotNotifyOnNextInfo)
                {
                    _doNotNotifyOnNextInfo = false;
                    break;
                }
                Mediator.Publish(new NotificationMessage("Info from " + _serverManager.CurrentServer!.ServerName, message, NotificationType.Info, TimeSpan.FromSeconds(5)));
                break;
        }

        unsafe
        {
            var warning = messageSeverity != MessageSeverity.Information;
            if (!message.Contains("Welcome"))
            {
                var text = $"{(warning ? "Mare警告: " : "Mare通知: ")} {message}";
                RaptureAtkModule.Instance()->ShowTextGimmickHint(
                    text,
                    warning ? RaptureAtkModule.TextGimmickHintStyle.Warning : RaptureAtkModule.TextGimmickHintStyle.Info,
                    50);
            }
        }


        return Task.CompletedTask;
    }

    public Task Client_UpdateSystemInfo(SystemInfoDto systemInfo)
    {
        SystemInfoDto = systemInfo;
        return Task.CompletedTask;
    }

    public Task Client_UpdateUserIndividualPairStatusDto(UserIndividualPairStatusDto dto)
    {
        Logger.LogDebug("Client_UpdateUserIndividualPairStatusDto: {dto}", dto);
        ExecuteSafely(() => _pairManager.UpdateIndividualPairStatus(dto));
        return Task.CompletedTask;
    }

    public Task Client_UserAddClientPair(UserPairDto dto)
    {
        Logger.LogDebug("Client_UserAddClientPair: {dto}", dto);
        ExecuteSafely(() => _pairManager.AddUserPair(dto, addToLastAddedUser: true));
        return Task.CompletedTask;
    }

    public Task Client_UserReceiveCharacterData(OnlineUserCharaDataDto dataDto)
    {
        Logger.LogTrace("Client_UserReceiveCharacterData: {user}", dataDto.User);
        ExecuteSafely(() => _pairManager.ReceiveCharaData(dataDto));
        return Task.CompletedTask;
    }

    public Task Client_UserReceiveUploadStatus(UserDto dto)
    {
        Logger.LogTrace("Client_UserReceiveUploadStatus: {dto}", dto);
        ExecuteSafely(() => _pairManager.ReceiveUploadStatus(dto));
        return Task.CompletedTask;
    }

    public Task Client_UserRemoveClientPair(UserDto dto)
    {
        Logger.LogDebug("Client_UserRemoveClientPair: {dto}", dto);
        ExecuteSafely(() => _pairManager.RemoveUserPair(dto));
        return Task.CompletedTask;
    }

    public Task Client_UserSendOffline(UserDto dto)
    {
        Logger.LogDebug("Client_UserSendOffline: {dto}", dto);
        ExecuteSafely(() => _pairManager.MarkPairOffline(dto.User));
        return Task.CompletedTask;
    }

    public Task Client_UserSendOnline(OnlineUserIdentDto dto)
    {
        Logger.LogDebug("Client_UserSendOnline: {dto}", dto);
        ExecuteSafely(() => _pairManager.MarkPairOnline(dto));
        return Task.CompletedTask;
    }

    public Task Client_UserUpdateDefaultPermissions(DefaultPermissionsDto dto)
    {
        Logger.LogDebug("Client_UserUpdateDefaultPermissions: {dto}", dto);
        _connectionDto!.DefaultPreferredPermissions = dto;
        return Task.CompletedTask;
    }

    public Task Client_UserUpdateOtherPairPermissions(UserPermissionsDto dto)
    {
        Logger.LogDebug("Client_UserUpdateOtherPairPermissions: {dto}", dto);
        ExecuteSafely(() => _pairManager.UpdatePairPermissions(dto));
        return Task.CompletedTask;
    }

    public Task Client_UserUpdateProfile(UserDto dto)
    {
        Logger.LogDebug("Client_UserUpdateProfile: {dto}", dto);
        ExecuteSafely(() => Mediator.Publish(new ClearProfileDataMessage(dto.User)));
        return Task.CompletedTask;
    }

    public Task Client_UserUpdateSelfPairPermissions(UserPermissionsDto dto)
    {
        Logger.LogDebug("Client_UserUpdateSelfPairPermissions: {dto}", dto);
        ExecuteSafely(() => _pairManager.UpdateSelfPairPermissions(dto));
        return Task.CompletedTask;
    }

    public Task Client_GposeLobbyJoin(UserData userData)
    {
        Logger.LogDebug("Client_GposeLobbyJoin: {dto}", userData);
        ExecuteSafely(() => Mediator.Publish(new GposeLobbyUserJoin(userData)));
        return Task.CompletedTask;
    }

    public Task Client_GposeLobbyLeave(UserData userData)
    {
        Logger.LogDebug("Client_GposeLobbyLeave: {dto}", userData);
        ExecuteSafely(() => Mediator.Publish(new GPoseLobbyUserLeave(userData)));
        return Task.CompletedTask;
    }

    public Task Client_GposeLobbyPushCharacterData(CharaDataDownloadDto charaDownloadDto)
    {
        Logger.LogDebug("Client_GposeLobbyPushCharacterData: {dto}", charaDownloadDto.Uploader);
        ExecuteSafely(() => Mediator.Publish(new GPoseLobbyReceiveCharaData(charaDownloadDto)));
        return Task.CompletedTask;
    }

    public Task Client_GposeLobbyPushPoseData(UserData userData, PoseData poseData)
    {
        Logger.LogDebug("Client_GposeLobbyPushPoseData: {dto}", userData);
        ExecuteSafely(() => Mediator.Publish(new GPoseLobbyReceivePoseData(userData, poseData)));
        return Task.CompletedTask;
    }

    public Task Client_GposeLobbyPushWorldData(UserData userData, WorldData worldData)
    {
        //Logger.LogDebug("Client_GposeLobbyPushWorldData: {dto}", userData);
        ExecuteSafely(() => Mediator.Publish(new GPoseLobbyReceiveWorldData(userData, worldData)));
        return Task.CompletedTask;
    }

    public Task Client_UpdateSupporterList(SupporterDto supporterDto)
    {
        Logger.LogDebug("Client_UpdateSupporterList: {dto}", supporterDto);
        ExecuteSafely(() => Mediator.Publish(new UpdateSupportersMessage(supporterDto)));
        return Task.CompletedTask;
    }

    public Task Client_GroupChat(GroupChatDto groupChatDto)
    {
        ExecuteSafely(() =>
        {
            // Mare 内置聊天 UI
            Mediator.Publish(new ChatMessage(groupChatDto.Group, groupChatDto.User, groupChatDto.Message, groupChatDto.Time));

            // 若 ChatTwo 可用，推送为 MareLinkshell[i]
            try
            {
                var sender = string.Equals(groupChatDto.User.UID, UID, StringComparison.Ordinal)
                    ? DisplayName
                    : _pairManager.GetPairByUID(groupChatDto.User.UID)?.UserData.AliasOrUID ?? groupChatDto.User.AliasOrUID;

                _ipcManager.ChatTwo.PushGroupChatMessage(groupChatDto.GID, sender, groupChatDto.Message, groupChatDto.Time, _mareConfigService);
            }
            catch
            {
                // 忽略 ChatTwo 不存在的情况
            }
        });
        return Task.CompletedTask;
    }

    public Task Client_SendLocationToClient(LocationDto locationDto)
    {
        Logger.LogDebug($"{nameof(Client_SendLocationToClient)}: {locationDto.user}");
        ExecuteSafely(() => Mediator.Publish(new LocationMeaasge(locationDto.user.UID, locationDto.location)));
        return Task.CompletedTask;
    }

    public Task Client_MoodlesShare(MoodlesDto moodlesDto)
    {
        switch (moodlesDto.Action)
        {
            case MoodlesAction.Download:
                _ = _ipcManager.Moodles.SendMoodlesToPlugin(moodlesDto.Statuses, UID);
                break;
        }
        Logger.LogDebug("Received MoodlesDto from server: {action}}", moodlesDto.Action);
        return Task.CompletedTask;
    }

    public void OnDownloadReady(Action<Guid> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_DownloadReady), act);
    }

    public void OnGroupChangePermissions(Action<GroupPermissionDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupChangePermissions), act);
    }

    public void OnGroupChangeUserPairPermissions(Action<GroupPairUserPermissionDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupChangeUserPairPermissions), act);
    }

    public void OnGroupDelete(Action<GroupDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupDelete), act);
    }

    public void OnGroupPairChangeUserInfo(Action<GroupPairUserInfoDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupPairChangeUserInfo), act);
    }

    public void OnGroupPairJoined(Action<GroupPairFullInfoDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupPairJoined), act);
    }

    public void OnGroupPairLeft(Action<GroupPairDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupPairLeft), act);
    }

    public void OnGroupSendFullInfo(Action<GroupFullInfoDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupSendFullInfo), act);
    }

    public void OnGroupSendInfo(Action<GroupInfoDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupSendInfo), act);
    }

    public void OnReceiveServerMessage(Action<MessageSeverity, string> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_ReceiveServerMessage), act);
    }

    public void OnUpdateSystemInfo(Action<SystemInfoDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UpdateSystemInfo), act);
    }

    public void OnUpdateUserIndividualPairStatusDto(Action<UserIndividualPairStatusDto> action)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UpdateUserIndividualPairStatusDto), action);
    }

    public void OnUserAddClientPair(Action<UserPairDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserAddClientPair), act);
    }

    public void OnUserDefaultPermissionUpdate(Action<DefaultPermissionsDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserUpdateDefaultPermissions), act);
    }

    public void OnUserReceiveCharacterData(Action<OnlineUserCharaDataDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserReceiveCharacterData), act);
    }

    public void OnUserReceiveUploadStatus(Action<UserDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserReceiveUploadStatus), act);
    }

    public void OnUserRemoveClientPair(Action<UserDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserRemoveClientPair), act);
    }

    public void OnUserSendOffline(Action<UserDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserSendOffline), act);
    }

    public void OnUserSendOnline(Action<OnlineUserIdentDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserSendOnline), act);
    }

    public void OnUserUpdateOtherPairPermissions(Action<UserPermissionsDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserUpdateOtherPairPermissions), act);
    }

    public void OnUserUpdateProfile(Action<UserDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserUpdateProfile), act);
    }

    public void OnUserUpdateSelfPairPermissions(Action<UserPermissionsDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserUpdateSelfPairPermissions), act);
    }

    public void OnUserApplyMoodlesByStatus(Action<ApplyMoodlesByStatusDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserApplyMoodlesByStatus), act);
    }
    
    public void OnGposeLobbyJoin(Action<UserData> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GposeLobbyJoin), act);
    }

    public void OnGposeLobbyLeave(Action<UserData> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GposeLobbyLeave), act);
    }

    public void OnGposeLobbyPushCharacterData(Action<CharaDataDownloadDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GposeLobbyPushCharacterData), act);
    }

    public void OnGposeLobbyPushPoseData(Action<UserData, PoseData> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GposeLobbyPushPoseData), act);
    }

    public void OnGposeLobbyPushWorldData(Action<UserData, WorldData> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GposeLobbyPushWorldData), act);
    }

    public void OnSupporterUpdate(Action<SupporterDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UpdateSupporterList), act);
    }

    public void OnReceiveGroupChat(Action<GroupChatDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupChat), act);
    }

    public void OnMoodlesShare(Action<MoodlesDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_MoodlesShare), act);
    }

    public void OnReciveLocation(Action<LocationDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_SendLocationToClient), act);
    }

    private void ExecuteSafely(Action act)
    {
        try
        {
            act();
        }
        catch (Exception ex)
        {
            Logger.LogCritical(ex, "Error on executing safely");
        }
    }
}