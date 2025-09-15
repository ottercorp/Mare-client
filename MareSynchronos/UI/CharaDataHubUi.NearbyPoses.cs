using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Utility;
using Dalamud.Interface;
using System.Numerics;

namespace MareSynchronos.UI;

internal partial class CharaDataHubUi
{
    private void DrawNearbyPoses()
    {
        _uiSharedService.BigText("附近的姿势");

        DrawHelpFoldout("本标签将显示你附近的他人共享的姿势." + Environment.NewLine + Environment.NewLine
                        + "这里的姿势是指附加了位置信息且访问权限设置为共享的姿势. "
                        + "这意味着所有在 '与你共享' 中设置了位置信息且在你所在地附近的姿势将被显示." + Environment.NewLine
                        + "默认会显示所有应该显示的姿势. 在房屋中设置的姿势应该在正确的服务器和地区显示." + Environment.NewLine + Environment.NewLine
                        + "姿势默认显示为漂浮的幽灵, 也会显示在下方的列表中. 鼠标悬浮在一条记录上时对应的幽灵将被高亮." + Environment.NewLine + Environment.NewLine
                        + "你可以将对应的姿势应用到自身或生成一个角色并应用." + Environment.NewLine + Environment.NewLine
                        + "你可以在 '设置 & 过滤' 中进行相关设置.");

        UiSharedService.DrawTree("设置 & 过滤", () =>
        {
            string filterByUser = _charaDataNearbyManager.UserNoteFilter;
            if (ImGui.InputTextWithHint("##filterbyuser", "按用户过滤", ref filterByUser, 50))
            {
                _charaDataNearbyManager.UserNoteFilter = filterByUser;
            }
            bool onlyCurrent = _configService.Current.NearbyOwnServerOnly;
            if (ImGui.Checkbox("仅显示当前服务器", ref onlyCurrent))
            {
                _configService.Current.NearbyOwnServerOnly = onlyCurrent;
                _configService.Save();
            }
            _uiSharedService.DrawHelpText("关闭这个选项将显示所有服务器中在这附近的姿势");
            bool showOwn = _configService.Current.NearbyShowOwnData;
            if (ImGui.Checkbox("也显示你的数据", ref showOwn))
            {
                _configService.Current.NearbyShowOwnData = showOwn;
                _configService.Save();
            }
            _uiSharedService.DrawHelpText("打开这个选项将显示你上传的姿势");
            bool ignoreHousing = _configService.Current.NearbyIgnoreHousingLimitations;
            if (ImGui.Checkbox("无视房屋限制", ref ignoreHousing))
            {
                _configService.Current.NearbyIgnoreHousingLimitations = ignoreHousing;
                _configService.Save();
            }
            _uiSharedService.DrawHelpText("这将解除在房屋中设置的姿势的限制. (无视分区, 门牌, 房间)" + UiSharedService.TooltipSeparator
                + "注意: 包含房屋相关装饰, 家居等的姿势. 在非对应位置的显示可能会有问题.");
            bool showWisps = _configService.Current.NearbyDrawWisps;
            if (ImGui.Checkbox("在有姿势的位置显示幽灵", ref showWisps))
            {
                _configService.Current.NearbyDrawWisps = showWisps;
                _configService.Save();
            }
            _uiSharedService.DrawHelpText("开启时, 将在有姿势的位置绘制幽灵.");
            int poseDetectionDistance = _configService.Current.NearbyDistanceFilter;
            UiSharedService.ScaledNextItemWidth(100);
            if (ImGui.SliderInt("检测距离", ref poseDetectionDistance, 5, 1000))
            {
                _configService.Current.NearbyDistanceFilter = poseDetectionDistance;
                _configService.Save();
            }
            _uiSharedService.DrawHelpText("可以修改显示姿势的最远距离. 最大可以显示本地图中的所有姿势.");
            bool alwaysShow = _configService.Current.NearbyShowAlways;
            if (ImGui.Checkbox("关闭标签后继续显示", ref alwaysShow))
            {
                _configService.Current.NearbyShowAlways = alwaysShow;
                _configService.Save();
            }
            _uiSharedService.DrawHelpText("打开选项将在标签关闭后继续显示相关姿势." + UiSharedService.TooltipSeparator
                + "注意: 在战斗和演奏中不会显示.");
        });

        if (!_uiSharedService.IsInGpose)
        {
            ImGuiHelpers.ScaledDummy(5);
            UiSharedService.DrawGroupedCenteredColorText("仅在GPose中可用.", ImGuiColors.DalamudYellow);
            ImGuiHelpers.ScaledDummy(5);
        }

        DrawUpdateSharedDataButton();

        UiSharedService.DistanceSeparator();

        using var child = ImRaii.Child("nearbyPosesChild", new(0, 0), false, ImGuiWindowFlags.AlwaysAutoResize);

        ImGuiHelpers.ScaledDummy(3f);

        using var indent = ImRaii.PushIndent(5f);
        if (_charaDataNearbyManager.NearbyData.Count == 0)
        {
            UiSharedService.DrawGroupedCenteredColorText("未找到数据.", ImGuiColors.DalamudYellow);
        }

        bool wasAnythingHovered = false;
        int i = 0;
        foreach (var pose in _charaDataNearbyManager.NearbyData.OrderBy(v => v.Value.Distance))
        {
            using var poseId = ImRaii.PushId("nearbyPose" + (i++));
            var pos = ImGui.GetCursorPos();
            var circleDiameter = 60f;
            var circleOriginX = ImGui.GetWindowContentRegionMax().X - circleDiameter - pos.X;
            float circleOffsetY = 0;

            UiSharedService.DrawGrouped(() =>
            {
                string? userNote = _serverConfigurationManager.GetNoteForUid(pose.Key.MetaInfo.Uploader.UID);
                var noteText = pose.Key.MetaInfo.IsOwnData ? "你" : (userNote == null ? pose.Key.MetaInfo.Uploader.AliasOrUID : $"{userNote} ({pose.Key.MetaInfo.Uploader.AliasOrUID})");
                ImGui.TextUnformatted("制作者 ");
                ImGui.SameLine();
                UiSharedService.ColorText(noteText, ImGuiColors.ParsedGreen);
                using (ImRaii.Group())
                {
                    UiSharedService.ColorText("数据描述", ImGuiColors.DalamudGrey);
                    ImGui.SameLine();
                    _uiSharedService.IconText(FontAwesomeIcon.ExternalLinkAlt, ImGuiColors.DalamudGrey);
                }
                UiSharedService.AttachToolTip(pose.Key.MetaInfo.Description);
                UiSharedService.ColorText("描述", ImGuiColors.DalamudGrey);
                ImGui.SameLine();
                UiSharedService.TextWrapped(pose.Key.Description ?? "未设置", circleOriginX);
                var posAfterGroup = ImGui.GetCursorPos();
                var groupHeightCenter = (posAfterGroup.Y - pos.Y) / 2;
                circleOffsetY = (groupHeightCenter - circleDiameter / 2);
                if (circleOffsetY < 0) circleOffsetY = 0;
                ImGui.SetCursorPos(new Vector2(circleOriginX, pos.Y));
                ImGui.Dummy(new Vector2(circleDiameter, circleDiameter));
                UiSharedService.AttachToolTip("点击以在地图上显示位置" + UiSharedService.TooltipSeparator
                    + pose.Key.WorldDataDescriptor);
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    _dalamudUtilService.SetMarkerAndOpenMap(pose.Key.Position, pose.Key.Map);
                }
                ImGui.SetCursorPos(posAfterGroup);
                if (_uiSharedService.IsInGpose)
                {
                    GposePoseAction(() =>
                    {
                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowRight, "应用姿势"))
                        {
                            _charaDataManager.ApplyFullPoseDataToGposeTarget(pose.Key);
                        }
                    }, $"应用姿势和位置于 {CharaName(_gposeTarget)}", _hasValidGposeTarget);
                    ImGui.SameLine();
                    GposeMetaInfoAction((_) =>
                    {
                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "生成并应用"))
                        {
                            _charaDataManager.SpawnAndApplyWorldTransform(pose.Key.MetaInfo, pose.Key);
                        }
                    }, "生成角色并应用姿势和位置", pose.Key.MetaInfo, _hasValidGposeTarget, true);
                }
            });
            if (ImGui.IsItemHovered())
            {
                wasAnythingHovered = true;
                _nearbyHovered = pose.Key;
            }
            var drawList = ImGui.GetWindowDrawList();
            var circleRadius = circleDiameter / 2f;
            var windowPos = ImGui.GetWindowPos();
            var scrollX = ImGui.GetScrollX();
            var scrollY = ImGui.GetScrollY();
            var circleCenter = new Vector2(windowPos.X + circleOriginX + circleRadius - scrollX, windowPos.Y + pos.Y + circleRadius + circleOffsetY - scrollY);
            var rads = pose.Value.Direction * (Math.PI / 180);

            float halfConeAngleRadians = 15f * (float)Math.PI / 180f;
            Vector2 baseDir1 = new Vector2((float)Math.Sin(rads - halfConeAngleRadians), -(float)Math.Cos(rads - halfConeAngleRadians));
            Vector2 baseDir2 = new Vector2((float)Math.Sin(rads + halfConeAngleRadians), -(float)Math.Cos(rads + halfConeAngleRadians));

            Vector2 coneBase1 = circleCenter + baseDir1 * circleRadius;
            Vector2 coneBase2 = circleCenter + baseDir2 * circleRadius;

            // Draw the cone as a filled triangle
            drawList.AddTriangleFilled(circleCenter, coneBase1, coneBase2, UiSharedService.Color(ImGuiColors.ParsedGreen));
            drawList.AddCircle(circleCenter, circleDiameter / 2, UiSharedService.Color(ImGuiColors.DalamudWhite), 360, 2);
            var distance = pose.Value.Distance.ToString("0.0") + "y";
            var textSize = ImGui.CalcTextSize(distance);
            drawList.AddText(new Vector2(circleCenter.X - textSize.X / 2, circleCenter.Y + textSize.Y / 3f), UiSharedService.Color(ImGuiColors.DalamudWhite), distance);

            ImGuiHelpers.ScaledDummy(3);
        }

        if (!wasAnythingHovered) _nearbyHovered = null;
        _charaDataNearbyManager.SetHoveredVfx(_nearbyHovered);
    }

    private void DrawUpdateSharedDataButton()
    {
        using (ImRaii.Disabled(_charaDataManager.GetAllDataTask != null
            || (_charaDataManager.GetSharedWithYouTimeoutTask != null && !_charaDataManager.GetSharedWithYouTimeoutTask.IsCompleted)))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowCircleDown, "更新数据"))
            {
                _ = _charaDataManager.GetAllSharedData(_disposalCts.Token).ContinueWith(u => UpdateFilteredItems());
            }
        }
        if (_charaDataManager.GetSharedWithYouTimeoutTask != null && !_charaDataManager.GetSharedWithYouTimeoutTask.IsCompleted)
        {
            UiSharedService.AttachToolTip("每分钟只能刷新一次数据. 请稍后.");
        }
    }
}