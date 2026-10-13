using Nekolpos.System;
using UnityEditor;
using UnityEngine;

namespace Nekolpos.EditorTools
{
    public static class UnityRoomTeachingRankingSetup
    {
        private const string ReporterObjectName = "UnityRoomTeachingRankingReporter";

        public static void SetupReporter()
        {
            UnityRoomTeachingRankingReporter reporter = Object.FindFirstObjectByType<UnityRoomTeachingRankingReporter>();
            if (reporter == null)
            {
                GameObject reporterObject = new GameObject(ReporterObjectName);
                Undo.RegisterCreatedObjectUndo(reporterObject, "Create UnityRoom Teaching Ranking Reporter");
                reporter = reporterObject.AddComponent<UnityRoomTeachingRankingReporter>();
            }

            EditorUtility.SetDirty(reporter);
            Debug.Log("[UnityRoomTeachingRankingSetup] UnityRoomTeachingRankingReporter is ready in the active scene.", reporter);
        }
    }
}
