using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Presets;
using UnityEngine;
using UnityEngine.UI;

namespace Mob404.Common.UITemplate.Editor
{
    /// <summary>
    /// Cửa sổ quản lý loại, ba tab:
    /// Button — Image có Button, preset Rect Button/Image + khối Text (Text là khoá → Rect Text, Outline/Shadow);
    /// Image — Image dùng sprite nguồn mà chưa thuộc loại Button, preset Rect Image/Image;
    /// Tab — tab có Button + hai nửa ON/OFF (Image + Title): Sync structure + preset gốc/ON/OFF + I2 Localize;
    /// Block — khối ghép (vd ô lực chiến): preset gốc + từng phần con theo đường dẫn, Sync dời/đổi tên node theo alias.
    /// Chỉ ghi xuống file khi bấm Scan &amp; Attach / Apply / Remove; Preview không ghi gì.
    /// </summary>
    public class MobUIToolWindow : EditorWindow
    {
        private const int TAB_BUTTON = 0;
        private const int TAB_IMAGE = 1;
        private const int TAB_TAB = 2;
        private const int TAB_BLOCK = 3;
        private static readonly string[] TAB_NAMES = { "Button", "Image", "Tab", "Block" };

        private static bool s_IndexStale = true;

        private MobUIToolConfig _config;
        private bool _indexed;
        private int _tab;
        private readonly int[] _selected = new int[4];
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

        /// <summary>Mở cửa sổ ở tab Tab, chọn sẵn loại typeId.</summary>
        public static void OpenTab(string typeId)
        {
            var window = GetWindow<MobUIToolWindow>("MobUITool");
            window.minSize = new Vector2(680, 460);
            window._Select(typeId, TAB_TAB);
        }

