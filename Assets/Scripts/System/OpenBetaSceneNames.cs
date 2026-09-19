using System;

namespace Nekolpos.System
{
    public static class OpenBetaSceneNames
    {
        public const string Title = "TitleScene";
        public const string EventTimelineProduction = "TitleScene_NerukoTailTest";
        public const string EventTimelineProductionPath = "Assets/Scenes/TitleScene_NerukoTailTest.unity";
        public const string PvMorning = "TitleScene_PV_Morning";

        public static bool IsTitleLikeScene(string sceneName)
        {
            return string.Equals(sceneName, Title, StringComparison.Ordinal) ||
                   string.Equals(sceneName, EventTimelineProduction, StringComparison.Ordinal);
        }

        public static bool IsPvMorningScene(string sceneName)
        {
            return string.Equals(sceneName, PvMorning, StringComparison.Ordinal);
        }
    }
}
