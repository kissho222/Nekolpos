using System;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Transport.Transports;
using UnityEditor;
using UnityEngine;

namespace Nekolpos.EditorTools
{
    [InitializeOnLoad]
    internal static class NekolposUnityMcpAutoStart
    {
        private const string AutoStartEnabledKey = "Nekolpos.UnityMcp.AutoStartEnabled";
        private const string UseHttpTransportKey = "MCPForUnity.UseHttpTransport";

        static NekolposUnityMcpAutoStart()
        {
            if (!EditorPrefs.GetBool(AutoStartEnabledKey, true))
            {
                return;
            }

            EditorApplication.delayCall += StartWhenEditorIsReady;
        }

        private static void EnableAutoStart()
        {
            EditorPrefs.SetBool(AutoStartEnabledKey, true);
            StartWhenEditorIsReady();
        }

        private static void DisableAutoStart()
        {
            EditorPrefs.SetBool(AutoStartEnabledKey, false);
        }

        private static void StartBridgeNow()
        {
            StartWhenEditorIsReady();
        }

        private static void StartWhenEditorIsReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += StartWhenEditorIsReady;
                return;
            }

            try
            {
                EditorPrefs.SetBool(UseHttpTransportKey, false);
                EditorConfigurationCache.Instance.SetUseHttpTransport(false);

                StdioBridgeHost.StartAutoConnect();

                if (!StdioBridgeHost.IsRunning)
                {
                    Debug.LogWarning("[NekolposUnityMcpAutoStart] MCP bridge failed to start.");
                    return;
                }

                Debug.Log($"[NekolposUnityMcpAutoStart] MCP bridge ready on port {StdioBridgeHost.GetCurrentPort()}.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[NekolposUnityMcpAutoStart] Failed to start MCP bridge: {exception.Message}");
            }
        }
    }
}
