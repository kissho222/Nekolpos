#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using Nekolpos.StatusSystem;
using Nekolpos.TimeSystem;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Nekolpos.System
{
    [Serializable]
    public sealed class DebugConsoleSnapshot
    {
        [Serializable] public sealed class StatusValue { public string type; public int value; }
        public int version = 1;
        public string sessionId, scenarioId, correlationId;
        public float elapsed;
        public int? day;
        public string date, period, weather, tomorrowWeather, area, home, representative;
        public string gameState;
        public bool? walk, tablet, transport, paused, nightDiary, diaryTransition, chatInput, chatTyping, chatDialogue, timeFlow;
        public bool settings;
        public bool? chatChoices, chatLog, diaryWindow, wordLearning;
        public string conversationPattern;
        public int? conversationLine, conversationCount;
        public float timeScale;
        public string cursorLock;
        public bool cursorVisible;
        public float[] playerPosition, catPosition, cameraPosition;
        public int consoleCount, canvasCount;
        public List<StatusValue> statuses = new();
        public static DebugConsoleSnapshot Capture()
        {
            var s = new DebugConsoleSnapshot { sessionId = DebugConsoleTrace.SessionId, scenarioId = DebugConsoleTrace.ScenarioId,
                correlationId = DebugConsoleTrace.CorrelationId, elapsed = Time.realtimeSinceStartup,
                settings = SettingsPanel.IsOpen, timeScale = Time.timeScale, cursorLock = Cursor.lockState.ToString(), cursorVisible = Cursor.visible };
            var time = Object.FindFirstObjectByType<TimeManager>();
            if (time != null && time.IsInitialized) { s.day = time.CurrentDay; s.date = time.GetCalendarDate(time.CurrentDay).ToString("yyyy-MM-dd"); s.period = time.CurrentPeriod.ToString(); s.weather = time.Weather.ToString(); s.tomorrowWeather = time.TomorrowWeather.ToString(); }
            var status = Object.FindFirstObjectByType<StatusManager>();
            if (status != null && status.IsInitialized) { foreach (StatusType type in Enum.GetValues(typeof(StatusType))) s.statuses.Add(new StatusValue { type = type.ToString(), value = status.GetValue(type) }); s.representative = status.GetRepresentativeState().DisplayName; }
            s.area = Object.FindFirstObjectByType<PlayerAreaTracker>()?.CurrentAreaId;
            s.home = Object.FindFirstObjectByType<CatPresentationModeController>()?.PositionController?.CurrentHomeLocation.ToString();
            s.gameState = GameManager.Instance?.CurrentState?.GetType().Name;
            var w = Object.FindFirstObjectByType<PlayerWalkModeController>(); if (w != null) s.walk = w.IsWalkMode;
            if (w != null) s.playerPosition = Coordinates(w.transform.position);
            var position = Object.FindFirstObjectByType<CatPositionController>(); if (position != null) s.catPosition = Coordinates(position.transform.position);
            var camera = w != null && w.IsWalkMode ? w.WalkCamera : Camera.main;
            if (camera != null) s.cameraPosition = Coordinates(camera.transform.position);
            s.consoleCount = Object.FindObjectsByType<NekolposDebugConsole>(FindObjectsSortMode.None).Length;
            s.canvasCount = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            var b = Object.FindFirstObjectByType<NekolposGameBootstrap>(); if (b != null) { s.tablet = b.IsTabletCameraViewActive; s.transport = b.IsTransporting; }
            var p = Object.FindFirstObjectByType<OpenBetaPauseMenuController>(); if (p != null) s.paused = p.IsPaused;
            var d = Object.FindFirstObjectByType<DiaryCalendarController>(); if (d != null) { s.nightDiary = d.IsNightDiaryActive; s.diaryTransition = d.IsWritingOrTransitioning; }
            if (d != null) s.diaryWindow = d.IsCalendarWindowOpen;
            var c = Object.FindFirstObjectByType<ChatUIController>(); if (c != null) { s.chatInput = c.IsControlGuideInputActive; s.chatTyping = c.IsTyping; s.chatDialogue = c.IsInDialogueMode; }
            if (c != null) { s.chatChoices = c.HasOpenChoices; s.chatLog = c.IsLogWindowOpen; }
            var learning = Object.FindFirstObjectByType<Nekolpos.Dialogue.UnknownWord.WordLearningController>(); if (learning != null) s.wordLearning = learning.IsActive;
            if (DialogueManager.Instance != null)
            { s.timeFlow = DialogueManager.Instance.IsTimeFlowRunning; s.conversationPattern = DebugConsoleTrace.SafeId(DialogueManager.Instance.DebugCurrentPatternId); s.conversationLine = DialogueManager.Instance.DebugCurrentLineIndex; s.conversationCount = DialogueManager.Instance.DebugConversationCount; }
            return s;
        }
        public string ToJson() => Newtonsoft.Json.JsonConvert.SerializeObject(this, Newtonsoft.Json.Formatting.Indented);
        private static float[] Coordinates(Vector3 position) => new[] { position.x, position.y, position.z };
    }
}
#endif
