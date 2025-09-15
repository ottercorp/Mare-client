using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Utility;
using Dalamud.Interface;
using MareSynchronos.API.Dto.CharaData;
using MareSynchronos.Services.CharaData.Models;
using System.Numerics;

namespace MareSynchronos.UI;

internal sealed partial class CharaDataHubUi
{
    private string _createDescFilter = string.Empty;
    private string _createCodeFilter = string.Empty;
    private bool _createOnlyShowFav = false;
    private bool _createOnlyShowNotDownloadable = false;

    private void DrawEditCharaData(CharaDataFullExtendedDto? dataDto)
    {
        using var imguiid = ImRaii.PushId(dataDto?.Id ?? "无数据");

        if (dataDto == null)
        {
            ImGuiHelpers.ScaledDummy(5);
            UiSharedService.DrawGroupedCenteredColorText("选择条目以编辑.", ImGuiColors.DalamudYellow);
            return;
        }

        var updateDto = _charaDataManager.GetUpdateDto(dataDto.Id);

        if (updateDto == null)
        {
            UiSharedService.DrawGroupedCenteredColorText("更新DTO时发生了错误. 请点击上方按钮更新角色数据.", ImGuiColors.DalamudYellow);
            return;
        }

        int otherUpdates = 0;
        foreach (var item in _charaDataManager.OwnCharaData.Values.Where(v => !string.Equals(v.Id, dataDto.Id, StringComparison.Ordinal)))
        {
            if (_charaDataManager.GetUpdateDto(item.Id)?.HasChanges ?? false)
            {
                otherUpdates++;
            }
        }

        bool canUpdate = updateDto.HasChanges;
        if (canUpdate || otherUpdates > 0 || (!_charaDataManager.CharaUpdateTask?.IsCompleted ?? false))
        {
            ImGuiHelpers.ScaledDummy(5);
        }

        var indent = ImRaii.PushIndent(10f);
        if (canUpdate || _charaDataManager.UploadTask != null)
        {
            ImGuiHelpers.ScaledDummy(5);
            UiSharedService.DrawGrouped(() =>
            {
                if (canUpdate)
                {
                    ImGui.AlignTextToFramePadding();
                    UiSharedService.ColorTextWrapped("警告: 有未保存的变更!", ImGuiColors.DalamudRed);
                    ImGui.SameLine();
                    using (ImRaii.Disabled(_charaDataManager.CharaUpdateTask != null && !_charaDataManager.CharaUpdateTask.IsCompleted))
                    {
                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowCircleUp, "保存到服务器"))
                        {
                            _charaDataManager.UploadCharaData(dataDto.Id);
                        }
                        ImGui.SameLine();
                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Undo, "撤销变更"))
                        {
                            updateDto.UndoChanges();
                        }
                    }
                    if (_charaDataManager.CharaUpdateTask != null && !_charaDataManager.CharaUpdateTask.IsCompleted)
                    {
                        UiSharedService.ColorTextWrapped("正在上传数据, 请稍后.", ImGuiColors.DalamudYellow);
                    }
                }

