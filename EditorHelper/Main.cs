﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ADOFAI;
using EditorHelper.Settings;
using EditorHelper.Utils;
using GDMiniJSON;
using HarmonyLib;
using SA.GoogleDoc;
using UnityEngine;
using UnityModManagerNet;
using PropertyInfo = ADOFAI.PropertyInfo;

namespace EditorHelper {
    internal static class Main {
        private static Harmony _harmony;
        private static UnityModManager.ModEntry _mod;
        internal static MainSettings Settings { get; private set; }
        internal static bool FirstLoaded;
        internal static bool IsEnabled;

        internal static bool highlightEnabled;
        // internal static UnityModManager.ModEntry.ModLogger Logger => _mod?.Logger;

        private const int Exact = 0;
        private const int NotLess = 1;
        private const int NotBigger = 2;
        private const int Bigger = 3;
        private const int Less = 4;
        
        private static bool Load(UnityModManager.ModEntry modEntry) {
            var version = AccessTools.Field(typeof(GCNS), "releaseNumber").GetValue(null) as int?;
            var editorHelperDir = Path.Combine(Directory.GetCurrentDirectory(), "Mods", "EditorHelper");
            var target = 76;
            var mode = Exact;
            if (File.Exists(Path.Combine(editorHelperDir, "Version.txt"))) {
                var value = File.ReadAllText(Path.Combine(editorHelperDir, "Version.txt"));
                if (value.StartsWith(">=")) {
                    mode = NotLess;
                    value = value.Substring(2);
                } else if (value.StartsWith("<=")) {
                    mode = NotBigger;
                    value = value.Substring(2);
                } else if (value.StartsWith(">")) {
                    mode = Bigger;
                    value = value.Substring(2);
                } else if (value.StartsWith("<")) {
                    mode = Less;
                    value = value.Substring(2);
                } else if (value.StartsWith("==")) {
                    mode = Exact;
                    value = value.Substring(2);
                }
                
                if (int.TryParse(value, out var val)) {
                    target = val;
                    UnityModManager.Logger.Log($"EditorHelper version set to {value}");
                }
            }

            if (version == null) return false;
            switch (mode) {
                case Exact:
                    if (version != target) return false;
                    break;
                case NotLess:
                    if (version < target) return false;
                    break;
                case NotBigger:
                    if (version > target) return false;
                    break;
                case Bigger:
                    if (version <= target) return false;
                    break;
                case Less:
                    if (version >= target) return false;
                    break;
            }
            
            Settings = UnityModManager.ModSettings.Load<MainSettings>(modEntry);
            Settings.moreEditorSettings_prev = Settings.MoreEditorSettings;

            _mod = modEntry;
            _mod.OnToggle = OnToggle;
            _mod.OnGUI = OnGUI;
            _mod.OnSaveGUI = OnSaveGUI; 
            
            PatchRDString.Translations["editor.useLegacyFlash"] = new Dictionary<LangCode, string> {
                {LangCode.Korean, "기존 플래시 사용"},
                {LangCode.English, "Use legacy flash"},
            };
            PatchRDString.Translations["editor.convertFloorMesh"] = new Dictionary<LangCode, string> {
                {LangCode.Korean, "레벨 변환"},
                {LangCode.English, "Convert level"},
            };
            PatchRDString.Translations["editor.convertFloorMesh.toLegacy"] = new Dictionary<LangCode, string> {
                {LangCode.Korean, "기존 타일로 변환"},
                {LangCode.English, "Convert to legacy tiles"},
            };
            PatchRDString.Translations["editor.convertFloorMesh.toMesh"] = new Dictionary<LangCode, string> {
                {LangCode.Korean, "자유 각도로 변환"},
                {LangCode.English, "Convert to mesh models"},
            };

            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value) {
            _mod = modEntry;
            IsEnabled = value;

            if (value) {
                StartTweaks();
                ApplyConfig();
            } else {
                StopTweaks();
                ApplyConfig(true);
            }

            return true;
        }

