using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SprocketModAPI
{
    internal sealed partial class ModMenuWindow
    {
        private void RebuildList()
        {
            ClearObjects(uiListObjects);
            if (uiListContent == null)
                return;

            IReadOnlyList<ModMenuRow> filtered = ModMenuListModel.Filter(rows, search);
            if (filtered.Count == 0)
            {
                TextMeshProUGUI empty = CreateText(uiListContent, "Empty", "NO MODS MATCH", 13f, TextAlignmentOptions.Center, HeaderTextColor, false);
                SetPreferredHeight(empty.gameObject, ModMenuLayout.ListRowHeight);
                uiListObjects.Add(empty.gameObject);
                return;
            }

            for (int index = 0; index < filtered.Count; index++)
                uiListObjects.Add(CreateListRow(filtered[index]));
        }

        private GameObject CreateListRow(ModMenuRow row)
        {
            bool selected = string.Equals(row.Location, selectedLocation, StringComparison.OrdinalIgnoreCase);
            Color background = selected ? SelectedRowColor
                : row.IsDisabled ? GroupColor
                : RowColor;
            // 底色交给 Button 的 ColorBlock 调色：图像本身必须是白色，否则两层颜色相乘会压成近黑。
            GameObject item = CreateImageNode(uiListContent!, "Mod Row", Color.white, true);
            SetPreferredHeight(item, ModMenuLayout.ListRowHeight);
            AddBorder(item, RowBorderColor);

            Image image = item.GetComponent<Image>();
            Button button = item.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = background;
            colors.highlightedColor = selected ? SelectedRowColor : GroupColor;
            colors.pressedColor = GroupColor;
            colors.selectedColor = background;
            colors.disabledColor = ButtonDisabledColor;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener((UnityAction)(() => SelectRow(row)));

            string suffix = row.IsDisabled ? "  [DISABLED]" : row.HasConfigPage ? "  [CFG]" : "";
            if (row.MissingDependencies.Count != 0)
                suffix += "  [!]";
            TextMeshProUGUI label = CreateText(item.transform, "Label",
                $"{row.DisplayName}{suffix}", 13f, TextAlignmentOptions.Left,
                row.IsDisabled ? HeaderTextColor : RowTextColor, false);
            RectTransform labelRect = label.GetComponent<RectTransform>();
            SetStretch(labelRect);
            labelRect.offsetMin = new Vector2(ModMenuLayout.ListRowPadding, 0f);
            labelRect.offsetMax = new Vector2(-ModMenuLayout.ListRowPadding, 0f);

            if (selected)
            {
                GameObject bar = CreateImageNode(item.transform, "Selection Bar", AccentColor, false);
                RectTransform barRect = bar.GetComponent<RectTransform>();
                barRect.anchorMin = new Vector2(0f, 0f);
                barRect.anchorMax = new Vector2(0f, 1f);
                barRect.pivot = new Vector2(0f, 0.5f);
                barRect.offsetMin = new Vector2(0f, 0f);
                barRect.offsetMax = new Vector2(ModMenuLayout.SelectionBarWidth, 0f);
            }

            return item;
        }

        private void SelectRow(ModMenuRow row)
        {
            selectedLocation = row.Location;
            showingConfig = false;
            CloseConfigPopup();
            uiSearchInput?.DeactivateInputField();
            RebuildAll();
        }

        private void RebuildDetail()
        {
            ClearObjects(uiDetailObjects);
            if (uiDetailContent == null || uiDetailTitle == null || uiDetailSubtitle == null || uiDetailActions == null)
                return;

            ModMenuRow? row = SelectedRow;
            if (row == null)
            {
                uiDetailTitle.text = "SELECT A MOD";
                uiDetailSubtitle.text = "Metadata is read from the loaded assembly.";
                return;
            }

            string version = string.IsNullOrEmpty(row.Version) ? "" : $"  v{row.Version}";
            uiDetailTitle.text = row.DisplayName;
            uiDetailSubtitle.text = $"{row.Id}{version}  -  {row.KindLabel}";

            CreateDetailActions(row);
            CreateDetailBody(row);
        }

        private void CreateDetailActions(ModMenuRow row)
        {
            if (row.HasConfigPage)
            {
                CreateButton(uiDetailActions!.transform, "Config Button", "CONFIG", ModMenuLayout.ActionButtonWidth, ModMenuLayout.DetailActionsHeight, 12f, true, Color.white, true,
                    () => OpenConfigPopup(row), out GameObject configObject, out _);
                uiDetailObjects.Add(configObject);
            }

            if (IsSelfAssembly(row))
            {
                return;
            }

            string label = row.IsDisabled ? "ENABLE" : "DISABLE";
            bool primary = row.IsDisabled;
            CreateButton(uiDetailActions!.transform, "Toggle Button", label, ModMenuLayout.ActionButtonWidth, ModMenuLayout.DetailActionsHeight, 12f, primary, primary ? Color.white : BoundTextColor, primary,
                () => ToggleEnabled(row), out GameObject toggleObject, out _);
            uiDetailObjects.Add(toggleObject);
        }

        private void CreateDetailBody(ModMenuRow row)
        {
            if (!string.IsNullOrEmpty(row.Description))
                AddWrappedBlock("Description", row.Description);

            // 按语义分组：所有字段堆成一条长清单时无法快速定位。
            AddSectionHeader("Metadata", uiDetailContent!, uiDetailObjects);
            AddDetailRow("Category", row.Category);
            AddDetailRow("License", row.License);
            AddDetailRow("Repository", row.Repository);
            AddDetailRow("Homepage", row.Homepage);
            AddDetailRow("Credits", row.Credits);

            bool hasDependencyInfo = row.RequiredDependencies.Count != 0
                || row.MissingDependencies.Count != 0 || row.OptionalDependencies.Count != 0
                || row.IncompatibleAssemblies.Count != 0 || row.HasIncompatiblePresent;
            if (hasDependencyInfo)
            {
                AddSectionHeader("Dependencies", uiDetailContent!, uiDetailObjects);
                AddDetailRow("Requires", row.RequiredDependencies);
                AddDetailRow("Missing", row.MissingDependencies);
                AddDetailRow("Optional deps", row.OptionalDependencies);
                AddDetailRow("Incompatible", row.IncompatibleAssemblies);
                if (row.HasIncompatiblePresent)
                    AddDetailRow("Conflict", "an incompatible assembly is installed");
            }

            AddSectionHeader("Files", uiDetailContent!, uiDetailObjects);
            AddDetailRow("Assembly", string.IsNullOrEmpty(row.AssemblyName) ? Path.GetFileName(row.Location) : row.AssemblyName);
            AddDetailRow("Status", row.IsDisabled ? "DISABLED (restart required)" : "LOADED");
            // 哈希与路径很长：换行显示完整值，而不是被省略号截断成没用的前缀。
            if (!string.IsNullOrEmpty(row.AssemblyHash))
                AddWrappedBlock("Hash", row.AssemblyHash);
            AddWrappedBlock("Path", row.Location);
        }

        private void AddDetailRow(string label, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            GameObject rowObject = CreateLayoutNode(uiDetailContent!, "Detail Row");
            SetPreferredHeight(rowObject, ModMenuLayout.DetailRowHeight);
            HorizontalLayoutGroup layout = rowObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            TextMeshProUGUI labelText = CreateText(rowObject.transform, "Label", label.ToUpperInvariant(), 11f, TextAlignmentOptions.Left, HeaderTextColor, false);
            SetPreferredWidth(labelText.gameObject, ModMenuLayout.DetailLabelWidth);

            TextMeshProUGUI valueText = CreateText(rowObject.transform, "Value", value, 12f, TextAlignmentOptions.Left, BoundTextColor, false);
            SetFlexibleWidth(valueText.gameObject, 1f);
            uiDetailObjects.Add(rowObject);
        }

        // 依赖清单按行展开：一项一行，不靠逗号串成一条被省略号截断的长值。
        private void AddDetailRow(string label, IReadOnlyList<string> values)
        {
            if (values.Count == 0)
                return;

            GameObject rowObject = CreateLayoutNode(uiDetailContent!, "Detail List Row");
            HorizontalLayoutGroup layout = rowObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            TextMeshProUGUI labelText = CreateText(rowObject.transform, "Label", label.ToUpperInvariant(), 11f, TextAlignmentOptions.TopLeft, HeaderTextColor, false);
            SetPreferredWidth(labelText.gameObject, ModMenuLayout.DetailLabelWidth);

            TextMeshProUGUI valueText = CreateText(rowObject.transform, "Value", string.Join("\n", values), 12f, TextAlignmentOptions.TopLeft, BoundTextColor, false);
            valueText.enableWordWrapping = true;
            valueText.overflowMode = TextOverflowModes.Overflow;
            SetFlexibleWidth(valueText.gameObject, 1f);
            uiDetailObjects.Add(rowObject);
        }

        private void AddWrappedBlock(string label, string value)
        {
            GameObject rowObject = CreateLayoutNode(uiDetailContent!, "Detail Block");
            VerticalLayoutGroup layout = rowObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 2f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            TextMeshProUGUI labelText = CreateText(rowObject.transform, "Label", label.ToUpperInvariant(), 11f, TextAlignmentOptions.Left, HeaderTextColor, false);
            TextMeshProUGUI valueText = CreateText(rowObject.transform, "Value", value, 12f, TextAlignmentOptions.TopLeft, BoundTextColor, false);
            valueText.enableWordWrapping = true;
            valueText.overflowMode = TextOverflowModes.Overflow;
            uiDetailObjects.Add(rowObject);
        }

        private void CreateConfigBody(ModMenuRow row, IModConfigRegistration registration, RectTransform target, List<GameObject> bucket)
        {
            ModConfigSnapshot snapshot = registration.Snapshot;
            bool wroteHeader = false;
            foreach (ModConfigEntrySnapshot entry in snapshot.Entries)
            {
                if (entry.Definition.SectionId.Length == 0)
                {
                    if (!wroteHeader)
                    {
                        AddSectionHeader("General", target, bucket);
                        wroteHeader = true;
                    }

                    AddConfigRow(entry, target, bucket);
                }
            }

            foreach (ModConfigSectionDefinition section in snapshot.Sections)
            {
                var entries = snapshot.Entries.Where(entry => string.Equals(entry.Definition.SectionId, section.Id, StringComparison.Ordinal)).ToArray();
                if (entries.Length == 0)
                    continue;

                AddSectionHeader(string.IsNullOrEmpty(section.Title) ? section.Id : section.Title, target, bucket);
                foreach (ModConfigEntrySnapshot entry in entries)
                    AddConfigRow(entry, target, bucket);
            }

            if (snapshot.Entries.Count == 0)
            {
                TextMeshProUGUI empty = CreateText(target, "Empty", "THIS MOD DECLARED NO SETTINGS", 12f, TextAlignmentOptions.Left, HeaderTextColor, false);
                SetPreferredHeight(empty.gameObject, ModMenuLayout.DetailRowHeight);
                bucket.Add(empty.gameObject);
            }
        }

        // 每个控件都要登记进调用方的那一份列表：清空时按列表销毁，漏掉谁就会在下次重建时留下孤儿。
        private void AddSectionHeader(string title, RectTransform target, List<GameObject> bucket)
        {
            TextMeshProUGUI header = CreateText(target, "Section", title.ToUpperInvariant(), 12f, TextAlignmentOptions.Left, Color.white, true);
            header.margin = new Vector4(0f, 8f, 0f, 4f);
            SetPreferredHeight(header.gameObject, ModMenuLayout.SectionRowHeight);
            bucket.Add(header.gameObject);
        }

        private void AddConfigRow(ModConfigEntrySnapshot entry, RectTransform target, List<GameObject> bucket)
        {
            GameObject rowObject = CreateLayoutNode(target, "Config Row");
            SetPreferredHeight(rowObject, ModMenuLayout.ListRowHeight);
            HorizontalLayoutGroup layout = rowObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            TextMeshProUGUI label = CreateText(rowObject.transform, "Label", entry.Definition.DisplayName, 12f, TextAlignmentOptions.Left, RowTextColor, false);
            if (entry.Definition.Kind == ModConfigEntryKind.Slider)
            {
                // 范围紧跟变量名，控件被末尾的弹性空白推到右边。
                CreateText(rowObject.transform, "Range", RangeLabel(entry), 11f, TextAlignmentOptions.Left, HeaderTextColor, false);
                GameObject spacer = CreateLayoutNode(rowObject.transform, "Spacer");
                SetFlexibleWidth(spacer, 1f);
            }
            else
            {
                SetFlexibleWidth(label.gameObject, 1f);
            }

            switch (entry.Definition.Kind)
            {
                case ModConfigEntryKind.Toggle:
                    bool toggleValue = DraftBool(entry);
                    CreateButton(rowObject.transform, "Toggle", toggleValue ? "ON" : "OFF", ModMenuLayout.SmallButtonWidth, ModMenuLayout.InputHeight, 12f,
                        toggleValue, toggleValue ? Color.white : BoundTextColor, toggleValue,
                        () => SetDraftValue(entry, !toggleValue), out GameObject toggleObject, out _);
                    bucket.Add(toggleObject);
                    break;
                case ModConfigEntryKind.Slider:
                    CreateButton(rowObject.transform, "Step Down", "-", ModMenuLayout.StepButtonWidth, ModMenuLayout.InputHeight, 14f, false, BoundTextColor, false,
                        () => SetDraftValue(entry, ClampDraftNumber(entry, -1)), out GameObject downObject, out _);
                    bucket.Add(downObject);

                    TMP_InputField numberField = CreateInputField(rowObject.transform, "Value", "", FormatNumber(DraftNumber(entry)), ModMenuLayout.NumberFieldWidth);
                    SetPreferredHeight(numberField.gameObject, ModMenuLayout.InputHeight);
                    numberField.contentType = TMP_InputField.ContentType.DecimalNumber;
                    numberField.characterLimit = 16;
                    numberField.onEndEdit.AddListener((UnityAction<string>)(value => SetDraftValue(entry, ParseDraftNumber(entry, value))));
                    bucket.Add(numberField.gameObject);

                    CreateButton(rowObject.transform, "Step Up", "+", ModMenuLayout.StepButtonWidth, ModMenuLayout.InputHeight, 14f, false, BoundTextColor, false,
                        () => SetDraftValue(entry, ClampDraftNumber(entry, 1)), out GameObject upObject, out _);
                    bucket.Add(upObject);
                    break;
                case ModConfigEntryKind.Choice:
                    CreateButton(rowObject.transform, "Choice", DraftText(entry), 150f, ModMenuLayout.InputHeight, 12f, false, BoundTextColor, false,
                        () => SetDraftValue(entry, NextOption(DraftText(entry), entry)), out GameObject choiceObject, out _);
                    bucket.Add(choiceObject);
                    break;
                case ModConfigEntryKind.Text:
                    TMP_InputField field = CreateInputField(rowObject.transform, "Value", "", DraftText(entry), 220f);
                    SetPreferredHeight(field.gameObject, ModMenuLayout.InputHeight);
                    field.onEndEdit.AddListener((UnityAction<string>)(value => SetDraftValue(entry, value ?? "")));
                    bucket.Add(field.gameObject);
                    break;
            }

            bool modified = IsDraftModified(entry);
            CreateButton(rowObject.transform, "Reset", "RESET", ModMenuLayout.SmallButtonWidth, ModMenuLayout.InputHeight, 11f,
                false, modified ? Color.white : HeaderTextColor, modified,
                () => SetDraftValue(entry, entry.Definition.DefaultValue), out GameObject resetObject, out _);
            Button resetButton = resetObject.GetComponent<Button>();
            resetButton.interactable = modified;
            if (modified)
                ApplyAttentionStyle(resetObject, resetButton);
            bucket.Add(resetObject);

            bucket.Add(rowObject);
        }

        // 偏离初始值的 RESET 用冷灰高亮（与强调色区分：琥珀色只表示主操作）。
        private static void ApplyAttentionStyle(GameObject buttonObject, Button button)
        {
            ColorBlock colors = button.colors;
            colors.normalColor = AttentionColor;
            colors.highlightedColor = AttentionHoverColor;
            colors.pressedColor = ButtonPressedColor;
            colors.selectedColor = AttentionColor;
            colors.disabledColor = ButtonDisabledColor;
            button.colors = colors;

            Outline outline = buttonObject.GetComponent<Outline>();
            if (outline != null)
                outline.effectColor = AttentionBorderColor;
        }

        // 步进按钮与输入框共用同一个钳制范围：步进不会越界，手输也落回区间内。
        private double ClampDraftNumber(ModConfigEntrySnapshot entry, int direction)
        {
            double step = entry.Definition.Step > 0 ? entry.Definition.Step : 0.1;
            double next = DraftNumber(entry) + (step * direction);
            return Math.Min(entry.Definition.Maximum, Math.Max(entry.Definition.Minimum, next));
        }

        private static double ParseDraftNumber(ModConfigEntrySnapshot entry, string text)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                return entry.NumberValue;

            return Math.Min(entry.Definition.Maximum, Math.Max(entry.Definition.Minimum, parsed));
        }

        private static string FormatNumber(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static string RangeLabel(ModConfigEntrySnapshot entry)
            => $"{FormatNumber(entry.Definition.Minimum)} .. {FormatNumber(entry.Definition.Maximum)}";

        private static string NextOption(string current, ModConfigEntrySnapshot entry)
        {
            IReadOnlyList<string> options = entry.Definition.Options;
            if (options.Count == 0)
                return current;

            int index = 0;
            for (int position = 0; position < options.Count; position++)
            {
                if (string.Equals(options[position], current, StringComparison.Ordinal))
                {
                    index = position;
                    break;
                }
            }

            return options[(index + 1) % options.Count];
        }

        private void ApplyDraft()
        {
            if (configPopupRegistration == null)
                return;

            int applied = 0;
            ModConfigSnapshot snapshot = configPopupRegistration.Snapshot;
            foreach (ModConfigEntrySnapshot entry in snapshot.Entries)
            {
                if (!configDraft.TryGetValue(entry.Definition.Key, out object? value))
                    continue;

                try
                {
                    switch (entry.Definition.Kind)
                    {
                        case ModConfigEntryKind.Toggle:
                            configPopupRegistration.SetBool(entry.Definition.Key, value is bool flag && flag);
                            break;
                        case ModConfigEntryKind.Slider:
                            configPopupRegistration.SetNumber(entry.Definition.Key, value is double number ? number : 0d);
                            break;
                        default:
                            configPopupRegistration.SetText(entry.Definition.Key, value as string ?? "");
                            break;
                    }

                    applied++;
                }
                catch (Exception exception)
                {
                    SetStatus($"Cannot save {entry.Definition.DisplayName}: {exception.Message}");
                    return;
                }
            }

            configDraft.Clear();
            SetStatus(applied == 0 ? "No changes to apply." : $"Applied {applied} setting(s).");
        }

        // ---- 配置弹窗与草图 ----

        private bool DraftBool(ModConfigEntrySnapshot entry)
            => configDraft.TryGetValue(entry.Definition.Key, out object? value) ? value is bool flag && flag : entry.BoolValue;

        private double DraftNumber(ModConfigEntrySnapshot entry)
            => configDraft.TryGetValue(entry.Definition.Key, out object? value) && value is double number ? number : entry.NumberValue;

        private string DraftText(ModConfigEntrySnapshot entry)
            => configDraft.TryGetValue(entry.Definition.Key, out object? value) && value is string text ? text : entry.TextValue;

        private void SetDraftValue(ModConfigEntrySnapshot entry, object value)
        {
            configDraft[entry.Definition.Key] = value;
            if (SelectedRow != null && configPopupRegistration != null)
                RebuildConfigPopupRows(SelectedRow, configPopupRegistration);
        }

        private bool IsDraftModified(ModConfigEntrySnapshot entry)
        {
            object fallback = entry.Definition.Kind switch
            {
                ModConfigEntryKind.Toggle => entry.BoolValue,
                ModConfigEntryKind.Slider => entry.NumberValue,
                _ => entry.TextValue
            };
            object current = configDraft.TryGetValue(entry.Definition.Key, out object? value) ? value : fallback;
            object declared = entry.Definition.DefaultValue;
            return entry.Definition.Kind switch
            {
                ModConfigEntryKind.Toggle => (current is bool flag && flag) != (declared is bool defaultFlag && defaultFlag),
                ModConfigEntryKind.Slider => Math.Abs(Convert.ToDouble(current) - Convert.ToDouble(declared is double number ? number : 0d)) > 1e-9,
                _ => !string.Equals(current as string ?? "", declared as string ?? "", StringComparison.Ordinal)
            };
        }

        private void OpenConfigPopup(ModMenuRow row)
        {
            IModConfigRegistration? registration = row.HasConfigPage ? config.Find(row.ConfigModId) : null;
            if (registration == null)
            {
                SetStatus("This mod has no config page.");
                return;
            }

            lastConfigLogId = row.ConfigModId;
            configPopupRegistration = registration;
            configDraft.Clear();
            uiSearchInput?.DeactivateInputField();
            info($"[SMA-MENU] config-page mod={row.ConfigModId} entries={registration.Snapshot.Entries.Count}");
            BuildConfigPopup(row, registration);
        }

        private void CloseConfigPopup()
        {
            ClearObjects(uiConfigPopupRows);
            ClearObjects(uiConfigPopupObjects);
            if (uiConfigPopup != null)
            {
                UnityEngine.Object.Destroy(uiConfigPopup);
                uiConfigPopup = null;
            }

            uiConfigPopupContent = null;
            configPopupRegistration = null;
            configDraft.Clear();
        }

        // 弹窗骨架：整窗遮罩 + 居中面板 + 标题行；配置页与重启确认共用同一套外观。
        private GameObject CreatePopupOverlay(List<GameObject> objects, string name, float width, float height, out GameObject overlay, out GameObject titleRow)
        {
            overlay = CreateImageNode(uiWindow!.transform, name, Rgba(0, 0, 0, 0.72f), true);
            SetStretch(overlay.GetComponent<RectTransform>());
            // 窗口本身是垂直布局：遮罩必须退出布局并置于最后，否则会被当成又一行排到窗口底部，
            // 既看不见面板居中，也拦不住下面那些按钮的点击。
            LayoutElement overlayLayout = overlay.AddComponent<LayoutElement>();
            overlayLayout.ignoreLayout = true;
            overlay.transform.SetAsLastSibling();
            objects.Add(overlay);

            GameObject panel = CreateImageNode(overlay.transform, $"{name} Panel", GroupColor, true);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(width, height);
            AddBorder(panel);
            objects.Add(panel);

            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset((int)ModMenuLayout.PanelPadding, (int)ModMenuLayout.PanelPadding, (int)ModMenuLayout.PanelPadding, (int)ModMenuLayout.PanelPadding);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            titleRow = CreateLayoutNode(panel.transform, $"{name} Title Row");
            SetPreferredHeight(titleRow, ModMenuLayout.DetailActionsHeight);
            HorizontalLayoutGroup titleLayout = titleRow.AddComponent<HorizontalLayoutGroup>();
            titleLayout.spacing = 8f;
            titleLayout.childAlignment = TextAnchor.MiddleLeft;
            titleLayout.childControlWidth = true;
            titleLayout.childControlHeight = true;
            titleLayout.childForceExpandWidth = false;
            titleLayout.childForceExpandHeight = true;
            objects.Add(titleRow);
            return panel;
        }

        private void BuildConfigPopup(ModMenuRow row, IModConfigRegistration registration)
        {
            ClearObjects(uiConfigPopupRows);
            ClearObjects(uiConfigPopupObjects);
            if (uiConfigPopup != null)
                UnityEngine.Object.Destroy(uiConfigPopup);

            GameObject panel = CreatePopupOverlay(uiConfigPopupObjects, "Config Popup",
                ModMenuLayout.ContentWidth * 0.75f, ModMenuLayout.ContentHeight * 0.8f, out uiConfigPopup, out GameObject titleRow);

            TextMeshProUGUI title = CreateText(titleRow.transform, "Config Popup Title", $"{row.DisplayName}  -  SETTINGS", 15f, TextAlignmentOptions.Left, Color.white, true);
            SetFlexibleWidth(title.gameObject, 1f);

            CreateButton(titleRow.transform, "Apply Button", "APPLY", ModMenuLayout.ActionButtonWidth, ModMenuLayout.DetailActionsHeight, 12f, false, BoundTextColor, false,
                ApplyDraft, out GameObject applyObject, out _);
            uiConfigPopupObjects.Add(applyObject);
            CreateButton(titleRow.transform, "Ok Button", "OK", ModMenuLayout.ActionButtonWidth, ModMenuLayout.DetailActionsHeight, 12f, true, Color.white, true,
                () => { ApplyDraft(); CloseConfigPopup(); }, out GameObject okObject, out _);
            uiConfigPopupObjects.Add(okObject);
            CreateButton(titleRow.transform, "Cancel Button", "CANCEL", ModMenuLayout.ActionButtonWidth, ModMenuLayout.DetailActionsHeight, 12f, false, BoundTextColor, false,
                CloseConfigPopup, out GameObject cancelObject, out _);
            uiConfigPopupObjects.Add(cancelObject);

            GameObject bodyPanel = CreateScrollArea(panel.transform, "Config Popup Body", out ScrollRect _, out RectTransform content);
            SetFlexibleHeight(bodyPanel, 1f);
            uiConfigPopupObjects.Add(bodyPanel);
            uiConfigPopupContent = content;

            RebuildConfigPopupRows(row, registration);
        }

        private void RebuildConfigPopupRows(ModMenuRow row, IModConfigRegistration registration)
        {
            if (uiConfigPopupContent == null)
                return;

            ClearObjects(uiConfigPopupRows);
            CreateConfigBody(row, registration, uiConfigPopupContent, uiConfigPopupRows);
        }

        // 关窗前的重启确认：磁盘上的启停只有下次启动才生效。
        private void ShowRestartPrompt()
        {
            if (uiRestartPopup != null)
                return;

            GameObject panel = CreatePopupOverlay(uiRestartPopupObjects, "Restart Popup", 560f, 210f, out uiRestartPopup, out GameObject titleRow);

            TextMeshProUGUI title = CreateText(titleRow.transform, "Restart Popup Title", "RESTART REQUIRED", 15f, TextAlignmentOptions.Left, Color.white, true);
            SetFlexibleWidth(title.gameObject, 1f);

            TextMeshProUGUI message = CreateText(panel.transform, "Restart Popup Message", "Enabled and disabled mods load on the next start of Sprocket.", 12f, TextAlignmentOptions.TopLeft, BoundTextColor, false);
            message.enableWordWrapping = true;
            message.overflowMode = TextOverflowModes.Overflow;
            SetFlexibleHeight(message.gameObject, 1f);

            GameObject actions = CreateLayoutNode(panel.transform, "Restart Popup Actions");
            SetPreferredHeight(actions, ModMenuLayout.DetailActionsHeight);
            HorizontalLayoutGroup actionsLayout = actions.AddComponent<HorizontalLayoutGroup>();
            actionsLayout.spacing = 8f;
            actionsLayout.childAlignment = TextAnchor.MiddleRight;
            actionsLayout.childControlWidth = true;
            actionsLayout.childControlHeight = true;
            actionsLayout.childForceExpandWidth = false;
            actionsLayout.childForceExpandHeight = true;
            uiRestartPopupObjects.Add(actions);

            CreateButton(actions.transform, "Restart Later Button", "RESTART LATER", 150f, ModMenuLayout.DetailActionsHeight, 12f, false, BoundTextColor, false,
                () =>
                {
                    CloseRestartPrompt();
                    CloseWindow();
                }, out _, out _);
            CreateButton(actions.transform, "Restart Now Button", "RESTART NOW", 150f, ModMenuLayout.DetailActionsHeight, 12f, true, Color.white, true,
                RestartNow, out _, out _);
        }

        private void CloseRestartPrompt()
        {
            ClearObjects(uiRestartPopupObjects);
            if (uiRestartPopup != null)
            {
                UnityEngine.Object.Destroy(uiRestartPopup);
                uiRestartPopup = null;
            }
        }

        // 重新拉起同一个可执行文件，再退出本进程：新进程的 MelonLoader 按磁盘状态加载模组。
        private void RestartNow()
        {
            string executable = CurrentExecutable();
            if (string.IsNullOrEmpty(executable))
            {
                SetStatus("Cannot locate the game executable; restart Sprocket manually.");
                CloseRestartPrompt();
                CloseWindow();
                return;
            }

            try
            {
                info($"[SMA-MENU] restarting {executable}");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = executable,
                    WorkingDirectory = Path.GetDirectoryName(executable) ?? "",
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                error($"[SMA-MENU] restart failed: {exception}");
                SetStatus($"Restart failed: {exception.Message}");
                CloseRestartPrompt();
                return;
            }

            CloseRestartPrompt();
            CloseWindow();
            Application.Quit();
        }

        private string CurrentExecutable()
        {
            try
            {
                string? path = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return path;
            }
            catch (Exception)
            {
                // 取不到主模块时按目录名约定回退到 <游戏目录>/<目录名>.exe。
            }

            string root = Path.GetDirectoryName(modsDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? "";
            string candidate = string.IsNullOrEmpty(root) ? "" : Path.Combine(root, Path.GetFileName(root) + ".exe");
            return File.Exists(candidate) ? candidate : "";
        }

        private bool IsSelfAssembly(ModMenuRow row)
        {
            string self = typeof(ModMenuWindow).Assembly.Location;
            return !string.IsNullOrEmpty(self) && string.Equals(Path.GetFullPath(row.Location), Path.GetFullPath(self), StringComparison.OrdinalIgnoreCase);
        }

        private void ToggleEnabled(ModMenuRow row)
        {
            string path = row.Location;
            if (string.IsNullOrEmpty(path))
            {
                SetStatus("This mod has no file on disk to toggle.");
                return;
            }

            try
            {
                // 以启用路径为身份：同一个模组启了又禁、禁了又启都落在同一条记录上。
                string identity = ModFileToggle.EnabledPathFor(path);
                restartTracker.Record(identity, row.IsDisabled);

                ModToggleResult result = row.IsDisabled ? ModFileToggle.Enable(path) : ModFileToggle.Disable(path);
                if (!result.Succeeded)
                {
                    SetStatus($"{(row.IsDisabled ? "Enable" : "Disable")} failed: {result.Message}");
                    return;
                }

                bool nowDisabled = !row.IsDisabled;
                restartTracker.Update(identity, nowDisabled);

                if (nowDisabled)
                    info($"[SMA-MENU] disabled {row.DisplayName} -> {result.Path}");
                else
                    info($"[SMA-MENU] enabled {row.DisplayName} -> {result.Path}");

                if (restartTracker.RestartPending)
                    SetStatus($"{row.DisplayName} is now {(nowDisabled ? "disabled" : "enabled")}. Restart Sprocket to apply.");
                else
                    SetStatus($"{row.DisplayName} is back to the state Sprocket started with. No restart needed.");

                RefreshRows();
                RebuildAll();
            }
            catch (Exception exception)
            {
                error($"[SMA-MENU] toggling {row.DisplayName} failed: {exception}");
                SetStatus($"Toggle failed: {exception.Message}");
            }
        }

        // ---- UGUI 原语：从已验证的键位窗口复制，保持同样的视觉与布局约定 ----

        private static GameObject CreateScrollArea(Transform parent, string name, out ScrollRect scroll, out RectTransform content)
        {
            GameObject panel = CreateImageNode(parent, name, TableBackgroundColor, true);
            AddBorder(panel);

            scroll = panel.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 0.2f;

            GameObject viewport = CreateImageNode(panel.transform, "Viewport", TableBackgroundColor, true);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(1f, 1f);
            viewportRect.offsetMax = new Vector2(-(1f + ModMenuLayout.ScrollbarWidth), -1f);
            Mask mask = viewport.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            GameObject contentObject = CreateLayoutNode(viewport.transform, "Content");
            content = contentObject.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            VerticalLayoutGroup contentLayout = contentObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset((int)ModMenuLayout.PanelPadding, (int)ModMenuLayout.PanelPadding, (int)ModMenuLayout.PanelPadding, (int)ModMenuLayout.PanelPadding);
            contentLayout.spacing = 0f;
            contentLayout.childAlignment = TextAnchor.UpperLeft;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;

            ContentSizeFitter fitter = contentObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Scrollbar scrollbar = CreateVerticalScrollbar(panel.transform);
            scroll.viewport = viewportRect;
            scroll.content = content;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scroll.verticalScrollbarSpacing = 0f;
            return panel;
        }

        private static Scrollbar CreateVerticalScrollbar(Transform parent)
        {
            GameObject track = CreateImageNode(parent, "Vertical Scrollbar", WindowColor, true);
            RectTransform trackRect = track.GetComponent<RectTransform>();
            trackRect.anchorMin = new Vector2(1f, 0f);
            trackRect.anchorMax = new Vector2(1f, 1f);
            trackRect.pivot = new Vector2(1f, 0.5f);
            trackRect.anchoredPosition = new Vector2(-1f, 0f);
            trackRect.sizeDelta = new Vector2(ModMenuLayout.ScrollbarWidth, -4f);

            GameObject slidingArea = CreateLayoutNode(track.transform, "Sliding Area");
            SetStretch(slidingArea.GetComponent<RectTransform>());
            GameObject handle = CreateImageNode(slidingArea.transform, "Handle", ScrollbarColor, true);
            SetStretch(handle.GetComponent<RectTransform>());

            Scrollbar scrollbar = track.AddComponent<Scrollbar>();
            scrollbar.targetGraphic = handle.GetComponent<Image>();
            scrollbar.handleRect = handle.GetComponent<RectTransform>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            return scrollbar;
        }

        private static TMP_InputField CreateInputField(Transform parent, string name, string placeholderText, string initialText, float width)
        {
            GameObject field = CreateImageNode(parent, name, SearchSurfaceColor, true);
            AddBorder(field);
            if (width > 0f)
                SetPreferredSize(field, width, ModMenuLayout.InputHeight);

            GameObject textArea = CreateLayoutNode(field.transform, "Text Area");
            RectTransform textAreaRect = textArea.GetComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.offsetMin = new Vector2(ModMenuLayout.InputPadding, 0f);
            textAreaRect.offsetMax = new Vector2(-ModMenuLayout.InputPadding, 0f);
            textArea.AddComponent<RectMask2D>();

            TextMeshProUGUI placeholder = CreateText(textArea.transform, "Placeholder", placeholderText, 12f, TextAlignmentOptions.Left, PlaceholderColor, false);
            SetStretch(placeholder.GetComponent<RectTransform>());

            TextMeshProUGUI text = CreateText(textArea.transform, "Text", initialText, 13f, TextAlignmentOptions.Left, BoundTextColor, false);
            SetStretch(text.GetComponent<RectTransform>());
            text.raycastTarget = true;

            TMP_InputField input = field.AddComponent<TMP_InputField>();
            input.textViewport = textAreaRect;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.targetGraphic = field.GetComponent<Image>();
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.characterLimit = 128;
            input.text = initialText;
            return input;
        }

        private static GameObject CreateLayoutNode(Transform parent, string name)
        {
            GameObject node = new(name);
            node.transform.SetParent(parent, false);
            node.AddComponent<RectTransform>();
            return node;
        }

        private static GameObject CreateImageNode(Transform parent, string name, Color color, bool raycast)
        {
            GameObject node = CreateLayoutNode(parent, name);
            Image image = node.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return node;
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name, string value, float fontSize, TextAlignmentOptions alignment, Color color, bool bold)
        {
            GameObject node = CreateLayoutNode(parent, name);
            TextMeshProUGUI text = node.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            text.characterSpacing = 0f;
            return text;
        }

        // 通用按钮。`primary` 用琥珀强调色（主操作），其余用中性灰底。
        private static Button CreateButton(Transform parent, string name, string label, float width, float height, float fontSize, bool primary, Color textColor, bool bold, Action onClick, out GameObject buttonObject, out TextMeshProUGUI labelText)
        {
            buttonObject = CreateImageNode(parent, name, Color.white, true);
            SetPreferredSize(buttonObject, width, height);

            Image image = buttonObject.GetComponent<Image>();
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = primary ? AccentColor : ButtonColor;
            colors.highlightedColor = primary ? AccentHoverColor : ButtonHoverColor;
            colors.pressedColor = primary ? AccentPressedColor : ButtonPressedColor;
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = ButtonDisabledColor;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener((UnityAction)onClick);

            // 强调色按钮的描边与底色同色：深一档的边框看起来像渲染残影（键位窗口同样是这么处理的）。
            Outline outline = buttonObject.AddComponent<Outline>();
            outline.effectColor = primary ? AccentColor : ButtonBorderColor;
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = false;

            labelText = CreateText(buttonObject.transform, "Label", label, fontSize, TextAlignmentOptions.Center, textColor, bold);
            SetStretch(labelText.GetComponent<RectTransform>());
            return button;
        }

        private static void AddBorder(GameObject target) => AddBorder(target, BorderColor);

        private static void AddBorder(GameObject target, Color color)
        {
            Outline outline = target.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = false;
        }

        private static void SetStretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetPreferredSize(GameObject target, float width, float height)
        {
            LayoutElement element = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
            element.minWidth = width;
            element.preferredWidth = width;
            element.flexibleWidth = 0f;
            element.minHeight = height;
            element.preferredHeight = height;
            element.flexibleHeight = 0f;
        }

        private static void SetPreferredWidth(GameObject target, float width)
        {
            LayoutElement element = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
            element.minWidth = width;
            element.preferredWidth = width;
            element.flexibleWidth = 0f;
        }

        private static void SetPreferredHeight(GameObject target, float height)
        {
            LayoutElement element = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            element.flexibleHeight = 0f;
        }

        private static void SetFlexibleWidth(GameObject target, float weight)
        {
            LayoutElement element = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
            element.minWidth = 0f;
            element.preferredWidth = 0f;
            element.flexibleWidth = weight;
        }

        private static void SetFlexibleHeight(GameObject target, float weight)
        {
            LayoutElement element = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
            element.minHeight = 0f;
            element.preferredHeight = 0f;
            element.flexibleHeight = weight;
        }

        private static Color Rgb(int red, int green, int blue)
            => new(red / 255f, green / 255f, blue / 255f, 1f);

        private static Color Rgba(int red, int green, int blue, float alpha)
            => new(red / 255f, green / 255f, blue / 255f, alpha);
    }
}
