using Destruxion.Multiplayer;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Destruxion.Editor.Multiplayer
{
    [InitializeOnLoad]
    public static class SimpleMultiplayerSetupEditor
    {
        const string PlayerPrefabPath = "Assets/Local Player.prefab";
        const string SetupKey = "Destruxion.SimpleMultiplayerSetup.v1";
        const string MenuPath = "Tools/DESTRUXion/Setup Simple Multiplayer";

        static SimpleMultiplayerSetupEditor()
        {
            EditorApplication.delayCall += AutoSetupOnce;
        }

        [MenuItem(MenuPath)]
        public static void SetupSimpleMultiplayer()
        {
            var playerPrefab = SetupPlayerPrefab();
            SetupActiveScene(playerPrefab);
            EditorPrefs.SetBool(SetupKey, true);
            Debug.Log("DESTRUXion simple multiplayer setup complete.");
        }

        static void AutoSetupOnce()
        {
            if (EditorPrefs.GetBool(SetupKey, false))
                return;

            if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) == null)
                return;

            SetupSimpleMultiplayer();
        }

        static GameObject SetupPlayerPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"Could not find player prefab at {PlayerPrefabPath}.");
                return null;
            }

            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                if (root.GetComponent<NetworkObject>() == null)
                    root.AddComponent<NetworkObject>();

                if (root.GetComponent<NetworkPlayerOwnerGate>() == null)
                    root.AddComponent<NetworkPlayerOwnerGate>();

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        }

        static void SetupActiveScene(GameObject playerPrefab)
        {
            if (playerPrefab == null)
                return;

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return;

            var bootstrap = Object.FindAnyObjectByType<SimpleMultiplayerBootstrap>();
            if (bootstrap == null)
            {
                var bootstrapObject = new GameObject("Simple Multiplayer Bootstrap");
                bootstrap = bootstrapObject.AddComponent<SimpleMultiplayerBootstrap>();
            }

            var serializedBootstrap = new SerializedObject(bootstrap);
            serializedBootstrap.FindProperty("playerPrefab").objectReferenceValue = playerPrefab;
            serializedBootstrap.FindProperty("connectAddress").stringValue = "127.0.0.1";
            serializedBootstrap.FindProperty("port").intValue = 7777;
            serializedBootstrap.FindProperty("showRuntimeButtons").boolValue = true;
            serializedBootstrap.ApplyModifiedProperties();

            EditorUtility.SetDirty(bootstrap);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!string.IsNullOrEmpty(scene.path))
                EditorSceneManager.SaveScene(scene);
        }
    }
}
