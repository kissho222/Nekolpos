using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Nekolpos.EditorTools
{
    public sealed class NekolposTimelinePalette : EditorWindow
    {
        private List<SignalAsset> signals = new List<SignalAsset>();
        private string search = "";
        private string category = "すべて";
        private Vector2 scroll;
        private SignalTrack targetTrack;
        private TimelineAsset lastTimeline;
        private string result;

        [MenuItem("Window/Nekolpos/Timeline Palette")]
        public static void Open() => GetWindow<NekolposTimelinePalette>("Timeline Palette");

        private void OnEnable()
        {
            Reload();
            EditorApplication.projectChanged += Reload;
            Undo.undoRedoPerformed += Repaint;
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= Reload;
            Undo.undoRedoPerformed -= Repaint;
        }

        private void OnInspectorUpdate() => Repaint();

        private void Reload()
        {
            signals = AssetDatabase.FindAssets("t:SignalAsset", new[] { "Assets" })
                .Select(g => AssetDatabase.LoadAssetAtPath<SignalAsset>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(s => s != null).OrderBy(s => s.name, StringComparer.OrdinalIgnoreCase).ToList();
            Repaint();
        }

        public static string GetCategory(SignalAsset signal)
        {
            string name = signal.name.ToLowerInvariant();
            if (name.Contains("fade")) return "暗転・明転";
            if (name.Contains("look") || name.Contains("gaze")) return "視線";
            if (name.Contains("ui")) return "UI";
            if (name.Contains("location")) return "場所変更";
            if (name.Contains("cat") || name.Contains("conversation")) return "猫・会話";
            return "その他";
        }

        private void OnGUI()
        {
            var window = TimelineEditor.GetWindow();
            var timeline = TimelineEditor.inspectedAsset;
            var director = TimelineEditor.inspectedDirector;
            if (timeline != lastTimeline) { lastTimeline = timeline; targetTrack = null; result = null; }
            EditorGUILayout.LabelField("編集中", timeline != null ? timeline.name : "Timelineを開いてください");
            double time = window != null && timeline != null
                ? window.playbackControls.GetCurrentTime(TimelinePlaybackControls.Context.Local) : 0;
            EditorGUILayout.LabelField("再生ヘッド", time.ToString("F3") + " 秒");

            var tracks = timeline != null ? GetSignalTracks(timeline).ToList() : new List<SignalTrack>();
            if (!tracks.Contains(targetTrack)) targetTrack = null;
            var choices = new[] { "自動（既存の使用可能Track／新規作成）" }.Concat(tracks.Select(t => t.name)).ToArray();
            int selected = EditorGUILayout.Popup("追加先", targetTrack == null ? 0 : tracks.IndexOf(targetTrack) + 1, choices);
            targetTrack = selected == 0 ? null : tracks[selected - 1];
            search = EditorGUILayout.TextField("検索（名前・パス）", search);
            var categories = new[] { "すべて" }.Concat(signals.Select(GetCategory).Distinct().OrderBy(c => c)).ToArray();
            int categoryIndex = Array.IndexOf(categories, category);
            category = categories[EditorGUILayout.Popup("カテゴリ", Math.Max(0, categoryIndex), categories)];
            if (GUILayout.Button("一覧を更新")) Reload();
            EditorGUILayout.HelpBox("Signal名をクリックすると再生ヘッドへ追加します。ReceiverとBindingは変更しません。新規TrackのBindingはTimelineで設定してください。", MessageType.Info);
            if (!string.IsNullOrEmpty(result)) EditorGUILayout.HelpBox(result, MessageType.Info);
            bool enabled = timeline != null && window != null && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;
            scroll = EditorGUILayout.BeginScrollView(scroll);
            int shown = 0;
            foreach (var signal in signals)
            {
                string path = AssetDatabase.GetAssetPath(signal);
                if (category != "すべて" && GetCategory(signal) != category) continue;
                if ((signal.name + " " + path).IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                shown++;
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!enabled))
                    {
                        if (GUILayout.Button(new GUIContent(signal.name + "  [" + GetCategory(signal) + "]", path)))
                        {
                            try
                            {
                                var marker = AddSignal(timeline, signal, time, targetTrack, director);
                                result = marker.parent.name + " / " + signal.name + " / " + time.ToString("F3") + " 秒へ追加";
                                // Selecting the marker can make an unlocked Timeline switch assets.
                                EditorGUIUtility.PingObject(marker);
                                TimelineEditor.Refresh(RefreshReason.ContentsAddedOrRemoved);
                            }
                            catch (Exception error) { result = error.Message; Debug.LogException(error); }
                        }
                    }
                    if (GUILayout.Button("確認", GUILayout.Width(44))) EditorGUIUtility.PingObject(signal);
                }
                EditorGUILayout.LabelField(path, EditorStyles.miniLabel);
            }
            if (shown == 0) EditorGUILayout.LabelField("該当するSignalはありません。");
            EditorGUILayout.EndScrollView();
        }

        public static IEnumerable<SignalTrack> GetSignalTracks(TimelineAsset timeline)
        {
            foreach (var root in timeline.GetRootTracks())
                foreach (var track in Traverse(root))
                    if (track is SignalTrack signalTrack) yield return signalTrack;
        }

        private static IEnumerable<TrackAsset> Traverse(TrackAsset root)
        {
            yield return root;
            foreach (var child in root.GetChildTracks())
                foreach (var item in Traverse(child)) yield return item;
        }

        private static bool IsUsable(SignalTrack track)
        {
            for (TrackAsset current = track; current != null; current = current.parent as TrackAsset)
                if (current.locked || current.muted) return false;
            return true;
        }

        public static SignalEmitter AddSignal(TimelineAsset timeline, SignalAsset signal, double time,
            SignalTrack preferred = null, PlayableDirector director = null)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Play・コンパイル中は追加できません。");
            if (timeline == null || signal == null) throw new ArgumentException("TimelineとSignalを指定してください。");
            if (double.IsNaN(time) || double.IsInfinity(time) || time < 0) throw new ArgumentOutOfRangeException(nameof(time));
            if (!AssetDatabase.IsOpenForEdit(timeline)) throw new InvalidOperationException("Timelineは編集できません。");
            var tracks = GetSignalTracks(timeline).Where(IsUsable).ToList();
            if (preferred != null && !tracks.Contains(preferred))
                throw new ArgumentException("追加先Trackが別Timeline・ロック中・ミュート中です。");
            if (director != null && director.playableAsset != timeline)
                throw new ArgumentException("Directorと編集中Timelineが一致しません。");
            var target = preferred;
            if (target == null && director != null)
                target = tracks.FirstOrDefault(t => director.GetGenericBinding(t) is SignalReceiver receiver && receiver.GetRegisteredSignals().Contains(signal));
            if (target == null) target = tracks.FirstOrDefault();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add Timeline Signal");
            try
            {
                // Timeline's public creation APIs register both container and created subasset Undo.
                if (target == null) target = timeline.CreateTrack<SignalTrack>(null, "Signals");
                var marker = target.CreateMarker<SignalEmitter>(time);
                Undo.RecordObject(marker, "Assign Signal");
                marker.asset = signal;
                EditorUtility.SetDirty(marker);
                EditorUtility.SetDirty(target);
                EditorUtility.SetDirty(timeline);
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(group);
                return marker;
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
        }
    }
}