                if (!_charaDataManager.UploadTask?.IsCompleted ?? false)
                {
                    DisableDisabled(() =>
                    {
                        if (_charaDataManager.UploadProgress != null)
                        {
                            UiSharedService.ColorTextWrapped(_charaDataManager.UploadProgress.Value ?? string.Empty, ImGuiColors.DalamudYellow);
                        }
                        if ((!_charaDataManager.UploadTask?.IsCompleted ?? false) && _uiSharedService.IconTextButton(FontAwesomeIcon.Ban, "取消上传"))
                        {
                            _charaDataManager.CancelUpload();
                        }
                        else if (_charaDataManager.UploadTask?.IsCompleted ?? false)
                        {
                            var color = UiSharedService.GetBoolColor(_charaDataManager.UploadTask.Result.Success);
                            UiSharedService.ColorTextWrapped(_charaDataManager.UploadTask.Result.Output, color);
                        }
                    });
                }
                else if (_charaDataManager.UploadTask?.IsCompleted ?? false)
                {
                    var color = UiSharedService.GetBoolColor(_charaDataManager.UploadTask.Result.Success);
                    UiSharedService.ColorTextWrapped(_charaDataManager.UploadTask.Result.Output, color);
                }
            });
        }

        if (otherUpdates > 0)
        {
            ImGuiHelpers.ScaledDummy(5);
            UiSharedService.DrawGrouped(() =>
            {
                ImGui.AlignTextToFramePadding();
                UiSharedService.ColorTextWrapped($"You have {otherUpdates} other entries with unsaved changes.", ImGuiColors.DalamudYellow);
                ImGui.SameLine();
                using (ImRaii.Disabled(_charaDataManager.CharaUpdateTask != null && !_charaDataManager.CharaUpdateTask.IsCompleted))
                {
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowAltCircleUp, "Save all to server"))
                    {
                        _charaDataManager.UploadAllCharaData();
                    }
                }
            });
        }
        indent.Dispose();

        if (canUpdate || otherUpdates > 0 || (!_charaDataManager.CharaUpdateTask?.IsCompleted ?? false))
        {
            ImGuiHelpers.ScaledDummy(5);
        }

        using var child = ImRaii.Child("editChild", new(0, 0), false, ImGuiWindowFlags.AlwaysAutoResize);

        DrawEditCharaDataGeneral(dataDto, updateDto);
        ImGuiHelpers.ScaledDummy(5);
        DrawEditCharaDataAccessAndSharing(updateDto);
        ImGuiHelpers.ScaledDummy(5);
        DrawEditCharaDataAppearance(dataDto, updateDto);
        ImGuiHelpers.ScaledDummy(5);
        DrawEditCharaDataPoses(updateDto);
    }

    private void DrawEditCharaDataAccessAndSharing(CharaDataExtendedUpdateDto updateDto)
    {
        _uiSharedService.BigText("访问权限设置");

        UiSharedService.ScaledNextItemWidth(200);
        var dtoAccessType = updateDto.AccessType;
        if (ImGui.BeginCombo("访问权限", GetAccessTypeString(dtoAccessType)))
        {
            foreach (var accessType in Enum.GetValues(typeof(AccessTypeDto)).Cast<AccessTypeDto>())
            {
                if (ImGui.Selectable(GetAccessTypeString(accessType), accessType == dtoAccessType))
                {
                    updateDto.AccessType = accessType;
                }
            }

            ImGui.EndCombo();
        }
        _uiSharedService.DrawHelpText("你可以对访问权限进行设置." + UiSharedService.TooltipSeparator
            + "特定: 仅在 '特定角色 / 同步贝' 中设置的角色和同步贝用户可以访问本数据" + Environment.NewLine
            + "直接配对: 仅与你直接配对的用户可以访问本数据" + Environment.NewLine
            + "所有配对: 所有和你处于配对状态的用户可以访问本数据" + Environment.NewLine
            + "所有人: 所有人都可以可以访问本数据" + UiSharedService.TooltipSeparator
            + "注意: 要访问非 '共享' 状态的数据, 需要拥有对应代码." + Environment.NewLine
            + "注意: 在 '直接配对' 和 '所有配对' 状态下, 暂停配对的用户无法访问数据." + Environment.NewLine
            + "注意: '特定角色 / 同步贝' 中设置的角色和同步贝用户可以访问本数据, 无论你是否暂停了配对.");

        DrawSpecific(updateDto);

        UiSharedService.ScaledNextItemWidth(200);
        var dtoShareType = updateDto.ShareType;
        if (ImGui.BeginCombo("共享类型", GetShareTypeString(dtoShareType)))
        {
            foreach (var shareType in Enum.GetValues(typeof(ShareTypeDto)).Cast<ShareTypeDto>())
            {
                if (ImGui.Selectable(GetShareTypeString(shareType), shareType == dtoShareType))
                {
                    updateDto.ShareType = shareType;
                }
            }

            ImGui.EndCombo();
        }
        _uiSharedService.DrawHelpText("你想如何分享你的数据." + UiSharedService.TooltipSeparator
            + "仅代码: 仅拥有对应代码的用户可以访问数据" + Environment.NewLine
            + "共享: 满足 '访问权限' 设置的用户可以在 '与你共享' 标签中访问数据 (也可以通过代码访问)" + UiSharedService.TooltipSeparator
            + "注意: 将权限设置为 '所有人' 和将权限设为 '所有配对' 效果基本相同, 但只有与你配对的用户才能访问.");

        ImGuiHelpers.ScaledDummy(10f);
    }

    private void DrawEditCharaDataAppearance(CharaDataFullExtendedDto dataDto, CharaDataExtendedUpdateDto updateDto)
    {
        _uiSharedService.BigText("外貌");

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowRight, "设置为当前外貌"))
        {
            _charaDataManager.SetAppearanceData(dataDto.Id);
        }
        _uiSharedService.DrawHelpText("这将使用你当前的外貌数据覆盖已保存的数据.");
        ImGui.SameLine();
        using (ImRaii.Disabled(dataDto.HasMissingFiles || !updateDto.IsAppearanceEqual || _charaDataManager.DataApplicationTask != null))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.CheckCircle, "预览已保存的外貌数据"))
            {
                _charaDataManager.ApplyDataToSelf(dataDto);
            }
        }
        _uiSharedService.DrawHelpText("将下载并在你的角色应用外貌数据. 将在15秒后恢复到原状态." + UiSharedService.TooltipSeparator
            + "注意: 职业不同的情况下无法佩戴对应武器.");

        ImGui.TextUnformatted("包含Glamourer数据");
        ImGui.SameLine();
        bool hasGlamourerdata = !string.IsNullOrEmpty(updateDto.GlamourerData);
        UiSharedService.ScaledSameLine(200);
        _uiSharedService.BooleanToColoredIcon(hasGlamourerdata, false);

        ImGui.TextUnformatted("包含文件");
        var hasFiles = (updateDto.FileGamePaths ?? []).Any() || (dataDto.OriginalFiles.Any());
        UiSharedService.ScaledSameLine(200);
        _uiSharedService.BooleanToColoredIcon(hasFiles, false);
        if (hasFiles && updateDto.IsAppearanceEqual)
        {
            ImGui.SameLine();
            ImGuiHelpers.ScaledDummy(20, 1);
            ImGui.SameLine();
            var pos = ImGui.GetCursorPosX();
            ImGui.NewLine();
            ImGui.SameLine(pos);
            ImGui.TextUnformatted($"{dataDto.FileGamePaths.DistinctBy(k => k.HashOrFileSwap).Count()} 文件hash (原始上传: {dataDto.OriginalFiles.DistinctBy(k => k.HashOrFileSwap).Count()} 文件hash)");
            ImGui.NewLine();
            ImGui.SameLine(pos);
            ImGui.TextUnformatted($"{dataDto.FileGamePaths.Count} 相关路径");
            ImGui.NewLine();
            ImGui.SameLine(pos);
            ImGui.TextUnformatted($"{dataDto.FileSwaps!.Count} 文件替换");
            ImGui.NewLine();
            ImGui.SameLine(pos);
            if (!dataDto.HasMissingFiles)
            {
                UiSharedService.ColorTextWrapped("所有文件均存在", ImGuiColors.HealerGreen);
            }
            else
            {
                UiSharedService.ColorTextWrapped($"服务器缺少 {dataDto.MissingFiles.DistinctBy(k => k.HashOrFileSwap).Count()} 个文件数据", ImGuiColors.DalamudRed);
                ImGui.NewLine();
                ImGui.SameLine(pos);
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowCircleUp, "上传缺失文件以修复数据"))
                {
                    _charaDataManager.UploadMissingFiles(dataDto.Id);
                }
            }
        }
        else if (hasFiles && !updateDto.IsAppearanceEqual)
        {
            ImGui.SameLine();
            ImGuiHelpers.ScaledDummy(20, 1);
            ImGui.SameLine();
            UiSharedService.ColorTextWrapped("新数据已设置. 可能包含需要上传的文件 (将在保存时上传)", ImGuiColors.DalamudYellow);
        }

        ImGui.TextUnformatted("包含 Manipulation 数据");
        bool hasManipData = !string.IsNullOrEmpty(updateDto.ManipulationData);
        UiSharedService.ScaledSameLine(200);
        _uiSharedService.BooleanToColoredIcon(hasManipData, false);

        ImGui.TextUnformatted("包含 Customize+ 数据");
        ImGui.SameLine();
        bool hasCustomizeData = !string.IsNullOrEmpty(updateDto.CustomizeData);
        UiSharedService.ScaledSameLine(200);
        _uiSharedService.BooleanToColoredIcon(hasCustomizeData, false);

        // ImGui.TextUnformatted("包含 Moodles 数据");
        // bool hasMoodlesData = !string.IsNullOrEmpty(updateDto.MoodlesData);
        // ImGui.SameLine(200);
        // _uiSharedService.BooleanToColoredIcon(hasMoodlesData, false);
    }

    private void DrawEditCharaDataGeneral(CharaDataFullExtendedDto dataDto, CharaDataExtendedUpdateDto updateDto)
    {
        _uiSharedService.BigText("通用");
        string code = dataDto.FullId;
        using (ImRaii.Disabled())
        {
            UiSharedService.ScaledNextItemWidth(200);
            ImGui.InputText("##CharaDataCode", ref code, 255, ImGuiInputTextFlags.ReadOnly);
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("角色数据代码");
        ImGui.SameLine();
        if (_uiSharedService.IconButton(FontAwesomeIcon.Copy))
        {
            ImGui.SetClipboardText(code);
        }
        UiSharedService.AttachToolTip("复制到剪切板");

        string creationTime = dataDto.CreatedDate.ToLocalTime().ToString();
        string updateTime = dataDto.UpdatedDate.ToLocalTime().ToString();
        string downloadCount = dataDto.DownloadCount.ToString();
        using (ImRaii.Disabled())
        {
            UiSharedService.ScaledNextItemWidth(200);
            ImGui.InputText("##CreationDate", ref creationTime, 255, ImGuiInputTextFlags.ReadOnly);
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("新建数据");
        ImGui.SameLine();
        ImGuiHelpers.ScaledDummy(20);
        ImGui.SameLine();
        using (ImRaii.Disabled())
        {
            UiSharedService.ScaledNextItemWidth(200);
            ImGui.InputText("##LastUpdate", ref updateTime, 255, ImGuiInputTextFlags.ReadOnly);
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("最后更新于");
        ImGui.SameLine();
        ImGuiHelpers.ScaledDummy(23);
        ImGui.SameLine();
        using (ImRaii.Disabled())
        {
            UiSharedService.ScaledNextItemWidth(50);
            ImGui.InputText("##DlCount", ref downloadCount, 255, ImGuiInputTextFlags.ReadOnly);
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("下载量");

        string description = updateDto.Description;
        UiSharedService.ScaledNextItemWidth(735);
        if (ImGui.InputText("##Description", ref description, 200))
        {
            updateDto.Description = description;
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("描述");
        _uiSharedService.DrawHelpText("MCD描述." + UiSharedService.TooltipSeparator
            + "注意: 所有拥有访问权限的用户都将可以看到本描述. 查看 '访问权限' 和 '共享' 部分.");

        var expiryDate = updateDto.ExpiryDate;
        bool isExpiring = expiryDate != DateTime.MaxValue;
        if (ImGui.Checkbox("过期", ref isExpiring))
        {
            updateDto.SetExpiry(isExpiring);
        }
        _uiSharedService.DrawHelpText("如果启用, 将自动于设定的日期删除对应数据.");
        using (ImRaii.Disabled(!isExpiring))
        {
            ImGui.SameLine();
            UiSharedService.ScaledNextItemWidth(100);
            if (ImGui.BeginCombo("年", expiryDate.Year.ToString()))
            {
                for (int year = DateTime.UtcNow.Year; year < DateTime.UtcNow.Year + 4; year++)
                {
                    if (ImGui.Selectable(year.ToString(), year == expiryDate.Year))
                    {
                        updateDto.SetExpiry(year, expiryDate.Month, expiryDate.Day);
                    }
                }
                ImGui.EndCombo();
            }
            ImGui.SameLine();

            int daysInMonth = DateTime.DaysInMonth(expiryDate.Year, expiryDate.Month);
            UiSharedService.ScaledNextItemWidth(100);
            if (ImGui.BeginCombo("月", expiryDate.Month.ToString()))
            {
                for (int month = 1; month <= 12; month++)
                {
                    if (ImGui.Selectable(month.ToString(), month == expiryDate.Month))
                    {
                        updateDto.SetExpiry(expiryDate.Year, month, expiryDate.Day);
                    }
                }
                ImGui.EndCombo();
            }
            ImGui.SameLine();

            UiSharedService.ScaledNextItemWidth(100);
            if (ImGui.BeginCombo("日", expiryDate.Day.ToString()))
            {
                for (int day = 1; day <= daysInMonth; day++)
                {
                    if (ImGui.Selectable(day.ToString(), day == expiryDate.Day))
                    {
                        updateDto.SetExpiry(expiryDate.Year, expiryDate.Month, day);
                    }
                }
                ImGui.EndCombo();
            }
        }
        ImGuiHelpers.ScaledDummy(5);

        using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "删除角色数据"))
            {
                _ = _charaDataManager.DeleteCharaData(dataDto);
                SelectedDtoId = string.Empty;
            }
        }
        if (!UiSharedService.CtrlPressed())
        {
            UiSharedService.AttachToolTip("按住CTRL并点击以删除. 无法撤销该操作.");
        }
    }

    private void DrawEditCharaDataPoses(CharaDataExtendedUpdateDto updateDto)
    {
        _uiSharedService.BigText("姿势");
        var poseCount = updateDto.PoseList.Count();
        using (ImRaii.Disabled(poseCount >= maxPoses))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "添加新姿势"))
            {
                updateDto.AddPose();
            }
        }
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow, poseCount == maxPoses))
            ImGui.TextUnformatted($"{poseCount}/{maxPoses} 姿势已添加");
        ImGuiHelpers.ScaledDummy(5);

        using var indent = ImRaii.PushIndent(10f);
        int poseNumber = 1;

        if (!_uiSharedService.IsInGpose && _charaDataManager.BrioAvailable)
        {
            ImGuiHelpers.ScaledDummy(5);
            UiSharedService.DrawGroupedCenteredColorText("请先进入Gpose.", ImGuiColors.DalamudYellow);
            ImGuiHelpers.ScaledDummy(5);
        }
        else if (!_charaDataManager.BrioAvailable)
        {
            ImGuiHelpers.ScaledDummy(5);
            UiSharedService.DrawGroupedCenteredColorText("请先安装Brio.", ImGuiColors.DalamudRed);
            ImGuiHelpers.ScaledDummy(5);
        }

        foreach (var pose in updateDto.PoseList)
        {
            ImGui.AlignTextToFramePadding();
            using var id = ImRaii.PushId("pose" + poseNumber);
            ImGui.TextUnformatted(poseNumber.ToString());

            if (pose.Id == null)
            {
                UiSharedService.ScaledSameLine(50);
                _uiSharedService.IconText(FontAwesomeIcon.Plus, ImGuiColors.DalamudYellow);
                UiSharedService.AttachToolTip("姿势还未保存到服务器. 保存后将进行上传.");
            }

            bool poseHasChanges = updateDto.PoseHasChanges(pose);
            if (poseHasChanges)
            {
                UiSharedService.ScaledSameLine(50);
                _uiSharedService.IconText(FontAwesomeIcon.ExclamationTriangle, ImGuiColors.DalamudYellow);
                UiSharedService.AttachToolTip("姿势变更还未保存到服务器.");
            }

            UiSharedService.ScaledSameLine(75);
            if (pose.Description == null && pose.WorldData == null && pose.PoseData == null)
            {
                UiSharedService.ColorText("计划删除姿势", ImGuiColors.DalamudYellow);
            }
            else
            {
                var desc = pose.Description;
                if (ImGui.InputTextWithHint("##description", "描述", ref desc, 100))
                {
                    pose.Description = desc;
                    updateDto.UpdatePoseList();
                }
                ImGui.SameLine();
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "删除"))
                {
                    updateDto.RemovePose(pose);
                }

                ImGui.SameLine();
                ImGuiHelpers.ScaledDummy(10, 1);
                ImGui.SameLine();
                bool hasPoseData = !string.IsNullOrEmpty(pose.PoseData);
                _uiSharedService.IconText(FontAwesomeIcon.Running, UiSharedService.GetBoolColor(hasPoseData));
                UiSharedService.AttachToolTip(hasPoseData
                    ? "本条目包含姿势数据"
                    : "本条目未包含姿势数据");
                ImGui.SameLine();

                using (ImRaii.Disabled(!_uiSharedService.IsInGpose || !(_charaDataManager.AttachingPoseTask?.IsCompleted ?? true) || !_charaDataManager.BrioAvailable))
                {
                    using var poseid = ImRaii.PushId("poseSet" + poseNumber);
                    if (_uiSharedService.IconButton(FontAwesomeIcon.Plus))
                    {
                        _charaDataManager.AttachPoseData(pose, updateDto);
                    }
                    UiSharedService.AttachToolTip("应用当前姿势到数据");
                }
                ImGui.SameLine();
                using (ImRaii.Disabled(!hasPoseData))
                {
                    using var poseid = ImRaii.PushId("poseDelete" + poseNumber);
                    if (_uiSharedService.IconButton(FontAwesomeIcon.Trash))
                    {
                        pose.PoseData = string.Empty;
                        updateDto.UpdatePoseList();
                    }
                    UiSharedService.AttachToolTip("Delete current pose data from pose");
                }

                ImGui.SameLine();
                ImGuiHelpers.ScaledDummy(10, 1);
                ImGui.SameLine();
                var worldData = pose.WorldData;
                bool hasWorldData = (worldData ?? default) != default;
                _uiSharedService.IconText(FontAwesomeIcon.Globe, UiSharedService.GetBoolColor(hasWorldData));
                var tooltipText = !hasWorldData ? "姿势中未包含位置数据." : "姿势中包含了位置数据.";
                if (hasWorldData)
                {
                    tooltipText += UiSharedService.TooltipSeparator + "点击以在地图显示";
                }
                UiSharedService.AttachToolTip(tooltipText);
                if (hasWorldData && ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    _dalamudUtilService.SetMarkerAndOpenMap(position: new Vector3(worldData.Value.PositionX, worldData.Value.PositionY, worldData.Value.PositionZ),
                        _dalamudUtilService.MapData.Value[worldData.Value.LocationInfo.MapId].Map);
                }
                ImGui.SameLine();
                using (ImRaii.Disabled(!_uiSharedService.IsInGpose || !(_charaDataManager.AttachingPoseTask?.IsCompleted ?? true) || !_charaDataManager.BrioAvailable))
                {
                    using var worldId = ImRaii.PushId("worldSet" + poseNumber);
                    if (_uiSharedService.IconButton(FontAwesomeIcon.Plus))
                    {
                        _charaDataManager.AttachWorldData(pose, updateDto);
                    }
                    UiSharedService.AttachToolTip("应用当前位置信息到姿势");
                }
                ImGui.SameLine();
                using (ImRaii.Disabled(!hasWorldData))
                {
                    using var worldId = ImRaii.PushId("worldDelete" + poseNumber);
                    if (_uiSharedService.IconButton(FontAwesomeIcon.Trash))
                    {
                        pose.WorldData = default(WorldData);
                        updateDto.UpdatePoseList();
                    }
                    UiSharedService.AttachToolTip("Delete current world position data from pose");
                }
            }

            if (poseHasChanges)
            {
                ImGui.SameLine();
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Undo, "撤销"))
                {
                    updateDto.RevertDeletion(pose);
                }
            }

            poseNumber++;
        }
    }

    private void DrawMcdOnline()
    {
        _uiSharedService.BigText("在线MCD");

        DrawHelpFoldout("在此选项卡中，您可以创建、查看和编辑存储在服务器上的Mare角色数据。" + Environment.NewLine + Environment.NewLine
            + "Mare在线角色数据的功能类似于之前用于导出角色的MCDF标准，不同之处在于您不必向其他人发送文件，而只需提供一个代码。" + Environment.NewLine + Environment.NewLine
            + "这里要解释的内容太多，无法完整说明您在此处可以做的所有事情，但是此选项卡中的所有元素都附有帮助文本，说明它们的用途。请仔细查看。" + Environment.NewLine + Environment.NewLine
            + "请注意，当您与其他人分享角色数据时，借助未经授权的第三方插件，您的外观可能会被不可逆地盗用，就像使用MCDF时一样。");

        ImGuiHelpers.ScaledDummy(5);
        using (ImRaii.Disabled((!_charaDataManager.GetAllDataTask?.IsCompleted ?? false)
            || (_charaDataManager.DataGetTimeoutTask != null && !_charaDataManager.DataGetTimeoutTask.IsCompleted)))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowCircleDown, "请求你的MCD数据"))
            {
                _ = _charaDataManager.GetAllData(_disposalCts.Token);
            }
        }
        if (_charaDataManager.DataGetTimeoutTask != null && !_charaDataManager.DataGetTimeoutTask.IsCompleted)
        {
            UiSharedService.AttachToolTip("每分钟仅能进行一次请求. 请稍后.");
        }

        using (var table = ImRaii.Table("拥有的角色数据", 12, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.ScrollY,
            new Vector2(ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X, 140 * ImGuiHelpers.GlobalScale)))
        {
            if (table)
            {
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 18 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 18 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("代码");
                ImGui.TableSetupColumn("描述", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("创建于");
                ImGui.TableSetupColumn("更新于");
                ImGui.TableSetupColumn("下载量", ImGuiTableColumnFlags.WidthFixed, 18 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("可下载", ImGuiTableColumnFlags.WidthFixed, 18 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("文件", ImGuiTableColumnFlags.WidthFixed, 32 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("Glamourer", ImGuiTableColumnFlags.WidthFixed, 18 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("Customize+", ImGuiTableColumnFlags.WidthFixed, 18 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("过期", ImGuiTableColumnFlags.WidthFixed, 18 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupScrollFreeze(0, 2);
                ImGui.TableHeadersRow();

                ImGui.TableNextColumn();
                ImGui.Dummy(new(0, 0));
                ImGui.TableNextColumn();
                ImGui.Checkbox("###createOnlyShowfav", ref _createOnlyShowFav);
                UiSharedService.AttachToolTip("Filter by favorites");
                ImGui.TableNextColumn();
                var x1 = ImGui.GetContentRegionAvail().X;
                ImGui.SetNextItemWidth(x1);
                ImGui.InputTextWithHint("###createFilterCode", "Filter by code", ref _createCodeFilter, 200);
                ImGui.TableNextColumn();
                var x2 = ImGui.GetContentRegionAvail().X;
                ImGui.SetNextItemWidth(x2);
                ImGui.InputTextWithHint("###createFilterDesc", "Filter by description", ref _createDescFilter, 200);
                ImGui.TableNextColumn();
                ImGui.Dummy(new(0, 0));
                ImGui.TableNextColumn();
                ImGui.Dummy(new(0, 0));
                ImGui.TableNextColumn();
                ImGui.Dummy(new(0, 0));
                ImGui.TableNextColumn();
                ImGui.Checkbox("###createShowNotDl", ref _createOnlyShowNotDownloadable);
                UiSharedService.AttachToolTip("Filter by not downloadable");
                ImGui.TableNextColumn();
                ImGui.Dummy(new(0, 0));
                ImGui.TableNextColumn();
                ImGui.Dummy(new(0, 0));
                ImGui.TableNextColumn();
                ImGui.Dummy(new(0, 0));
                ImGui.TableNextColumn();
                ImGui.Dummy(new(0, 0));


                foreach (var entry in _charaDataManager.OwnCharaData.Values
                    .Where(v =>
                    {
                        bool show = true;
                        if (!string.IsNullOrWhiteSpace(_createCodeFilter))
                        {
                            show &= v.FullId.Contains(_createCodeFilter, StringComparison.OrdinalIgnoreCase);
                        }
                        if (!string.IsNullOrWhiteSpace(_createDescFilter))
                        {
                            show &= v.Description.Contains(_createDescFilter, StringComparison.OrdinalIgnoreCase);
                        }
                        if (_createOnlyShowFav)
                        {
                            show &= _configService.Current.FavoriteCodes.ContainsKey(v.FullId);
                        }
                        if (_createOnlyShowNotDownloadable)
                        {
                            show &= !(!v.HasMissingFiles && !string.IsNullOrEmpty(v.GlamourerData));
                        }

                        return show;
                    }).OrderBy(b => b.CreatedDate))
                {
                    var uDto = _charaDataManager.GetUpdateDto(entry.Id);
                    ImGui.TableNextColumn();
                    if (string.Equals(entry.Id, SelectedDtoId, StringComparison.Ordinal))
                        _uiSharedService.IconText(FontAwesomeIcon.CaretRight);

                    ImGui.TableNextColumn();
                    DrawAddOrRemoveFavorite(entry);

                    ImGui.TableNextColumn();
                    var idText = entry.FullId;
                    if (uDto?.HasChanges ?? false)
                    {
                        UiSharedService.ColorText(idText, ImGuiColors.DalamudYellow);
                        UiSharedService.AttachToolTip("条目有未保存的变更");
                    }
                    else
                    {
                        ImGui.TextUnformatted(idText);
                    }
                    if (ImGui.IsItemClicked()) SelectedDtoId = entry.Id;

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(entry.Description);
                    if (ImGui.IsItemClicked()) SelectedDtoId = entry.Id;
                    UiSharedService.AttachToolTip(entry.Description);

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(entry.CreatedDate.ToLocalTime().ToString());
                    if (ImGui.IsItemClicked()) SelectedDtoId = entry.Id;

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(entry.UpdatedDate.ToLocalTime().ToString());
                    if (ImGui.IsItemClicked()) SelectedDtoId = entry.Id;

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(entry.DownloadCount.ToString());
                    if (ImGui.IsItemClicked()) SelectedDtoId = entry.Id;

                    ImGui.TableNextColumn();
                    bool isDownloadable = !entry.HasMissingFiles
                        && !string.IsNullOrEmpty(entry.GlamourerData);
                    _uiSharedService.BooleanToColoredIcon(isDownloadable, false);
                    if (ImGui.IsItemClicked()) SelectedDtoId = entry.Id;
                    UiSharedService.AttachToolTip(isDownloadable ? "可被其他人下载" : "无法下载: 缺失文件或数据, 请手动浏览该条目");

                    ImGui.TableNextColumn();
                    var count = entry.FileGamePaths.Concat(entry.FileSwaps).Count();
                    ImGui.TextUnformatted(count.ToString());
                    if (ImGui.IsItemClicked()) SelectedDtoId = entry.Id;
                    UiSharedService.AttachToolTip(count == 0 ? "无附加文件" : "有附加文件");

                    ImGui.TableNextColumn();
                    bool hasGlamourerData = !string.IsNullOrEmpty(entry.GlamourerData);
                    _uiSharedService.BooleanToColoredIcon(hasGlamourerData, false);
                    if (ImGui.IsItemClicked()) SelectedDtoId = entry.Id;
                    UiSharedService.AttachToolTip(string.IsNullOrEmpty(entry.GlamourerData) ? "无Glamourer数据" : "有Glamourer数据");

                    ImGui.TableNextColumn();
                    bool hasCustomizeData = !string.IsNullOrEmpty(entry.CustomizeData);
                    _uiSharedService.BooleanToColoredIcon(hasCustomizeData, false);
                    if (ImGui.IsItemClicked()) SelectedDtoId = entry.Id;
                    UiSharedService.AttachToolTip(string.IsNullOrEmpty(entry.CustomizeData) ? "无Customize+数据" : "有Customize+数据");

                    ImGui.TableNextColumn();
                    FontAwesomeIcon eIcon = FontAwesomeIcon.None;
                    if (!Equals(DateTime.MaxValue, entry.ExpiryDate))
                        eIcon = FontAwesomeIcon.Clock;
                    _uiSharedService.IconText(eIcon, ImGuiColors.DalamudYellow);
                    if (ImGui.IsItemClicked()) SelectedDtoId = entry.Id;
                    if (eIcon != FontAwesomeIcon.None)
                    {
                        UiSharedService.AttachToolTip($"将于 {entry.ExpiryDate.ToLocalTime()} 过期");
                    }
                }
            }
        }

        using (ImRaii.Disabled(!_charaDataManager.Initialized || _charaDataManager.DataCreationTask != null || _charaDataManager.OwnCharaData.Count == _charaDataManager.MaxCreatableCharaData))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "新建MCD"))
            {
                _charaDataManager.CreateCharaDataEntry(_closalCts.Token);
                _selectNewEntry = true;
            }
        }
        if (_charaDataManager.DataCreationTask != null)
        {
            UiSharedService.AttachToolTip("请求过于频繁. 请稍后.");
        }
        if (!_charaDataManager.Initialized)
        {
            UiSharedService.AttachToolTip("点击 \"获取角色数据\" 再尝试新建.");
        }

        if (_charaDataManager.Initialized)
        {
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            UiSharedService.TextWrapped($"服务器上的数据: {_charaDataManager.OwnCharaData.Count}/{_charaDataManager.MaxCreatableCharaData}");
            if (_charaDataManager.OwnCharaData.Count == _charaDataManager.MaxCreatableCharaData)
            {
                ImGui.AlignTextToFramePadding();
                UiSharedService.ColorTextWrapped("你无法创建更多MCD数据.", ImGuiColors.DalamudYellow);
            }
        }

        if (_charaDataManager.DataCreationTask != null && !_charaDataManager.DataCreationTask.IsCompleted)
        {
            UiSharedService.ColorTextWrapped("正在创建...", ImGuiColors.DalamudYellow);
        }
        else if (_charaDataManager.DataCreationTask != null && _charaDataManager.DataCreationTask.IsCompleted)
        {
            var color = _charaDataManager.DataCreationTask.Result.Success ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed;
            UiSharedService.ColorTextWrapped(_charaDataManager.DataCreationTask.Result.Output, color);
        }

        ImGuiHelpers.ScaledDummy(10);
        ImGui.Separator();

        var charaDataEntries = _charaDataManager.OwnCharaData.Count;
        if (charaDataEntries != _dataEntries && _selectNewEntry && _charaDataManager.OwnCharaData.Any())
        {
            SelectedDtoId = _charaDataManager.OwnCharaData.OrderBy(o => o.Value.CreatedDate).Last().Value.Id;
            _selectNewEntry = false;
        }
        _dataEntries = _charaDataManager.OwnCharaData.Count;

        _ = _charaDataManager.OwnCharaData.TryGetValue(SelectedDtoId, out var dto);
        DrawEditCharaData(dto);
    }

    bool _selectNewEntry = false;
    int _dataEntries = 0;

    private void DrawSpecific(CharaDataExtendedUpdateDto updateDto)
    {
        UiSharedService.DrawTree("特定角色/同步贝访问权限", () =>
        {
            using (ImRaii.PushId("user"))
            {
                using (ImRaii.Group())
                {
                    InputComboHybrid("##AliasToAdd", "##AliasToAddPicker", ref _specificIndividualAdd, _pairManager.PairsWithGroups.Keys,
                        static pair => (pair.UserData.UID, pair.UserData.Alias, pair.UserData.AliasOrUID, pair.GetNote()));
                    ImGui.SameLine();
                    using (ImRaii.Disabled(string.IsNullOrEmpty(_specificIndividualAdd)
                        || updateDto.UserList.Any(f => string.Equals(f.UID, _specificIndividualAdd, StringComparison.Ordinal) || string.Equals(f.Alias, _specificIndividualAdd, StringComparison.Ordinal))))
                    {
                        if (_uiSharedService.IconButton(FontAwesomeIcon.Plus))
                        {
                            updateDto.AddUserToList(_specificIndividualAdd);
                            _specificIndividualAdd = string.Empty;
                        }
                    }
                    ImGui.SameLine();
                    ImGui.TextUnformatted("添加UID/个性UID");
                    _uiSharedService.DrawHelpText("添加到本列表中的角色无论你是否和他们配对都可以查看该MCD数据." + UiSharedService.TooltipSeparator
                        + "注意: 错误输入将被自动清除.");

                    using (var lb = ImRaii.ListBox("允许的用户", new(200 * ImGuiHelpers.GlobalScale, 200 * ImGuiHelpers.GlobalScale)))
                    {
                        foreach (var user in updateDto.UserList)
                        {
                            var userString = string.IsNullOrEmpty(user.Alias) ? user.UID : $"{user.Alias} ({user.UID})";
                            if (ImGui.Selectable(userString, string.Equals(user.UID, _selectedSpecificUserIndividual, StringComparison.Ordinal)))
                            {
                                _selectedSpecificUserIndividual = user.UID;
                            }
                        }
                    }

                    using (ImRaii.Disabled(string.IsNullOrEmpty(_selectedSpecificUserIndividual)))
                    {
                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "移除选中UID"))
                        {
                            updateDto.RemoveUserFromList(_selectedSpecificUserIndividual);
                            _selectedSpecificUserIndividual = string.Empty;
                        }
                    }

                    using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
                    {
                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.ExclamationTriangle, "应用当前的用户许可到所有MCD条目"))
                        {
                            foreach (var own in _charaDataManager.OwnCharaData.Values.Where(k => !string.Equals(k.Id, updateDto.Id, StringComparison.Ordinal)))
                            {
                                var otherUpdateDto = _charaDataManager.GetUpdateDto(own.Id);
                                if (otherUpdateDto == null) continue;
                                foreach (var user in otherUpdateDto.UserList.Select(k => k.UID).Concat(otherUpdateDto.AllowedUsers ?? []).Distinct(StringComparer.Ordinal).ToList())
                                {
                                    otherUpdateDto.RemoveUserFromList(user);
                                }
                                foreach (var user in updateDto.UserList.Select(k => k.UID).Concat(updateDto.AllowedUsers ?? []).Distinct(StringComparer.Ordinal).ToList())
                                {
                                    otherUpdateDto.AddUserToList(user);
                                }
                            }
                        }
                    }
                    UiSharedService.AttachToolTip("这会将当前的用户许可设置应用到你所有的Mare角色数据条目中." + UiSharedService.TooltipSeparator
                        + "按住CTRL并点击.");
                }
            }
            ImGui.SameLine();
            ImGuiHelpers.ScaledDummy(20);
            ImGui.SameLine();

            using (ImRaii.PushId("group"))
            {
                using (ImRaii.Group())
                {
                    InputComboHybrid("##GroupAliasToAdd", "##GroupAliasToAddPicker", ref _specificGroupAdd, _pairManager.Groups.Keys,
                        group => (group.GID, group.Alias, group.AliasOrGID, _serverConfigurationManager.GetNoteForGid(group.GID)));
                    ImGui.SameLine();
                    using (ImRaii.Disabled(string.IsNullOrEmpty(_specificGroupAdd)
                        || updateDto.GroupList.Any(f => string.Equals(f.GID, _specificGroupAdd, StringComparison.Ordinal) || string.Equals(f.Alias, _specificGroupAdd, StringComparison.Ordinal))))
                    {
                        if (_uiSharedService.IconButton(FontAwesomeIcon.Plus))
                        {
                            updateDto.AddGroupToList(_specificGroupAdd);
                            _specificGroupAdd = string.Empty;
                        }
                    }
                    ImGui.SameLine();
                    ImGui.TextUnformatted("添加GID/个性GID");
                    _uiSharedService.DrawHelpText("在该GID对应的配对贝中的所有用户将可以查看该MCD数据, 无论你是否暂停了配对." + UiSharedService.TooltipSeparator
                        + "注意: 错误输入将被自动清除.");

                    using (var lb = ImRaii.ListBox("允许的配对贝", new(200 * ImGuiHelpers.GlobalScale, 200 * ImGuiHelpers.GlobalScale)))
                    {
                        foreach (var group in updateDto.GroupList)
                        {
                            var userString = string.IsNullOrEmpty(group.Alias) ? group.GID : $"{group.Alias} ({group.GID})";
                            if (ImGui.Selectable(userString, string.Equals(group.GID, _selectedSpecificGroupIndividual, StringComparison.Ordinal)))
                            {
                                _selectedSpecificGroupIndividual = group.GID;
                            }
                        }
                    }

                    using (ImRaii.Disabled(string.IsNullOrEmpty(_selectedSpecificGroupIndividual)))
                    {
                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "移除选中贝"))
                        {
                            updateDto.RemoveGroupFromList(_selectedSpecificGroupIndividual);
                            _selectedSpecificGroupIndividual = string.Empty;
                        }
                    }

                    using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
                    {
                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.ExclamationTriangle, "将目前允许的同步贝同步到所有MCD条目中"))
                        {
                            foreach (var own in _charaDataManager.OwnCharaData.Values.Where(k => !string.Equals(k.Id, updateDto.Id, StringComparison.Ordinal)))
                            {
                                var otherUpdateDto = _charaDataManager.GetUpdateDto(own.Id);
                                if (otherUpdateDto == null) continue;
                                foreach (var group in otherUpdateDto.GroupList.Select(k => k.GID).Concat(otherUpdateDto.AllowedGroups ?? []).Distinct(StringComparer.Ordinal).ToList())
                                {
                                    otherUpdateDto.RemoveGroupFromList(group);
                                }
                                foreach (var group in updateDto.GroupList.Select(k => k.GID).Concat(updateDto.AllowedGroups ?? []).Distinct(StringComparer.Ordinal).ToList())
                                {
                                    otherUpdateDto.AddGroupToList(group);
                                }
                            }
                        }
                    }
                    UiSharedService.AttachToolTip("这会将当前的同步贝许可设置应用到你所有的Mare角色数据条目中." + UiSharedService.TooltipSeparator
                        + "按住CTRL并点击.");
                }
            }

            ImGui.Separator();
            ImGuiHelpers.ScaledDummy(5);
        });
    }

    private void InputComboHybrid<T>(string inputId, string comboId, ref string value, IEnumerable<T> comboEntries,
        Func<T, (string Id, string? Alias, string AliasOrId, string? Note)> parseEntry)
    {
        const float ComponentWidth = 200;
        UiSharedService.ScaledNextItemWidth(ComponentWidth - ImGui.GetFrameHeight());
        ImGui.InputText(inputId, ref value, 20);
        ImGui.SameLine(0.0f, 0.0f);

        using var combo = ImRaii.Combo(comboId, string.Empty, ImGuiComboFlags.NoPreview | ImGuiComboFlags.PopupAlignLeft);
        if (!combo)
        {
            return;
        }

        if (_openComboHybridEntries is null || !string.Equals(_openComboHybridId, comboId, StringComparison.Ordinal))
        {
            var valueSnapshot = value;
            _openComboHybridEntries = comboEntries
                .Select(parseEntry)
                .Where(entry => entry.Id.Contains(valueSnapshot, StringComparison.OrdinalIgnoreCase)
                    || (entry.Alias is not null && entry.Alias.Contains(valueSnapshot, StringComparison.OrdinalIgnoreCase))
                    || (entry.Note is not null && entry.Note.Contains(valueSnapshot, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(entry => entry.Note is null ? entry.AliasOrId : $"{entry.Note} ({entry.AliasOrId})", StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _openComboHybridId = comboId;
        }
        _comboHybridUsedLastFrame = true;

        // Is there a better way to handle this?
        var width = ComponentWidth - 2 * ImGui.GetStyle().FramePadding.X - (_openComboHybridEntries.Length > 8 ? ImGui.GetStyle().ScrollbarSize : 0);
        foreach (var (id, alias, aliasOrId, note) in _openComboHybridEntries)
        {
            var selected = !string.IsNullOrEmpty(value)
                && (string.Equals(id, value, StringComparison.Ordinal) || string.Equals(alias, value, StringComparison.Ordinal));
            using var font = ImRaii.PushFont(UiBuilder.MonoFont, note is null);
            if (ImGui.Selectable(note is null ? aliasOrId : $"{note} ({aliasOrId})", selected, ImGuiSelectableFlags.None, new(width, 0)))
            {
                value = aliasOrId;
            }
        }
    }
}