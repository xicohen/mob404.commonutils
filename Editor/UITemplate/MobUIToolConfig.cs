using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Mob404.Common.UITemplate.Editor
{
    /// <summary>
    /// Danh sách loại Button (tab Button) và loại Image (tab Image) của project. Nằm trong Assets/ của project
    /// (package cài qua git là read-only). Preset là file .preset riêng trong project, config chỉ tham chiếu tới.
    /// </summary>
    public class MobUIToolConfig : ScriptableObject
    {
        public const string DEFAULT_PATH = "Assets/Editor/MobUITool/MobUIToolConfig.asset";

        public List<GGButtonType> types = new List<GGButtonType>();
        public List<GGImageType> imageTypes = new List<GGImageType>();

        public GGButtonType FindType(string id) => _Find(types, id);

        public GGImageType FindImageType(string id) => _Find(imageTypes, id);

        private static T _Find<T>(List<T> list, string id) where T : GGTypeBase
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var t in list)
                if (t.id == id) return t;
            return null;
        }

        /// <summary>Tìm config trong project, không có thì tạo mới ở DEFAULT_PATH.</summary>
        public static MobUIToolConfig LoadOrCreate()
        {
            var config = Load();
            if (config != null) return config;

            string folder = System.IO.Path.GetDirectoryName(DEFAULT_PATH).Replace('\\', '/');
            EnsureFolder(folder);
            config = CreateInstance<MobUIToolConfig>();
            AssetDatabase.CreateAsset(config, DEFAULT_PATH);
            AssetDatabase.SaveAssets();
            return config;
        }

        public static MobUIToolConfig Load()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(MobUIToolConfig)))
            {
                var config = AssetDatabase.LoadAssetAtPath<MobUIToolConfig>(AssetDatabase.GUIDToAssetPath(guid));
                if (config != null) return config;
            }
            return null;
        }

        public void Save()
        {
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssetIfDirty(this);
        }

        /// <summary>Tạo thư mục (và thư mục cha) trong AssetDatabase nếu chưa có.</summary>
        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
        }
    }
}