        private static void StartTweaks() {
            _harmony = new Harmony(_mod.Info.Id);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            CheckMoreEditorSettings();
        }

        internal static void CheckMoreEditorSettings() {
            if (Settings.MoreEditorSettings) {
                try {
                    GCS.settingsInfo["MiscSettings"].propertiesInfo["useLegacyFlash"] =
                        new PropertyInfo(new Dictionary<string, object> {
                            {"name", "useLegacyFlash"},
                            {"type", "Enum:Toggle"},
                            {"default", "Disabled"}
                        }, GCS.settingsInfo["MiscSettings"]);
                    GCS.settingsInfo["MiscSettings"].propertiesInfo["convertFloorMesh"] =
                        new PropertyInfo(new Dictionary<string, object> {
                            {"name", "convertFloorMesh"},
                            {"type", "Export"}
                        }, GCS.settingsInfo["MiscSettings"]);
                } catch (NullReferenceException) { }
            } else {
                GCS.settingsInfo["MiscSettings"].propertiesInfo.Remove("useLegacyFlash");
                GCS.settingsInfo["MiscSettings"].propertiesInfo.Remove("convertFloorMesh");
            }
        }
        
        private static void StopTweaks() {
            TargetPatch.UntargetFloor();
            _harmony.UnpatchAll(_harmony.Id);
            _harmony = null;
            GCS.settingsInfo["MiscSettings"].propertiesInfo.Remove("useLegacyFlash");
            GCS.settingsInfo["MiscSettings"].propertiesInfo.Remove("convertFloorMesh");
        }

