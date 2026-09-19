using System;
using System.Reflection;
using UnityEngine;

namespace Nekolpos.System
{
    public sealed class UnityRoomTeachingRankingReporter : MonoBehaviour
    {
        [SerializeField] private bool submitOnStart = true;
        [SerializeField] private bool submitWhenChanged = true;
        [SerializeField] private int catSeriesBoardNo = 1;
        [SerializeField] private int catExpressionBoardNo = 2;
        [SerializeField] private string scoreboardWriteMode = "Always";

        private bool warnedMissingClient;

        private void OnEnable()
        {
            TeachingDiscoveryStore.DiscoveryChanged += HandleDiscoveryChanged;
        }

        private void Start()
        {
            if (submitOnStart)
            {
                SubmitCurrentScores();
            }
        }

        private void OnDisable()
        {
            TeachingDiscoveryStore.DiscoveryChanged -= HandleDiscoveryChanged;
        }

        public void SubmitCurrentScores()
        {
            SubmitScore(catSeriesBoardNo, TeachingDiscoveryStore.GetCatSeriesRankingValue(), "CatSeries");
            SubmitScore(catExpressionBoardNo, TeachingDiscoveryStore.GetCatExpressionRankingValue(), "CatExpression");
        }

        private void HandleDiscoveryChanged(DiscoveryMarkResult result)
        {
            if (!submitWhenChanged)
            {
                return;
            }

            if (result.WasReset)
            {
                SubmitCurrentScores();
                return;
            }

            if (result.Category == TeachingCategory.CatSeries && result.SeriesWasNew)
            {
                SubmitScore(catSeriesBoardNo, TeachingDiscoveryStore.GetCatSeriesRankingValue(), "CatSeries");
            }

            if (result.Category == TeachingCategory.CatExpression && result.EntryWasNew)
            {
                SubmitScore(catExpressionBoardNo, TeachingDiscoveryStore.GetCatExpressionRankingValue(), "CatExpression");
            }
        }

        private void SubmitScore(int boardNo, int score, string label)
        {
            if (boardNo <= 0)
            {
                Debug.LogWarning($"[UnityRoomTeachingRankingReporter] {label} の boardNo が未設定です。");
                return;
            }

            if (!TrySendUnityRoomScore(boardNo, score))
            {
                WarnMissingClientOnce();
            }
        }

        private bool TrySendUnityRoomScore(int boardNo, int score)
        {
            Type clientType = FindType("unityroom.Api.UnityroomApiClient");
            Type writeModeType = FindType("unityroom.Api.ScoreboardWriteMode");
            if (clientType == null || writeModeType == null)
            {
                return false;
            }

            PropertyInfo instanceProperty = clientType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            object instance = instanceProperty != null ? instanceProperty.GetValue(null) : null;
            if (instance == null)
            {
                return false;
            }

            object writeMode;
            try
            {
                writeMode = Enum.Parse(writeModeType, scoreboardWriteMode, true);
            }
            catch (ArgumentException)
            {
                writeMode = Enum.Parse(writeModeType, "Always", true);
            }

            MethodInfo sendScore = clientType.GetMethod(
                "SendScore",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(int), typeof(float), writeModeType },
                null);
            if (sendScore == null)
            {
                return false;
            }

            sendScore.Invoke(instance, new object[] { boardNo, (float)score, writeMode });
            return true;
        }

        private void WarnMissingClientOnce()
        {
            if (warnedMissingClient)
            {
                return;
            }

            warnedMissingClient = true;
            Debug.LogWarning("[UnityRoomTeachingRankingReporter] unityroom-client-library が見つかりません。パッケージ導入後、UnityroomApiClient Prefab をシーンに配置してください。");
        }

        private static Type FindType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(fullName);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }
    }
}