        /// <summary>Mở cửa sổ ở tab Block, chọn sẵn loại typeId.</summary>
        public static void OpenBlock(string typeId)
        {
            var window = GetWindow<MobUIToolWindow>("MobUITool");
            window.minSize = new Vector2(680, 460);
            window._Select(typeId, TAB_BLOCK);
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
                    "• GGImage cho các Image còn lại có sprite thuộc một loại Image;\n" +
                    "• GGTab lên tab (GameObject có Button) chứa Image dùng sprite của một loại Tab.\n" +
                    "Marker mồ côi (loại đã bị xoá) được gắn lại như chưa có.\n\n" +
                    "Không Undo được — nên commit git trước.", "Scan", "Cancel"))
                return;
            if (!MobUIToolAssetUtil.SaveOpenEditorsIfUserWantsTo()) return;
            _Finish(MobUIToolScanner.Scan(_config));
        }

        private void _RemoveOrphans()
        {
            if (!EditorUtility.DisplayDialog("MobUITool — Remove orphan markers",
                    "Gỡ mọi marker GGButton/GGImage/GGTab có typeId trống hoặc trỏ tới loại không còn trong config.\n\nKhông Undo được.",
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

        private bool _IsTabTab => _tab == TAB_TAB;

        private bool _IsBlockTab => _tab == TAB_BLOCK;

        /// <summary>Danh sách loại của tab đang mở (List&lt;GGButtonType&gt; / List&lt;GGImageType&gt; / List&lt;GGTabType&gt;).</summary>
        private IList _List => _IsBlockTab ? (IList)_config.blockTypes : _IsTabTab ? _config.tabTypes : _IsImageTab ? _config.imageTypes : _config.types;

        private string _ListProperty => _IsBlockTab ? nameof(MobUIToolConfig.blockTypes) : _IsTabTab ? nameof(MobUIToolConfig.tabTypes)
            : _IsImageTab ? nameof(MobUIToolConfig.imageTypes) : nameof(MobUIToolConfig.types);

        private string _Unit => _IsBlockTab ? "block" : _IsTabTab ? "tab" : _IsImageTab ? "image" : "button";

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
            if (GUILayout.Button(new GUIContent("+ Add", _IsBlockTab
                    ? "Tạo loại từ khối mẫu đang chọn trong Hierarchy: preset cho gốc + mọi node con, gắn GGBlock lên mẫu"
                    : "Thêm loại mới với sprite đang chọn trong Project")))
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
            if (_IsBlockTab)
            {
                var created = MobUIToolBlockApplier.CreateTypeFrom(_config, Selection.activeGameObject, null, out _message);
                if (created == null) return;
                _selected[_tab] = _List.Count - 1;
                _search = "";
                Debug.Log("[MobUITool] " + _message);
                GUIUtility.ExitGUI();
            }
            GGTypeBase type = _IsTabTab ? new GGTabType() : _IsImageTab ? (GGTypeBase)new GGImageType() : new GGButtonType();
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
            string prefix = _IsBlockTab ? "Block_" : _IsTabTab ? "Tab_" : _IsImageTab ? "Img_" : "Btn_";
            if (sprite == null) return prefix + "New";
            string name = sprite.name;
            foreach (var p in new[] { "button_", "btn_", "img_", "image_", "tab_" })
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
                EditorGUILayout.HelpBox(_IsBlockTab
                    ? "Chưa có loại Block nào. Chọn GameObject gốc của khối mẫu trong Hierarchy (vd FieldPower) rồi bấm \"+ Add\"."
                    : $"Chưa có loại {TAB_NAMES[_tab]} nào. Chọn sprite trong Project rồi bấm \"+ Add\".", MessageType.Info);
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
            if (!(type is GGBlockType))
                EditorGUILayout.PropertyField(tp.FindPropertyRelative("sourceSprite"), new GUIContent("Scan sprite (tuỳ chọn)",
                    "Chỉ dùng để Scan & Attach tìm đối tượng CHƯA có marker. Đối tượng đã gắn đi theo loại (Name/Id), không đọc sprite này."));
            if (EditorGUI.EndChangeCheck() && so.ApplyModifiedProperties()) _config.Save();

            if (type is GGBlockType)
            {
                EditorGUILayout.HelpBox("Loại Block không Scan theo sprite — chọn gốc các khối trong Hierarchy rồi Attach to selection.", MessageType.None);
                return;
            }

            if (type is GGButtonType costType && MobUIToolApplier.HasCostKey(costType))
            {
                EditorGUILayout.HelpBox("Loại có khối Cost — Scan & Attach không gắn tự động. Gắn marker GGButton bằng tay rồi chọn loại này.", MessageType.Info);
                return;
            }
            // Loại đã có đối tượng liên kết thì không cần sprite quét.
            if (type.sourceSprite == null && type.linkedAssets.Count == 0)
                EditorGUILayout.HelpBox("Chưa có scan sprite và chưa liên kết đối tượng nào — gắn bằng Attach to selection, hoặc đặt scan sprite rồi Scan & Attach.", MessageType.Info);

            // Trùng khoá quét (sprite nguồn hoặc sprite trong Image preset) không gây gắn nhầm: Scan bỏ qua đối tượng mơ hồ.
            var mine = _ScanSprites(type);
            foreach (GGTypeBase other in _List)
            {
                if (other == type || other is GGButtonType ob && MobUIToolApplier.HasCostKey(ob)) continue;
                var shared = _ScanSprites(other).Find(mine.Contains);
                if (shared == null) continue;
                EditorGUILayout.LabelField($"Dùng chung sprite quét '{shared.name}' với '{other.name}' — Scan không tự gắn đối tượng mới có sprite này; dùng Attach to selection.",
                    EditorStyles.wordWrappedMiniLabel);
                break;
            }
        }

        /// <summary>Sprite Scan dùng để nhận đối tượng của loại: sprite nguồn + sprite trong Image preset (nửa ON với loại Tab).</summary>
        private static List<Sprite> _ScanSprites(GGTypeBase type)
        {
            var list = new List<Sprite>();
            if (type.sourceSprite != null) list.Add(type.sourceSprite);
            var preset = type is GGTabType tab ? tab.on.imagePreset : type.imagePreset;
            var presetSprite = MobUIToolApplier.PresetSprite(preset);
            if (presetSprite != null && !list.Contains(presetSprite)) list.Add(presetSprite);
            return list;
        }

        /// <summary>
        /// Preset của loại, chia khối theo GameObject nhận preset: khối Button (hoặc Image) cho GameObject có Image,
        /// khối Text cho Text con. Chỉ lưu vào config — Apply mới ghi xuống prefab/scene.
        /// </summary>
        private void _DrawStyle(SerializedObject so, SerializedProperty tp, GGTypeBase type)
        {
            EditorGUILayout.LabelField("Style", EditorStyles.boldLabel);
            if (type is GGTabType tabType)
            {
                _DrawTabStyle(so, tp, tabType);
                return;
            }
            if (type is GGBlockType blockType)
            {
                _DrawBlockStyle(so, tp, blockType);
                return;
            }
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

            // Khối Cost tuỳ chọn: Icon preset hoặc Text preset là khoá. Loại có khối Cost không được Scan gắn tự động.
            _BeginGroup("Cost", "Button kèm giá: icon tiền tệ + Text giá nằm trong icon. Có khối này thì loại chỉ gắn marker bằng tay. Nút + tạo preset từ đúng node đang chọn (IconCost / TextCost)");
            _PresetRow(so, tp, nameof(GGButtonType.costIconRectPreset), "Rect Icon",
                "File .preset của RectTransform — áp lên icon giá", () => buttonType.costIconRectPreset,
                MobUIToolPresetFactory.Slot.Rect, typeof(RectTransform));
            _PresetRow(so, tp, nameof(GGButtonType.costIconPreset), "Icon",
                "File .preset của Image — áp lên icon giá, không bao giờ ghi sprite", () => buttonType.costIconPreset,
                MobUIToolPresetFactory.Slot.Image, typeof(Image));
            _PresetRow(so, tp, nameof(GGButtonType.costTextPreset), "Text",
                "File .preset của Text giá (không bao giờ ghi nội dung chữ)", () => buttonType.costTextPreset,
                MobUIToolPresetFactory.Slot.Text, typeof(Text));
            if (MobUIToolApplier.IsPresetFor(buttonType.costTextPreset, typeof(Text)))
            {
                _PresetRow(so, tp, nameof(GGButtonType.costTextRectPreset), "Rect Text",
                    "File .preset của RectTransform — áp lên Text giá", () => buttonType.costTextRectPreset,
                    MobUIToolPresetFactory.Slot.TextRect, typeof(RectTransform));
                _PresetRow(so, tp, nameof(GGButtonType.costEffectPreset), "Outline / Shadow",
                    "File .preset của Outline hoặc Shadow cho Text giá. Để trống = xoá cả hai",
                    () => buttonType.costEffectPreset, MobUIToolPresetFactory.Slot.Effect, typeof(Outline), typeof(Shadow));
            }
            _EndGroup();
        }

        /// <summary>
        /// Loại Tab: khối Structure (quy ước tên, Remove legacy, Localize) + khối Tab gốc + khối ON + khối OFF.
        /// Nút + của ô preset tạo từ component NGAY TRÊN object đang chọn — chọn đúng node mẫu (vd TabX/ImageOn/Title).
        /// </summary>
        private void _DrawTabStyle(SerializedObject so, SerializedProperty tp, GGTabType type)
        {
            _BeginGroup("Structure", "Quy ước tên node — Sync structure tạo node còn thiếu theo các tên này");
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(tp.FindPropertyRelative(nameof(GGTabType.onName)), new GUIContent("ON node", "Node con của tab giữ trạng thái đang chọn (vd TabButton.m_GoOn)"));
            EditorGUILayout.PropertyField(tp.FindPropertyRelative(nameof(GGTabType.offName)), new GUIContent("OFF node", "Node con của tab giữ trạng thái chưa chọn (vd TabButton.m_GoOff)"));
            EditorGUILayout.PropertyField(tp.FindPropertyRelative(nameof(GGTabType.imageName)), new GUIContent("Image node", "Tên node Image trong mỗi nửa"));
            EditorGUILayout.PropertyField(tp.FindPropertyRelative(nameof(GGTabType.titleName)), new GUIContent("Title node", "Tên node Text trong mỗi nửa"));
            EditorGUILayout.PropertyField(tp.FindPropertyRelative(nameof(GGTabType.removeLegacy)), new GUIContent("Remove legacy",
                "Xoá Graphic trên chính nửa ON/OFF và node con ngoài quy ước (vd 'img'). Node có tham chiếu từ ngoài không bị xoá. Xem Preview trước."));
            EditorGUILayout.PropertyField(tp.FindPropertyRelative(nameof(GGTabType.attachLocalize)), new GUIContent("Attach I2 Localize",
                "Gắn I2 Localize lên Title hai nửa; term lấy từ ô Term của marker GGTab trên từng tab"));
            if (EditorGUI.EndChangeCheck() && so.ApplyModifiedProperties()) _config.Save();
            _EndGroup();

            _BeginGroup("Tab", "GameObject gốc của tab (có Button)");
            _PresetRow(so, tp, nameof(GGTypeBase.rectTransformPreset), "Rect Tab",
                "File .preset của RectTransform — áp lên tab gốc", () => type.rectTransformPreset,
                MobUIToolPresetFactory.Slot.Rect, typeof(RectTransform));
            _PresetRow(so, tp, nameof(GGTypeBase.imagePreset), "Image",
                "File .preset của Image trên tab gốc (thường là target graphic của Button)", () => type.imagePreset,
                MobUIToolPresetFactory.Slot.Image, typeof(Image));
            _PresetRow(so, tp, nameof(GGTabType.layoutElementPreset), "Layout Element",
                "File .preset của LayoutElement — thiếu thì thêm vào tab gốc", () => type.layoutElementPreset,
                MobUIToolPresetFactory.Slot.Layout, typeof(LayoutElement));
            _EndGroup();

            _DrawTabHalf(so, tp, "on", "ON — đang chọn", type.onName, type.on, type);
            _DrawTabHalf(so, tp, "off", "OFF — chưa chọn", type.offName, type.off, type);
            _DrawTabExtras(so, tp, type);
        }

        /// <summary>
        /// Phần phụ của tab (vd Notify*): mỗi dòng một tiền tố + preset Rect/Image. "+ Add" lấy node phụ đang chọn làm mẫu.
        /// </summary>
        private void _DrawTabExtras(SerializedObject so, SerializedProperty tp, GGTabType type)
        {
            _BeginGroup("Extras", "Node con trực tiếp của tab gốc theo tiền tố tên (vd Notify → NotifyArmor). " +
                                  "Chỉ áp preset: không tạo, không đổi tên, không bật/tắt GameObject.");
            var list = tp.FindPropertyRelative(nameof(GGTabType.extras));
            for (int i = 0; i < list.arraySize; i++)
            {
                string f = $"{nameof(GGTabType.extras)}.Array.data[{i}]";
                var extra = type.extras[i];
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(tp.FindPropertyRelative(f + "." + nameof(GGTabExtra.prefix)),
                    new GUIContent("Prefix", "Node con trực tiếp của tab có tên bắt đầu bằng chuỗi này (vd Notify)"));
                if (EditorGUI.EndChangeCheck() && so.ApplyModifiedProperties()) _config.Save();
                if (GUILayout.Button(new GUIContent("−", "Bỏ phần phụ này khỏi loại (không xoá file preset, không đụng node)"), EditorStyles.miniButton, GUILayout.Width(22)))
                {
                    list.DeleteArrayElementAtIndex(i);
                    if (so.ApplyModifiedProperties()) _config.Save();
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
                _PresetRow(so, tp, f + "." + nameof(GGTabExtra.rectPreset), "Rect",
                    $"RectTransform của {extra.prefix}* (giữ vị trí, neo, scale trong tab)", () => extra.rectPreset,
                    MobUIToolPresetFactory.Slot.Rect, typeof(RectTransform));
                _PresetRow(so, tp, f + "." + nameof(GGTabExtra.imagePreset), "Image",
                    $"Image của {extra.prefix}*", () => extra.imagePreset, MobUIToolPresetFactory.Slot.Image, typeof(Image));
                EditorGUILayout.Space(2);
            }
            if (GUILayout.Button(new GUIContent("+ Add từ node đang chọn",
                    "Chọn node phụ mẫu trong Hierarchy (vd TabAccessory/NotifyAccessory): thêm dòng với tiền tố chung của node cùng loại ở các tab anh em, kèm preset Rect + Image")))
                _AddTabExtra(type);
            _EndGroup();
        }

        private void _AddTabExtra(GGTabType type)
        {
            var selected = Selection.activeGameObject;
            var tab = selected != null ? selected.transform.parent : null;
            if (tab == null || tab.GetComponent<Button>() == null || selected.name == type.onName || selected.name == type.offName)
            {
                _message = "Chọn node phụ là con trực tiếp của tab gốc (vd TabAccessory/NotifyAccessory), không phải nửa ON/OFF.";
                return;
            }

            string prefix = MobUIToolTabCapture.ExtraPrefix(selected.transform);
            if (type.extras.Exists(e => e != null && e.prefix == prefix))
            {
                _message = $"Loại '{type.name}' đã có phần phụ '{prefix}' — sửa thẳng trên tab rồi dùng Save to preset ở Inspector của GGTab.";
                return;
            }

            var extra = new GGTabExtra { prefix = prefix };
            extra.rectPreset = MobUIToolPresetFactory.SaveTabPreset(_config, MobUIToolPresetFactory.Slot.Rect, selected.transform, false, out var rectMsg);
            string imageMsg = "không có Image";
            var image = selected.GetComponent<Image>();
            if (image != null) extra.imagePreset = MobUIToolPresetFactory.SaveTabPreset(_config, MobUIToolPresetFactory.Slot.Image, image, false, out imageMsg);

            Undo.RecordObject(_config, "MobUITool add tab extra");
            type.extras.Add(extra);
            _config.Save();
            _message = $"Thêm phần phụ '{prefix}' từ '{selected.name}'. {rectMsg} {imageMsg}";
            GUIUtility.ExitGUI();
        }

        private void _DrawTabHalf(SerializedObject so, SerializedProperty tp, string field, string title, string node, GGTabHalf half, GGTabType type)
        {
            _BeginGroup(title, $"Node '{node}/{type.imageName}' và '{node}/{type.titleName}'");
            _PresetRow(so, tp, field + "." + nameof(GGTabHalf.imageRectPreset), "Rect Image",
                $"RectTransform của {node}/{type.imageName} (giữ vị trí trong tab)", () => half.imageRectPreset,
                MobUIToolPresetFactory.Slot.Rect, typeof(RectTransform));
            _PresetRow(so, tp, field + "." + nameof(GGTabHalf.imagePreset), "Image",
                $"Image của {node}/{type.imageName}", () => half.imagePreset, MobUIToolPresetFactory.Slot.Image, typeof(Image));
            _PresetRow(so, tp, field + "." + nameof(GGTabHalf.textPreset), "Text",
                $"Text của {node}/{type.titleName} (không bao giờ ghi nội dung chữ). Là khoá của Rect Text + Outline/Shadow",
                () => half.textPreset, MobUIToolPresetFactory.Slot.Text, typeof(Text));
            if (MobUIToolApplier.IsPresetFor(half.textPreset, typeof(Text)))
            {
                _PresetRow(so, tp, field + "." + nameof(GGTabHalf.textRectPreset), "Rect Text",
                    $"RectTransform của {node}/{type.titleName} (giữ vị trí trong tab)", () => half.textRectPreset,
                    MobUIToolPresetFactory.Slot.TextRect, typeof(RectTransform));
                _PresetRow(so, tp, field + "." + nameof(GGTabHalf.effectPreset), "Outline / Shadow",
                    "Title giữ đúng 1 component kiểu đó, xoá kiểu còn lại. Để trống = xoá cả hai",
                    () => half.effectPreset, MobUIToolPresetFactory.Slot.Effect, typeof(Outline), typeof(Shadow));
            }
            _EndGroup();
        }

        /// <summary>
        /// Loại Block: khối Gốc + mỗi phần con một khối (path, aliases, Rect/Image/Text/Outline-Shadow). Nút + của ô preset tạo
        /// từ component NGAY TRÊN object đang chọn; "+ Add part" lấy node con đang chọn của một khối có GGBlock.
        /// </summary>
        private void _DrawBlockStyle(SerializedObject so, SerializedProperty tp, GGBlockType type)
        {
            _BeginGroup("Gốc", "GameObject gốc của khối (gắn GGBlock). Rect gốc không mang vị trí/neo/pivot — chỉ kích thước, scale (lật chiều), xoay");
            _PresetRow(so, tp, nameof(GGTypeBase.rectTransformPreset), "Rect",
                "File .preset của RectTransform gốc", () => type.rectTransformPreset, MobUIToolPresetFactory.Slot.Rect, typeof(RectTransform));
            _PresetRow(so, tp, nameof(GGTypeBase.imagePreset), "Image",
                "File .preset của Image trên gốc", () => type.imagePreset, MobUIToolPresetFactory.Slot.Image, typeof(Image));
            _EndGroup();

            var list = tp.FindPropertyRelative(nameof(GGBlockType.parts));
            for (int i = 0; i < list.arraySize; i++)
            {
                string f = $"{nameof(GGBlockType.parts)}.Array.data[{i}]";
                var part = type.parts[i];
                _BeginGroup(string.IsNullOrEmpty(part.path) ? "(chưa có path)" : part.path, "Phần con, tìm theo đường dẫn tính từ gốc khối");
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(tp.FindPropertyRelative(f + "." + nameof(GGBlockPart.path)), new GUIContent("Path", "Đường dẫn node tính từ gốc khối (vd IconPower/TextPower)"));
                if (EditorGUI.EndChangeCheck() && so.ApplyModifiedProperties()) _config.Save();
                if (GUILayout.Button(new GUIContent("−", "Bỏ phần này khỏi loại (không xoá file preset, không đụng node)"), EditorStyles.miniButton, GUILayout.Width(22)))
                {
                    list.DeleteArrayElementAtIndex(i);
                    if (so.ApplyModifiedProperties()) _config.Save();
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(tp.FindPropertyRelative(f + "." + nameof(GGBlockPart.aliases)), new GUIContent("Aliases",
                    "Đường dẫn cũ của node này ở các màn dựng khác (vd ImagePower/TextPower) — Apply dời node về Path và đổi tên, giữ tham chiếu"), true);
                if (EditorGUI.EndChangeCheck() && so.ApplyModifiedProperties()) _config.Save();
                _PresetRow(so, tp, f + "." + nameof(GGBlockPart.rectPreset), "Rect",
                    $"RectTransform của {part.path} (giữ vị trí/neo/scale trong khối)", () => part.rectPreset,
                    MobUIToolPresetFactory.Slot.Rect, typeof(RectTransform));
                _PresetRow(so, tp, f + "." + nameof(GGBlockPart.imagePreset), "Image",
                    $"Image của {part.path}", () => part.imagePreset, MobUIToolPresetFactory.Slot.Image, typeof(Image));
                _PresetRow(so, tp, f + "." + nameof(GGBlockPart.textPreset), "Text",
                    $"Text của {part.path} (không bao giờ ghi nội dung chữ). Là khoá của Outline/Shadow", () => part.textPreset,
                    MobUIToolPresetFactory.Slot.Text, typeof(Text));
                if (MobUIToolApplier.IsPresetFor(part.textPreset, typeof(Text)))
                    _PresetRow(so, tp, f + "." + nameof(GGBlockPart.effectPreset), "Outline / Shadow",
                        "Text giữ đúng 1 component kiểu đó, xoá kiểu còn lại. Để trống = xoá cả hai",
                        () => part.effectPreset, MobUIToolPresetFactory.Slot.Effect, typeof(Outline), typeof(Shadow));
                _EndGroup();
            }
            if (GUILayout.Button(new GUIContent("+ Add part từ node đang chọn",
                    "Chọn node con mẫu của một khối đã gắn GGBlock: thêm phần với path tính từ gốc khối, kèm preset Rect/Image/Text/Outline-Shadow của node")))
                _AddBlockPart(type);
        }

        private void _AddBlockPart(GGBlockType type)
        {
            var selected = Selection.activeGameObject;
            var marker = selected != null ? selected.GetComponentInParent<GGBlock>(true) : null;
            if (marker == null || marker.gameObject == selected)
            {
                _message = "Chọn node con (không phải gốc) của một khối đã gắn GGBlock.";
                return;
            }
            string path = MobUIToolBlockApplier.RelativePath(marker.transform, selected.transform);
            if (type.parts.Exists(p => p != null && p.path == path))
            {
                _message = $"Loại '{type.name}' đã có phần '{path}' — sửa thẳng trên khối rồi dùng Save to preset ở Inspector của GGBlock.";
                return;
            }

            var part = new GGBlockPart { path = path };
            part.rectPreset = MobUIToolPresetFactory.SaveBlockPreset(_config, MobUIToolPresetFactory.Slot.Rect, selected.transform, false, type.name, out _);
            var image = selected.GetComponent<Image>();
            if (image != null) part.imagePreset = MobUIToolPresetFactory.SaveBlockPreset(_config, MobUIToolPresetFactory.Slot.Image, image, false, type.name, out _);
            var text = selected.GetComponent<Text>();
            if (text != null) part.textPreset = MobUIToolPresetFactory.SaveBlockPreset(_config, MobUIToolPresetFactory.Slot.Text, text, false, type.name, out _);

            Undo.RecordObject(_config, "MobUITool add block part");
            type.parts.Add(part);
            _config.Save();
            _message = $"Thêm phần '{path}' vào loại '{type.name}'.";
            GUIUtility.ExitGUI();
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
                var created = _IsBlockTab
                    ? MobUIToolPresetFactory.CreateForBlock(_config, slot, componentTypes[0], field == nameof(GGTypeBase.rectTransformPreset), _Current.name, out _message)
                    : _IsTabTab
                    ? MobUIToolPresetFactory.CreateForTab(_config, slot, componentTypes[0], field == nameof(GGTypeBase.rectTransformPreset), out _message)
                    : field.StartsWith("cost")
                        ? MobUIToolPresetFactory.CreateForCost(_config, slot, componentTypes[0], out _message)
                        : MobUIToolPresetFactory.CreateFromSelection(_config, slot, _IsImageTab, out _message);
                if (created != null)
                {
                    _SetPreset(path, created);
                    EditorGUIUtility.PingObject(created);
                }
            }
            EditorGUILayout.EndHorizontal();

            // Node con giữ vị trí tương đối trong button/tab — chỉ Rect gốc mới cần cảnh báo vị trí.
            _DrawPresetInfo(current(), label, field == nameof(GGTypeBase.rectTransformPreset), componentTypes);
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
        private static void _DrawPresetInfo(Preset preset, string label, bool warnPosition, System.Type[] componentTypes)
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

            if (warnPosition && MobUIToolApplier.IsPresetFor(preset, typeof(RectTransform)) && MobUIToolApplier.TouchesPosition(preset))
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
            if (type is GGTabType tabType) _DrawTabAttach(tabType);
            if (type is GGBlockType blockType) _DrawBlockAttach(blockType);
            // Tab/Block: Sync structure luôn có việc để làm, kể cả khi chưa gắn preset nào.
            bool canApply = type is GGTabType || type is GGBlockType ||
                            (buttonType != null && MobUIToolApplier.HasTextKey(buttonType)) ||
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

        /// <summary>Gắn marker GGTab của loại lên tab chứa các object đang chọn trong Hierarchy (cho tab cấu trúc cũ, Scan không nhận ra).</summary>
        private void _DrawTabAttach(GGTabType type)
        {
            using (new EditorGUI.DisabledScope(_InPlayMode || Selection.gameObjects.Length == 0))
            {
                if (GUILayout.Button(new GUIContent($"Attach to selection ({Selection.gameObjects.Length})",
                        "Gắn GGTab của loại này lên tab (GameObject có Button) chứa từng object đang chọn trong Hierarchy. Undo được; lưu scene/prefab rồi Refresh."),
                        GUILayout.Height(22)))
                {
                    MobUIToolScanner.AttachToSelection(type, Selection.gameObjects, out _message);
                    Debug.Log("[MobUITool] " + _message);
                }
            }
        }

        /// <summary>Gắn marker GGBlock của loại lên chính các object đang chọn (gốc khối).</summary>
        private void _DrawBlockAttach(GGBlockType type)
        {
            using (new EditorGUI.DisabledScope(_InPlayMode || Selection.gameObjects.Length == 0))
            {
                if (GUILayout.Button(new GUIContent($"Attach to selection ({Selection.gameObjects.Length})",
                        "Gắn GGBlock của loại này lên CHÍNH từng object đang chọn (chọn gốc khối, vd FieldPower). Undo được; lưu scene/prefab rồi Refresh."),
                        GUILayout.Height(22)))
                {
                    MobUIToolBlockApplier.AttachToSelection(type, Selection.gameObjects, out _message);
                    Debug.Log("[MobUITool] " + _message);
                }
            }
        }

        private void _Apply(GGTypeBase type)
        {
            if (!MobUIToolAssetUtil.SaveOpenEditorsIfUserWantsTo()) return;

            var direct = MobUIToolScanner.FilesOfType(type);
            var files = MobUIToolAssetUtil.SortByDependency(MobUIToolAssetUtil.WithParents(direct));

            string removeNote = type is GGButtonType bt && MobUIToolApplier.HasTextKey(bt) && MobUIToolApplier.IsEmpty(bt.effectPreset)
                ? "\n\nÔ Outline / Shadow đang trống → sẽ XOÁ Outline và Shadow khỏi Text của mọi button thuộc loại."
                : type is GGTabType tt && tt.removeLegacy
                    ? "\n\nRemove legacy đang BẬT → sẽ XOÁ node/component cũ trong nửa ON/OFF (xem Preview để biết cụ thể)."
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