        private static void OnGUI(UnityModManager.ModEntry modEntry) {
            GUIEx.Toggle(ref Settings.EnableFloor0Events, (LangCode.English, "Enable Floor 0 Events"), (LangCode.Korean, "첫 타일 이벤트 활성화"));
            GUIEx.Toggle(ref Settings.RemoveLimits, (LangCode.English, "Remove All Editor Limits"), (LangCode.Korean, "에디터 입력값 제한 비활성화"));
            GUIEx.Toggle(ref Settings.AutoArtistURL, (LangCode.English, "Enable Auto Paste Artist URL"), (LangCode.Korean, "작곡가 URL 자동 입력"));
            GUIEx.Toggle(ref Settings.SmallerDeltaDeg, (LangCode.English, "Enable Smaller Delta Degree (90° -> 15°, Press 'Ctrl + Alt + ,' or 'Ctrl + Alt + .' to use 15°)"), (LangCode.Korean, "더 작은 각도로 타일 회전 (90° -> 15°, 'Ctrl + Alt + ,' 또는 'Ctrl + Alt + .'로 15° 단위 회전)"));
            GUIEx.Toggle(ref Settings.EnableBetterBackup, (LangCode.English, "Enable better editor backup in nested directory"), (LangCode.Korean, "레벨이 있는 폴더에서 더 나은 백업"));
            if (Settings.EnableBetterBackup) {
                GUIEx.BeginIndent();
                GUIEx.IntField(ref Settings.MaximumBackups, (LangCode.English, "Limit the amount of backups (0 is infinite)"), (LangCode.Korean, "백업 개수 제한 (0 ⇒ 제한 없음)"));
                GUIEx.Toggle(ref Settings.SaveLatestBackup, (LangCode.English, "Still put the backup in backup.adofai after using better backup"), (LangCode.Korean, "더 나은 백업 활성화 후에도 backup.adofai 사용"));
                GUIEx.EndIndent();
            }

            GUIEx.Toggle(ref Settings.ThisTile, (LangCode.English, "Change Event Using 'This Tile'"), (LangCode.Korean, "'이 타일'로 이벤트 변경"));
            GUIEx.Toggle(ref Settings.FirstTile, (LangCode.English, "Change Event Using 'First Tile'"), (LangCode.Korean, "'첫 타일'로 이벤트 변경"));
            GUIEx.Toggle(ref Settings.LastTile, (LangCode.English, "Change Event Using 'Last Tile'"), (LangCode.Korean, "'마지막 타일'로 이벤트 변경"));
            GUIEx.Toggle(ref Settings.HighlightTargetedTiles, (LangCode.English, "Highlight Targeted Tiles"), (LangCode.Korean, "목표 타일 하이라이트"));
            GUIEx.Toggle(ref Settings.SelectTileWithShortcutKeys, (LangCode.English, "Select Tile With ; + Click, ' + Click"), (LangCode.Korean, "타일을 ; + 클릭, ' + 클릭으로 선택"));
            GUIEx.Toggle(ref Settings.ChangeIndexWhenToggle, (LangCode.English, "Change Index When Toggle This Tile, First Tile, Last Tile"), (LangCode.Korean, "이 타일, 첫 타일, 마지막 타일 전환 시 선택된 타일 유지"));
            GUIEx.Toggle(ref Settings.MoreEditorSettings, (LangCode.English, "Enable More Editor Settings (Toggle Mesh tiles, etc.)"), (LangCode.Korean, "더 많은 에디터 설정 (자유 각도 토글 등)"));
            GUIEx.Toggle(ref Settings.EnableScreenRot, (LangCode.English, "Enable Rotating Editor Screen (Press 'Alt + ,' or 'Alt + .' to rotate editor screen 15°)"), (LangCode.Korean, "에디터 화면 회전 ('Alt' + , 또는 'Alt' + .)"));
            GUIEx.Toggle(ref Settings.EnableSelectedTileShowAngle, (LangCode.English, "Show Angle of Selected Tiles"), (LangCode.Korean, "선택된 타일의 각도 보기"));
            GUIEx.Toggle(ref Settings.EnableChangeAngleByDragging, (LangCode.English, "Enable Change Angle By Dragging"), (LangCode.Korean, "드래그해서 각도 변경"));
            if (Settings.EnableChangeAngleByDragging) {
                GUIEx.BeginIndent();
                GUILayout.BeginHorizontal();
                GUIEx.IntField(ref Settings.MeshNumerator, 0, int.MaxValue, GUILayout.Width(60));
                GUILayout.Label("/", GUILayout.Width(15));
                GUIEx.IntField(ref Settings.MeshDenominator, 1, int.MaxValue, GUILayout.Width(60));
                GUILayout.Label($"({Settings.MeshDelta})", GUILayout.Width(40));
                GUIEx.Label((LangCode.English, "Changed Angle Delta"), (LangCode.Korean, "각도 변경 단위"));
                GUILayout.EndHorizontal();
                GUIEx.EndIndent();
            }

            if (Settings.HighlightTargetedTiles && !highlightEnabled) {
                highlightEnabled = true;
                if (scnEditor.instance?.selectedFloors.Count == 1)
                    if (scnEditor.instance.levelEventsPanel.selectedEventType == LevelEventType.MoveTrack ||
                        scnEditor.instance.levelEventsPanel.selectedEventType == LevelEventType.RecolorTrack)
                        TargetPatch.TargetFloor();
            }

            if (!Settings.HighlightTargetedTiles && highlightEnabled) {
                highlightEnabled = false;
                if (TargetPatch.targets.Count != 0)
                    TargetPatch.UntargetFloor();
            }
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry) {
            Settings.Save(modEntry);

            ApplyConfig();
        }

