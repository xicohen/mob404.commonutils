using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Mob404.Common.UITemplate.Editor
{
    /// <summary>Thao tác hàng loạt trên prefab/scene: Apply / Preview một loại, gỡ marker của một loại, dọn marker mồ côi.</summary>
    public static class MobUIToolBatchApply
    {
        /// <summary>
        /// File cần xử lý khi áp một loại: file chứa trực tiếp marker của loại (tìm lại trong YAML, không dựa vào
        /// danh sách cache) + mọi prefab/scene chứa các prefab đó, sắp prefab nguồn trước.
        /// </summary>
        public static List<string> ApplyTargets(GGTypeBase type) =>
            MobUIToolAssetUtil.SortByDependency(MobUIToolAssetUtil.WithParents(MobUIToolScanner.FilesOfType(type)));

        /// <summary>Áp (dryRun = false) hoặc xem trước (dryRun = true — không sửa, không lưu file nào) một loại.</summary>
        public static MobUIToolReport Apply(GGTypeBase type, bool dryRun, List<string> files = null)
        {
            files ??= ApplyTargets(type);
            var report = new MobUIToolReport($"Apply '{type.name}'", dryRun) { summary = $"{files.Count} file" };
            MobUIToolApplier.DryRun = dryRun;
            try
            {
                _Run(report, files, !dryRun, (roots, entry) =>
                {
                    int n = 0;
                    foreach (var root in roots)
                    {
                        if (type is GGButtonType buttonType)
                        {
                            foreach (var marker in root.GetComponentsInChildren<GGButton>(true))
                                if (marker.typeId == type.id && MobUIToolApplier.ApplyToButton(marker, buttonType)) n++;
                        }
                        else if (type is GGImageType imageType)
                        {
                            foreach (var marker in root.GetComponentsInChildren<GGImage>(true))
                                if (marker.typeId == type.id && MobUIToolApplier.ApplyToImage(marker, imageType)) n++;
                        }
                    }
                    return n;
                });
            }
            finally
            {
                MobUIToolApplier.DryRun = false;
            }
            return report;
        }

        /// <summary>Gỡ mọi marker của một loại khỏi prefab/scene (marker thuộc prefab nguồn được gỡ ở chính prefab nguồn).</summary>
        public static MobUIToolReport RemoveMarkers(GGTypeBase type)
        {
            var files = MobUIToolAssetUtil.SortByDependency(MobUIToolScanner.FilesOfType(type));
            var report = new MobUIToolReport($"Remove markers '{type.name}'") { summary = $"{files.Count} file" };
            var markerType = type is GGButtonType ? typeof(GGButton) : typeof(GGImage);
            _Run(report, files, true, (roots, entry) =>
                _DestroyMarkers(roots, markerType, id => id == type.id));
            return report;
        }

        /// <summary>Gỡ marker mồ côi: typeId trống hoặc trỏ tới loại không còn trong config.</summary>
        public static MobUIToolReport RemoveOrphans(MobUIToolConfig config)
        {
            var guids = new List<string>();
            foreach (var t in new[] { typeof(GGButton), typeof(GGImage) })
            {
                string guid = MobUIToolScanner.ScriptGuid(t);
                if (!string.IsNullOrEmpty(guid)) guids.Add(guid);
            }
            var files = MobUIToolAssetUtil.SortByDependency(
                MobUIToolAssetUtil.FilterContaining(MobUIToolAssetUtil.AllPrefabsAndScenes(), guids));
            var report = new MobUIToolReport("Remove orphan markers") { summary = $"{files.Count} file có marker" };
            _Run(report, files, true, (roots, entry) =>
                _DestroyMarkers(roots, typeof(GGButton), id => config.FindType(id) == null) +
                _DestroyMarkers(roots, typeof(GGImage), id => config.FindImageType(id) == null));
            return report;
        }

        private static int _DestroyMarkers(GameObject[] roots, Type markerType, Func<string, bool> match)
        {
            int n = 0;
            foreach (var root in roots)
            foreach (var c in root.GetComponentsInChildren(markerType, true))
            {
                string id = c is GGButton b ? b.typeId : ((GGImage)c).typeId;
                if (!match(id)) continue;
                if (PrefabUtility.IsPartOfPrefabInstance(c) && !PrefabUtility.IsAddedComponentOverride(c)) continue;
                UnityEngine.Object.DestroyImmediate(c);
                n++;
            }
            return n;
        }

        /// <summary>Chạy action trên từng file (có thanh tiến độ, huỷ được), gom kết quả + cảnh báo vào report.</summary>
        private static void _Run(MobUIToolReport report, List<string> files, bool save, Func<GameObject[], MobUIToolReport.Entry, int> action)
        {
            try
            {
                for (int i = 0; i < files.Count; i++)
                {
                    string path = files[i];
                    if (EditorUtility.DisplayCancelableProgressBar("MobUITool — " + report.title, path, (float)i / files.Count))
                    {
                        report.cancelled = true;
                        break;
                    }

                    var entry = report.Add(path);
                    MobUIToolApplier.Warnings.Clear();
                    try
                    {
                        entry.result = MobUIToolAssetUtil.Process(path, roots =>
                        {
                            entry.objects = action(roots, entry);
                            return entry.objects > 0;
                        }, save, out entry.skipReason);
                    }
                    catch (Exception e)
                    {
                        entry.error = e.Message;
                        Debug.LogError($"[MobUITool] Lỗi ({report.title}) ở {path}: {e}");
                    }
                    entry.warnings.AddRange(MobUIToolApplier.Warnings);
                }
            }
            finally
            {
                MobUIToolApplier.Warnings.Clear();
                MobUIToolApplier.CleanupTemp();
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
