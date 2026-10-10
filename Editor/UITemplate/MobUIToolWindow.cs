using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Presets;
using UnityEngine;
using UnityEngine.UI;

namespace Mob404.Common.UITemplate.Editor
{
    /// <summary>
    /// Cửa sổ quản lý loại, hai tab:
    /// Button — Image có Button, preset Rect Button/Image + khối Text (Text là khoá → Rect Text, Outline/Shadow);
    /// Image — Image dùng sprite nguồn mà chưa thuộc loại Button, preset Rect Image/Image.
    /// Chỉ ghi xuống file khi bấm Scan &amp; Attach / Apply / Remove; Preview không ghi gì.
    /// </summary>
    public class MobUIToolWindow : EditorWindow
    {
        private const int TAB_BUTTON = 0;
        private const int TAB_IMAGE = 1;
        private static readonly string[] TAB_NAMES = { "Button", "Image" };

        private static bool s_IndexStale = true;

        private MobUIToolConfig _config;
        private bool _indexed;
        private int _tab;
        private readonly int[] _selected = new int[2];
        private string _search = "";
        private Vector2 _listScroll;
        private Vector2 _detailScroll;
        private bool _showLinked;
        private bool _showReport = true;
        private string _message;
        private MobUIToolReport _report;
        private double _lastClick;
        private string _lastClickPath;

        [MenuItem("Tools/Mob404/MobUITool")]
        public static void Open() => Open(null);

        /// <summary>Mở cửa sổ, chọn sẵn loại typeId ở tab Button (imageTab = false) hoặc tab Image.</summary>
        public static void Open(string typeId, bool imageTab = false)
        {
            var window = GetWindow<MobUIToolWindow>("MobUITool");
            window.minSize = new Vector2(680, 460);
            if (!string.IsNullOrEmpty(typeId)) window._Select(typeId, imageTab ? TAB_IMAGE : TAB_BUTTON);
        }

        private void OnEnable() => EditorApplication.projectChanged += _OnProjectChanged;

        private void OnDisable() => EditorApplication.projectChanged -= _OnProjectChanged;

        private static void _OnProjectChanged() => s_IndexStale = true;

        private void OnFocus()
        {
            if (s_IndexStale) _RefreshIndex();
        }

        private void _RefreshIndex()
        {
            if (_config == null) _config = MobUIToolConfig.LoadOrCreate();
            MobUIToolScanner.RefreshIndex(_config);
            s_IndexStale = false;
            _indexed = true;
            Repaint();
        }

        private void _Select(string typeId, int tab)
        {
            if (_config == null) _config = MobUIToolConfig.LoadOrCreate();
            _tab = tab;
            _search = "";
            var list = _List;
            for (int i = 0; i < list.Count; i++)
            {
                if (((GGTypeBase)list[i]).id != typeId) continue;
                _selected[tab] = i;
                return;
            }
        }

