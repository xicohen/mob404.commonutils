using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace Mob404.Common.UITemplate.Editor
{
    /// <summary>Tìm, sắp xếp và mở/lưu prefab + scene cho tool MobUITool.</summary>
    public static class MobUIToolAssetUtil
    {
        /// <summary>Changed = đã lưu (hoặc, khi chạy thử, sẽ lưu).</summary>
        public enum Result { Unchanged, Changed, Skipped }

        /// <summary>Mọi .prefab và .unity dưới Assets/.</summary>
        public static List<string> AllPrefabsAndScenes()
        {
            var paths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab t:Scene", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".prefab") || path.EndsWith(".unity")) paths.Add(path);
            }
            return paths;
        }

        /// <summary>Lọc nhanh theo text YAML: file có chứa ít nhất một chuỗi trong danh sách (GUID, typeId…).</summary>
        public static List<string> FilterContaining(List<string> paths, ICollection<string> needles)
        {
            var result = new List<string>();
            if (needles.Count == 0) return result;
            foreach (var path in paths)
            {
                string text;
                try { text = File.ReadAllText(path); }
                catch (IOException) { continue; }
                foreach (var needle in needles)
                {
                    if (text.Contains(needle)) { result.Add(path); break; }
                }
            }
            return result;
        }

        /// <summary>
        /// Thêm vào danh sách mọi prefab/scene chứa (trực tiếp hoặc lồng nhiều tầng) một prefab trong danh sách —
        /// để instance lồng nhau đang override giá trị cũng được đồng bộ.
        /// </summary>
        public static List<string> WithParents(List<string> files)
        {
            var parents = new Dictionary<string, List<string>>();
            foreach (var path in AllPrefabsAndScenes())
            {
                foreach (var dep in AssetDatabase.GetDependencies(path, false))
                {
                    if (dep == path || !dep.EndsWith(".prefab")) continue;
                    if (!parents.TryGetValue(dep, out var list)) parents[dep] = list = new List<string>();
                    list.Add(path);
                }
            }

            var result = new HashSet<string>(files);
            var queue = new Queue<string>(files);
            while (queue.Count > 0)
            {
                if (!parents.TryGetValue(queue.Dequeue(), out var list)) continue;
                foreach (var p in list)
                    if (result.Add(p)) queue.Enqueue(p);
            }
            return new List<string>(result);
        }

        /// <summary>
        /// Prefab nguồn trước, prefab dùng nó sau, scene cuối cùng. Nếu A chứa B thì
        /// dependencies(A) ⊇ dependencies(B) ∪ {B} nên đếm phụ thuộc trong danh sách là thứ tự tô-pô hợp lệ.
        /// </summary>
        public static List<string> SortByDependency(List<string> paths)
        {
            var set = new HashSet<string>(paths);
            var depCount = new Dictionary<string, int>();
            foreach (var path in paths)
            {
                int n = 0;
                foreach (var dep in AssetDatabase.GetDependencies(path, true))
                    if (dep != path && set.Contains(dep)) n++;
                depCount[path] = n;
            }

            var sorted = new List<string>(paths);
            sorted.Sort((a, b) =>
            {
                bool sa = a.EndsWith(".unity"), sb = b.EndsWith(".unity");
                if (sa != sb) return sa ? 1 : -1;
                int c = depCount[a].CompareTo(depCount[b]);
                return c != 0 ? c : string.CompareOrdinal(a, b);
            });
            return sorted;
        }

        /// <summary>
        /// Trước khi ghi hàng loạt: hỏi lưu scene đang sửa dở (hộp thoại chuẩn của Unity) và prefab đang mở trong
        /// Prefab Mode. Trả về false nếu người dùng huỷ. Chọn "Don't Save" thì file đó sẽ bị bỏ qua khi xử lý.
        /// </summary>
        public static bool SaveOpenEditorsIfUserWantsTo()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.scene.isDirty)
            {
                int choice = EditorUtility.DisplayDialogComplex("MobUITool",
                    $"Prefab '{Path.GetFileName(stage.assetPath)}' đang mở trong Prefab Mode và có thay đổi chưa lưu.",
                    "Save", "Cancel", "Don't Save (bỏ qua file này)");
                if (choice == 1) return false;
                if (choice == 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath);
                    stage.ClearDirtiness();
                }
            }
            return EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
        }

        /// <summary>
        /// File nào trong danh sách đang có thay đổi chưa commit trong git. Trả về null nếu không chạy được git
        /// (không có git, không phải repo…) — khi đó bỏ qua kiểm tra.
        /// </summary>
        public static List<string> GitDirty(IList<string> paths)
        {
            if (paths.Count == 0) return new List<string>();
            try
            {
                var args = "status --porcelain --";
                foreach (var p in paths) args += " \"" + p + "\"";
                var psi = new ProcessStartInfo("git", args)
                {
                    WorkingDirectory = Path.GetDirectoryName(Application.dataPath),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using (var process = System.Diagnostics.Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    if (!process.WaitForExit(10000) || process.ExitCode != 0) return null;
                    var dirty = new List<string>();
                    foreach (var line in output.Split('\n'))
                        if (line.Length > 3) dirty.Add(line.Substring(3).Trim().Trim('"'));
                    return dirty;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MobUITool] Không kiểm được git status: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Mở prefab/scene, gọi action trên các root GameObject, lưu nếu action trả về true (save = false: chạy thử, không lưu).
        /// Scene đang mở và có thay đổi chưa lưu, hoặc prefab đang mở trong Prefab Mode chưa lưu, sẽ bị bỏ qua.
        /// </summary>
        public static Result Process(string path, Func<GameObject[], bool> action, bool save, out string skipReason)
        {
            return path.EndsWith(".unity")
                ? _ProcessScene(path, action, save, out skipReason)
                : _ProcessPrefab(path, action, save, out skipReason);
        }

        private static Result _ProcessPrefab(string path, Func<GameObject[], bool> action, bool save, out string skipReason)
        {
            skipReason = null;
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.assetPath == path && stage.scene.isDirty)
            {
                skipReason = "đang mở trong Prefab Mode và chưa lưu";
                return Result.Skipped;
            }

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (!action(new[] { root })) return Result.Unchanged;
                if (save) PrefabUtility.SaveAsPrefabAsset(root, path);
                return Result.Changed;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Result _ProcessScene(string path, Func<GameObject[], bool> action, bool save, out string skipReason)
        {
            skipReason = null;
            var scene = SceneManager.GetSceneByPath(path);
            bool wasLoaded = scene.IsValid() && scene.isLoaded;
            if (wasLoaded && scene.isDirty)
            {
                skipReason = "scene đang mở và có thay đổi chưa lưu";
                return Result.Skipped;
            }

            if (!wasLoaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                if (!action(scene.GetRootGameObjects())) return Result.Unchanged;
                if (save)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                return Result.Changed;
            }
            finally
            {
                if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