        private static void ApplyConfig(bool forceRemove = false) {
            if (GCS.settingsInfo == null) {
                return;
            }

            if (!FirstLoaded) {
                FirstLoaded = true;
            }

            if (Settings.RemoveLimits && !forceRemove) {
                foreach (var propertyInfo in GCS.levelEventsInfo.SelectMany(eventPair =>
                    eventPair.Value.propertiesInfo.Select(propertiesPair => propertiesPair.Value))) {
                    switch (propertyInfo.type) {
                        case PropertyType.Color:
                            propertyInfo.color_usesAlpha = true;
                            break;
                        case PropertyType.Int:
                            propertyInfo.int_min = int.MinValue;
                            propertyInfo.int_max = int.MaxValue;
                            break;
                        case PropertyType.Float:
                            propertyInfo.float_min = float.NegativeInfinity;
                            propertyInfo.float_max = float.PositiveInfinity;
                            break;
                        case PropertyType.Vector2:
                            propertyInfo.maxVec = Vector2.positiveInfinity;
                            propertyInfo.minVec = Vector2.negativeInfinity;
                            break;
                    }
                }

                foreach (var propertyInfo in GCS.settingsInfo.SelectMany(eventPair =>
                    eventPair.Value.propertiesInfo.Select(propertiesPair => propertiesPair.Value))) {
                    switch (propertyInfo.type) {
                        case PropertyType.Color:
                            propertyInfo.color_usesAlpha = true;
                            break;
                        case PropertyType.Int:
                            propertyInfo.int_min = int.MinValue;
                            propertyInfo.int_max = int.MaxValue;
                            break;
                        case PropertyType.Float:
                            propertyInfo.float_min = float.NegativeInfinity;
                            propertyInfo.float_max = float.PositiveInfinity;
                            break;
                        case PropertyType.Vector2:
                            propertyInfo.maxVec = Vector2.positiveInfinity;
                            propertyInfo.minVec = Vector2.negativeInfinity;
                            break;
                    }
                }
            } else {
                if (!(Json.Deserialize(Resources.Load<TextAsset>("LevelEditorProperties").text) is
                    Dictionary<string, object> dictionary)) {
                    return;
                }

                var levelEventsInfo = Misc.Decode(dictionary["levelEvents"] as IEnumerable<object>);
                var settingsInfo = Misc.Decode(dictionary["settings"] as IEnumerable<object>);

                foreach (var (key, value) in GCS.levelEventsInfo) {
                    var levelEventInfo = levelEventsInfo[key];

                    foreach (var (property, propertyInfo) in value.propertiesInfo) {
                        var originalPropertyInfo = levelEventInfo.propertiesInfo[property];

                        switch (propertyInfo.type) {
                            case PropertyType.Color:
                                propertyInfo.color_usesAlpha = originalPropertyInfo.color_usesAlpha;
                                break;
                            case PropertyType.Int:
                                propertyInfo.int_min = originalPropertyInfo.int_min;
                                propertyInfo.int_max = originalPropertyInfo.int_max;
                                break;
                            case PropertyType.Float:
                                propertyInfo.float_min = originalPropertyInfo.float_min;
                                propertyInfo.float_max = originalPropertyInfo.float_max;
                                break;
                            case PropertyType.Vector2:
                                propertyInfo.maxVec = originalPropertyInfo.maxVec;
                                propertyInfo.minVec = originalPropertyInfo.minVec;
                                break;
                        }
                    }
                }

                foreach (var (key, value) in GCS.settingsInfo) {
                    var levelEventInfo = settingsInfo[key];

                    foreach (var (property, propertyInfo) in value.propertiesInfo) {
                        var originalPropertyInfo = levelEventInfo.propertiesInfo[property];

                        switch (propertyInfo.type) {
                            case PropertyType.Color:
                                propertyInfo.color_usesAlpha = originalPropertyInfo.color_usesAlpha;
                                break;
                            case PropertyType.Int:
                                propertyInfo.int_min = originalPropertyInfo.int_min;
                                propertyInfo.int_max = originalPropertyInfo.int_max;
                                break;
                            case PropertyType.Float:
                                propertyInfo.float_min = originalPropertyInfo.float_min;
                                propertyInfo.float_max = originalPropertyInfo.float_max;
                                break;
                            case PropertyType.Vector2:
                                propertyInfo.maxVec = originalPropertyInfo.maxVec;
                                propertyInfo.minVec = originalPropertyInfo.minVec;
                                break;
                        }
                    }
                }
            }
        }
    }
}