        private void OnGUI()
        {
            if (_config == null) _config = MobUIToolConfig.LoadOrCreate();
            // Dựng index một lần khi mở cửa sổ; sau đó chỉ khi cửa sổ được focus lại sau khi project đổi (OnFocus) hoặc sau mỗi thao tác.
            if (!_indexed && Event.current.type == EventType.Layout) _RefreshIndex();

            _DrawToolbar();
            int tab = GUILayout.Toolbar(_tab, TAB_NAMES);
            if (tab != _tab)
            {
                _tab = tab;
                GUI.FocusControl(null);
            }
            if (_InPlayMode) EditorGUILayout.HelpBox("Đang Play mode — Scan, Apply và Remove bị khoá.", MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            _DrawTypeList();
            _DrawDetail();
            EditorGUILayout.EndHorizontal();

            _DrawStatus();
        }

        #region Toolbar & status

        private void _DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            using (new EditorGUI.DisabledScope(_InPlayMode))
            {
                if (GUILayout.Button(new GUIContent("Scan & Attach", "Quét chung cả hai tab: gắn marker cho Image dùng sprite nguồn (không áp style). Ghi file, không Undo."), EditorStyles.toolbarButton))
                    _Scan();
                if (GUILayout.Button(new GUIContent("Refresh", "Dựng lại danh sách liên kết từ YAML — chỉ đọc, không ghi gì"), EditorStyles.toolbarButton))
                {
                    _RefreshIndex();
                    _message = "Đã refresh danh sách liên kết.";
                }
                if (GUILayout.Button(new GUIContent("Remove orphan markers", "Gỡ marker có typeId trống hoặc trỏ tới loại đã bị xoá"), EditorStyles.toolbarButton))
                    _RemoveOrphans();
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Select config", EditorStyles.toolbarButton))
            {
                Selection.activeObject = _config;
                EditorGUIUtility.PingObject(_config);
            }
            EditorGUILayout.EndHorizontal();
        }

        private static bool _InPlayMode => EditorApplication.isPlayingOrWillChangePlaymode;

        private void _Scan()
        {
            if (!EditorUtility.DisplayDialog("MobUITool — Scan & Attach",
                    "Tool sẽ mở từng prefab/scene có dùng sprite nguồn và gắn marker (chưa áp style):\n" +
                    "• GGButton nếu Image có Button và sprite thuộc một loại Button;\n" +
                    "• GGImage cho các Image còn lại có sprite thuộc một loại Image.\n" +
                    "Marker mồ côi (loại đã bị xoá) được gắn lại như chưa có.\n\n" +
                    "Không Undo được — nên commit git trước.", "Scan", "Cancel"))
                return;
            if (!MobUIToolAssetUtil.SaveOpenEditorsIfUserWantsTo()) return;
            _Finish(MobUIToolScanner.Scan(_config));
        }

        private void _RemoveOrphans()
        {
            if (!EditorUtility.DisplayDialog("MobUITool — Remove orphan markers",
                    "Gỡ mọi marker GGButton/GGImage có typeId trống hoặc trỏ tới loại không còn trong config.\n\nKhông Undo được.",
                    "Remove", "Cancel"))
                return;
            if (!MobUIToolAssetUtil.SaveOpenEditorsIfUserWantsTo()) return;
            _Finish(MobUIToolBatchApply.RemoveOrphans(_config));
        }

        /// <summary>Ghi nhận report của một thao tác, refresh danh sách nếu có ghi file.</summary>
        private void _Finish(MobUIToolReport report)
        {
            _report = report;
            _showReport = true;
            _message = report.Headline;
            Debug.Log("[MobUITool] " + _message);
            foreach (var w in report.warnings) Debug.LogWarning("[MobUITool] " + w);
            if (!report.dryRun) _RefreshIndex();
        }

        private void _DrawStatus()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(string.IsNullOrEmpty(_message) ? "Ready" : _message, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Type list

        private bool _IsImageTab => _tab == TAB_IMAGE;

        /// <summary>Danh sách loại của tab đang mở (List&lt;GGButtonType&gt; hoặc List&lt;GGImageType&gt;).</summary>
        private IList _List => _IsImageTab ? (IList)_config.imageTypes : _config.types;

        private string _ListProperty => _IsImageTab ? nameof(MobUIToolConfig.imageTypes) : nameof(MobUIToolConfig.types);

        private string _Unit => _IsImageTab ? "image" : "button";

        private void _DrawTypeList()
        {
            var list = _List;
            EditorGUILayout.BeginVertical(GUILayout.Width(230));
            _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, EditorStyles.helpBox);
            for (int i = 0; i < list.Count; i++)
            {
                var t = (GGTypeBase)list[i];
                string name = string.IsNullOrEmpty(t.name) ? "(no name)" : t.name;
                if (!string.IsNullOrEmpty(_search) && name.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                EditorGUILayout.BeginHorizontal();
                var thumb = _Thumbnail(t.sourceSprite);
                GUILayout.Label(thumb, GUILayout.Width(20), GUILayout.Height(20));
                bool on = GUILayout.Toggle(i == _selected[_tab], $"{name}  [{t.TotalCount}]", "Button", GUILayout.Height(20));
                EditorGUILayout.EndHorizontal();
                if (on && i != _selected[_tab])
                {
                    _selected[_tab] = i;
                    _report = null;
                    GUI.FocusControl(null);
                }
            }
            EditorGUILayout.EndScrollView();
            if (AssetPreview.IsLoadingAssetPreviews()) Repaint();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("+ Add", "Thêm loại mới với sprite đang chọn trong Project")))
                _AddType();
            using (new EditorGUI.DisabledScope(_Current == null))
            {
                if (GUILayout.Button(new GUIContent("Duplicate", "Nhân bản loại (giữ preset, bỏ sprite nguồn để không trùng)")))
                    _DuplicateCurrent();
                if (GUILayout.Button("− Delete") && EditorUtility.DisplayDialog("MobUITool — Delete type",
                        $"Xoá loại '{_Current.name}'? Marker trên {_Unit} vẫn giữ và thành mồ côi — dùng \"Remove markers\" trước nếu muốn gỡ sạch.",
                        "Delete", "Cancel"))
                    _RemoveCurrent();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private static Texture _Thumbnail(Sprite sprite)
        {
            if (sprite == null) return EditorGUIUtility.IconContent("console.warnicon.sml").image;
            var preview = AssetPreview.GetAssetPreview(sprite);
            return preview != null ? preview : AssetPreview.GetMiniThumbnail(sprite);
        }

        private GGTypeBase _Current
        {
            get
            {
                var list = _List;
                int i = _selected[_tab];
                return i >= 0 && i < list.Count ? (GGTypeBase)list[i] : null;
            }
        }

        private void _AddType()
        {
            GGTypeBase type = _IsImageTab ? (GGTypeBase)new GGImageType() : new GGButtonType();
            type.id = System.Guid.NewGuid().ToString("N").Substring(0, 8);
            type.sourceSprite = _FirstSelectedSprite();
            type.name = _DefaultName(type.sourceSprite);
            _List.Add(type);
            _config.Save();
            _selected[_tab] = _List.Count - 1;
            _search = "";
        }

        /// <summary>Btn_Yellow / Img_PopupBg — tên loại theo sprite, bỏ tiền tố btn/button/img thừa của tên file.</summary>
        private string _DefaultName(Sprite sprite)
        {
            string prefix = _IsImageTab ? "Img_" : "Btn_";
            if (sprite == null) return prefix + "New";
            string name = sprite.name;
            foreach (var p in new[] { "button_", "btn_", "img_", "image_" })
                if (name.StartsWith(p, System.StringComparison.OrdinalIgnoreCase)) { name = name.Substring(p.Length); break; }
            return prefix + MobUIToolPresetFactory.Pascal(name);
        }

        private void _DuplicateCurrent()
        {
            var source = _Current;
            var copy = (GGTypeBase)System.Activator.CreateInstance(source.GetType());
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(source), copy);
            copy.id = System.Guid.NewGuid().ToString("N").Substring(0, 8);
            copy.name = source.name + "_Copy";
            copy.sourceSprite = null;
            _List.Insert(_selected[_tab] + 1, copy);
            _config.Save();
            _selected[_tab]++;
        }

        private void _RemoveCurrent()
        {
            var list = _List;
            list.RemoveAt(_selected[_tab]);
            _config.Save();
            _selected[_tab] = Mathf.Clamp(_selected[_tab] - 1, 0, list.Count - 1);
            _report = null;
        }

        /// <summary>Sprite đầu tiên đang chọn trong Project (chọn texture thì lấy sprite đầu tiên bên trong). Không có thì null.</summary>
        private static Sprite _FirstSelectedSprite()
        {
            foreach (var obj in Selection.objects)
            {
                if (obj is Sprite s) return s;
                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;
                foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                    if (o is Sprite sprite) return sprite;
            }
            return null;
        }

        #endregion

        #region Detail

        private void _DrawDetail()
        {
            EditorGUILayout.BeginVertical();
            var type = _Current;
            if (type == null)
            {
                EditorGUILayout.HelpBox($"Chưa có loại {TAB_NAMES[_tab]} nào. Chọn sprite trong Project rồi bấm \"+ Add\".", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
            var so = new SerializedObject(_config);
            var tp = so.FindProperty(_ListProperty).GetArrayElementAtIndex(_selected[_tab]);

            _DrawIdentity(so, tp, type);
            EditorGUILayout.Space();
            _DrawStyle(so, tp, type);
            EditorGUILayout.Space();
            _DrawActions(type);
            EditorGUILayout.Space();
            _DrawReport();
            _DrawLinkedAssets(type);

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        /// <summary>Tên + sprite nguồn. Đổi ở đây không tự áp — cần Scan lại.</summary>
        private void _DrawIdentity(SerializedObject so, SerializedProperty tp, GGTypeBase type)
        {
            EditorGUILayout.LabelField("Identity", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.DelayedTextField(tp.FindPropertyRelative("name"), new GUIContent("Name"));
            EditorGUILayout.LabelField("Id", type.id, EditorStyles.miniLabel);
            EditorGUILayout.PropertyField(tp.FindPropertyRelative("sourceSprite"), new GUIContent("Source sprite", "Image dùng sprite này sẽ được gắn vào loại khi Scan & Attach"));
            if (EditorGUI.EndChangeCheck() && so.ApplyModifiedProperties()) _config.Save();

            if (type.sourceSprite == null)
                EditorGUILayout.HelpBox("Chưa có sprite nguồn — Scan & Attach sẽ không gắn được đối tượng mới vào loại này.", MessageType.Warning);
            foreach (GGTypeBase other in _List)
            {
                if (other == type || other.sourceSprite == null || other.sourceSprite != type.sourceSprite) continue;
                EditorGUILayout.HelpBox($"Sprite nguồn trùng với loại '{other.name}' — chỉ một loại nhận được đối tượng khi Scan.", MessageType.Warning);
                break;
            }
        }

        /// <summary>
        /// Preset của loại, chia khối theo GameObject nhận preset: khối Button (hoặc Image) cho GameObject có Image,
        /// khối Text cho Text con. Chỉ lưu vào config — Apply mới ghi xuống prefab/scene.
        /// </summary>
        private void _DrawStyle(SerializedObject so, SerializedProperty tp, GGTypeBase type)
        {
            EditorGUILayout.LabelField("Style", EditorStyles.boldLabel);
            var buttonType = type as GGButtonType;
            string owner = buttonType != null ? "Button" : "Image";

            _BeginGroup(owner, buttonType != null ? "GameObject có Image + Button" : "GameObject có Image");
            _PresetRow(so, tp, nameof(GGTypeBase.rectTransformPreset), "Rect " + owner,
                $"File .preset của RectTransform — áp lên RectTransform của {owner}", () => type.rectTransformPreset,
                MobUIToolPresetFactory.Slot.Rect, typeof(RectTransform));
            _PresetRow(so, tp, nameof(GGTypeBase.imagePreset), "Image",
                "File .preset của Image", () => type.imagePreset, MobUIToolPresetFactory.Slot.Image, typeof(Image));
            _EndGroup();

            if (buttonType == null) return;

            // Text preset là khoá: chỉ khi có Text preset mới hiện (và áp) Rect Text + Outline/Shadow.
            _BeginGroup("Text", "Text con của button. Gắn Text preset để mở Rect Text và Outline/Shadow");
            _PresetRow(so, tp, nameof(GGButtonType.textPreset), "Text",
                "File .preset của Text (không bao giờ ghi nội dung chữ)", () => buttonType.textPreset,
                MobUIToolPresetFactory.Slot.Text, typeof(Text));
            if (MobUIToolApplier.HasTextKey(buttonType))
            {
                _PresetRow(so, tp, nameof(GGButtonType.textRectTransformPreset), "Rect Text",
                    "File .preset của RectTransform — áp lên RectTransform của Text", () => buttonType.textRectTransformPreset,
                    MobUIToolPresetFactory.Slot.TextRect, typeof(RectTransform));
                _PresetRow(so, tp, nameof(GGButtonType.effectPreset), "Outline / Shadow",
                    "File .preset của Outline hoặc Shadow — Text giữ đúng 1 component kiểu đó, xoá kiểu còn lại. Để trống = xoá cả hai",
                    () => buttonType.effectPreset, MobUIToolPresetFactory.Slot.Effect, typeof(Outline), typeof(Shadow));
            }
            _EndGroup();
        }

        private static void _BeginGroup(string title, string hint)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(new GUIContent(title, hint), EditorStyles.boldLabel);
        }

        private static void _EndGroup()
        {
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        /// <summary>
        /// Một ô preset: [kéo thả / chọn] [▾ chọn nhanh preset đúng kiểu] [+ tạo từ object đang chọn],
        /// cảnh báo ngay bên dưới khi cần (Missing / sai kiểu / rỗng / Rect còn vị trí).
        /// </summary>
        private void _PresetRow(SerializedObject so, SerializedProperty tp, string field, string label, string tooltip,
            System.Func<Preset> current, MobUIToolPresetFactory.Slot slot, params System.Type[] componentTypes)
        {
            string path = tp.FindPropertyRelative(field).propertyPath;
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(tp.FindPropertyRelative(field), new GUIContent(label, tooltip + ". Kéo file .preset vào đây."));
            if (EditorGUI.EndChangeCheck() && so.ApplyModifiedProperties()) _config.Save();

            if (GUILayout.Button(new GUIContent("▾", "Chọn nhanh trong các preset đúng kiểu của ô này"), EditorStyles.miniButton, GUILayout.Width(22)))
                _ShowPresetMenu(path, componentTypes);
            if (GUILayout.Button(new GUIContent("+", "Tạo preset từ object đang chọn trong Hierarchy (tự đặt tên, lưu vào Presets/) và gắn vào ô này"), EditorStyles.miniButton, GUILayout.Width(22)))
            {
                var created = MobUIToolPresetFactory.CreateFromSelection(_config, slot, _IsImageTab, out _message);
                if (created != null)
                {
                    _SetPreset(path, created);
                    EditorGUIUtility.PingObject(created);
                }
            }
            EditorGUILayout.EndHorizontal();

            _DrawPresetInfo(current(), label, componentTypes);
        }

        private void _ShowPresetMenu(string propertyPath, System.Type[] componentTypes)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("(None)"), false, () => _SetPreset(propertyPath, null));
            menu.AddSeparator("");
            string root = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(_config)).Replace('\\', '/') + "/Presets/";
            int count = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Preset"))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var preset = AssetDatabase.LoadAssetAtPath<Preset>(assetPath);
                bool match = false;
                foreach (var t in componentTypes)
                    if (MobUIToolApplier.IsPresetFor(preset, t)) match = true;
                if (!match) continue;
                string label = assetPath.StartsWith(root)
                    ? assetPath.Substring(root.Length, assetPath.Length - root.Length - ".preset".Length)
                    : "Other/" + System.IO.Path.GetFileNameWithoutExtension(assetPath);
                menu.AddItem(new GUIContent(label), false, () => _SetPreset(propertyPath, preset));
                count++;
            }
            if (count == 0) menu.AddDisabledItem(new GUIContent("Chưa có preset phù hợp — bấm + để tạo"));
            menu.ShowAsContext();
        }

        private void _SetPreset(string propertyPath, Preset preset)
        {
            var so = new SerializedObject(_config);
            so.FindProperty(propertyPath).objectReferenceValue = preset;
            if (so.ApplyModifiedProperties()) _config.Save();
            Repaint();
        }

        /// <summary>Báo preset Missing / sai kiểu / rỗng / Rect còn vị trí. Ô trống hoặc preset hợp lệ thì không hiện gì.</summary>
        private static void _DrawPresetInfo(Preset preset, string label, System.Type[] componentTypes)
        {
            if (MobUIToolApplier.IsMissing(preset))
            {
                EditorGUILayout.HelpBox($"{label}: preset bị mất (Missing) — sẽ bỏ qua.", MessageType.Error);
                return;
            }
            if (preset == null) return;

            bool match = false;
            foreach (var t in componentTypes)
                if (MobUIToolApplier.IsPresetFor(preset, t)) match = true;
            if (!match)
            {
                var names = new List<string>();
                foreach (var t in componentTypes) names.Add(t.Name);
                EditorGUILayout.HelpBox($"{label}: preset của '{preset.GetTargetFullTypeName()}', " +
                                        $"không phải {string.Join(" / ", names)} — sẽ không được áp.", MessageType.Error);
                return;
            }

            if (MobUIToolApplier.AppliedPaths(preset).Count == 0)
            {
                EditorGUILayout.HelpBox($"{label}: preset không còn thuộc tính nào để áp (đã exclude hết).", MessageType.Warning);
                return;
            }

            if (MobUIToolApplier.IsPresetFor(preset, typeof(RectTransform)) && MobUIToolApplier.TouchesPosition(preset))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.HelpBox($"{label}: preset còn áp vị trí — Apply sẽ dời mọi đối tượng về cùng một chỗ.", MessageType.Warning);
                if (GUILayout.Button("Exclude position", GUILayout.Width(110), GUILayout.Height(38)))
                    MobUIToolApplier.ExcludePosition(preset);
                EditorGUILayout.EndHorizontal();
            }
        }

        /// <summary>Preview (không ghi) / Apply (ghi) / Remove markers của loại đang chọn.</summary>
        private void _DrawActions(GGTypeBase type)
        {
            var buttonType = type as GGButtonType;
            bool canApply = (buttonType != null && MobUIToolApplier.HasTextKey(buttonType)) ||
                            MobUIToolApplier.HasValues(type.rectTransformPreset, typeof(RectTransform)) ||
                            MobUIToolApplier.HasValues(type.imagePreset, typeof(Image));

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!canApply || type.linkedAssets.Count == 0))
            {
                if (GUILayout.Button(new GUIContent("Preview", "Chạy thử: đếm file/đối tượng sẽ đổi và cảnh báo — không sửa, không lưu gì"), GUILayout.Height(28), GUILayout.Width(90)))
                    _Finish(MobUIToolBatchApply.Apply(type, true));
            }
            using (new EditorGUI.DisabledScope(_InPlayMode || !canApply || type.linkedAssets.Count == 0))
            {
                if (GUILayout.Button(new GUIContent($"Apply ({type.linkedAssets.Count} files, {type.TotalCount} {_Unit})",
                        "Ghi style vào mọi prefab/scene liên kết (kèm prefab/scene chứa chúng). Không Undo được."), GUILayout.Height(28)))
                    _Apply(type);
            }
            using (new EditorGUI.DisabledScope(_InPlayMode || type.linkedAssets.Count == 0))
            {
                if (GUILayout.Button(new GUIContent("Remove markers", "Gỡ mọi marker của loại này khỏi prefab/scene (không đổi style)"), GUILayout.Height(28), GUILayout.Width(115)) &&
                    EditorUtility.DisplayDialog("MobUITool — Remove markers",
                        $"Gỡ marker của loại '{type.name}' khỏi {type.linkedAssets.Count} file? Style đã áp vẫn giữ nguyên.\n\nKhông Undo được.",
                        "Remove", "Cancel") &&
                    MobUIToolAssetUtil.SaveOpenEditorsIfUserWantsTo())
                    _Finish(MobUIToolBatchApply.RemoveMarkers(type));
            }
            EditorGUILayout.EndHorizontal();
        }

        private void _Apply(GGTypeBase type)
        {
            if (!MobUIToolAssetUtil.SaveOpenEditorsIfUserWantsTo()) return;

            var direct = MobUIToolScanner.FilesOfType(type);
            var files = MobUIToolAssetUtil.SortByDependency(MobUIToolAssetUtil.WithParents(direct));

            string removeNote = type is GGButtonType bt && MobUIToolApplier.HasTextKey(bt) && MobUIToolApplier.IsEmpty(bt.effectPreset)
                ? "\n\nÔ Outline / Shadow đang trống → sẽ XOÁ Outline và Shadow khỏi Text của mọi button thuộc loại."
                : "";
            var dirty = MobUIToolAssetUtil.GitDirty(files);
            string gitNote = dirty == null
                ? "\n\n(Không kiểm được git status.)"
                : dirty.Count > 0
                    ? $"\n\n⚠ {dirty.Count} file đang có thay đổi CHƯA COMMIT — sau khi Apply sẽ khó tách thay đổi của tool:\n• " +
                      string.Join("\n• ", dirty.GetRange(0, Mathf.Min(8, dirty.Count))) + (dirty.Count > 8 ? $"\n… và {dirty.Count - 8} file khác" : "")
                    : "";

            if (!EditorUtility.DisplayDialog("MobUITool — Apply",
                    $"Ghi style '{type.name}' vào {files.Count} prefab/scene ({direct.Count} chứa marker + {files.Count - direct.Count} chứa instance lồng nhau)?" +
                    $"{removeNote}{gitNote}\n\nKhông Undo được.",
                    dirty != null && dirty.Count > 0 ? "Apply anyway" : "Apply", "Cancel"))
                return;

            _Finish(MobUIToolBatchApply.Apply(type, false, files));
        }

        private void _DrawReport()
        {
            if (_report == null) return;
            _showReport = EditorGUILayout.Foldout(_showReport, "Last result: " + _report.Headline, true);
            if (!_showReport) return;

            EditorGUI.indentLevel++;
            foreach (var w in _report.warnings) EditorGUILayout.LabelField("⚠ " + w, EditorStyles.wordWrappedMiniLabel);
            int shown = 0;
            foreach (var e in _report.entries)
            {
                if (!e.Notable) continue;
                if (++shown > 200)
                {
                    EditorGUILayout.LabelField("… (xem thêm trong Console)", EditorStyles.miniLabel);
                    break;
                }
                string status = e.error != null ? "error: " + e.error
                    : e.result == MobUIToolAssetUtil.Result.Skipped ? "skipped: " + e.skipReason
                    : e.result == MobUIToolAssetUtil.Result.Changed ? $"{e.objects} {(_report.dryRun ? "sẽ đổi" : "đã đổi")}"
                    : "không đổi";
                _AssetRow(e.path, status);
                foreach (var w in e.warnings) EditorGUILayout.LabelField("    ⚠ " + w, EditorStyles.wordWrappedMiniLabel);
            }
            if (shown == 0 && _report.warnings.Count == 0) EditorGUILayout.LabelField("Không có file nào đổi.", EditorStyles.miniLabel);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space();
        }

        private void _DrawLinkedAssets(GGTypeBase type)
        {
            _showLinked = EditorGUILayout.Foldout(_showLinked,
                $"Linked: {type.linkedAssets.Count} files, {type.TotalCount} {_Unit}", true);
            if (!_showLinked) return;

            if (type.linkedAssets.Count == 0)
            {
                EditorGUILayout.HelpBox("Chưa có. Bấm \"Scan & Attach\".", MessageType.Info);
                return;
            }
            foreach (var asset in type.linkedAssets) _AssetRow(asset.path, asset.count.ToString());
        }

        /// <summary>Một dòng file: click = ping, double-click = mở.</summary>
        private void _AssetRow(string path, string status)
        {
            EditorGUILayout.BeginHorizontal();
            var content = new GUIContent(path, AssetDatabase.GetCachedIcon(path), "Click: ping — Double-click: mở");
            var rect = GUILayoutUtility.GetRect(content, EditorStyles.label, GUILayout.Height(18), GUILayout.ExpandWidth(true));
            if (GUI.Button(rect, content, EditorStyles.label))
            {
                var obj = AssetDatabase.LoadMainAssetAtPath(path);
                if (Event.current.clickCount > 1 || EditorApplication.timeSinceStartup - _lastClick < 0.35 && _lastClickPath == path)
                    AssetDatabase.OpenAsset(obj);
                else
                    EditorGUIUtility.PingObject(obj);
                _lastClick = EditorApplication.timeSinceStartup;
                _lastClickPath = path;
            }
            GUILayout.Label(status, EditorStyles.miniLabel, GUILayout.MaxWidth(220));
            EditorGUILayout.EndHorizontal();
        }

        #endregion
    }
}
