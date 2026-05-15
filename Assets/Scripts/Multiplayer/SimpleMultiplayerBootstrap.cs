using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Destruxion.Multiplayer
{
    [DisallowMultipleComponent]
    [AddComponentMenu("DESTRUXion/Simple Multiplayer Bootstrap")]
    public sealed class SimpleMultiplayerBootstrap : MonoBehaviour
    {
        [SerializeField] GameObject playerPrefab;
        [SerializeField] string connectAddress = "127.0.0.1";
        [SerializeField] ushort port = 7777;
        [SerializeField] bool showRuntimeButtons = true;
        [SerializeField] Vector2 buttonPosition = new(16f, 16f);
        [SerializeField] Vector2 buttonSize = new(150f, 34f);

        NetworkManager runtimeManager;
        UnityTransport runtimeTransport;
        string status = "Choose Host or Client";

        void Awake()
        {
            EnsureNetworkManager();
        }

        void Update()
        {
            EnsureNetworkManager();
            if (runtimeManager == null)
                return;

            if (!runtimeManager.IsListening)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;

                if (ShortcutPressed(KeyCode.H))
                    StartHost();
                else if (ShortcutPressed(KeyCode.C))
                    StartClient();
                else if (ShortcutPressed(KeyCode.S))
                    StartServer();
            }
            else if (ShortcutPressed(KeyCode.Escape))
            {
                Shutdown();
            }
        }

        void OnGUI()
        {
            if (!showRuntimeButtons)
                return;

            EnsureNetworkManager();
            if (runtimeManager == null)
                return;

            var x = buttonPosition.x;
            var y = buttonPosition.y;
            var width = buttonSize.x;
            var height = buttonSize.y;

            if (!runtimeManager.IsListening)
            {
                GUI.Box(new Rect(x, y, width + 110f, height), status);
                if (GUI.Button(new Rect(x, y + height + 8f, width, height), "Host (H)"))
                    StartHost();

                if (GUI.Button(new Rect(x, y + (height + 8f) * 2f, width, height), "Client (C)"))
                    StartClient();

                if (GUI.Button(new Rect(x, y + (height + 8f) * 3f, width, height), "Server (S)"))
                    StartServer();
            }
            else
            {
                var mode = runtimeManager.IsHost ? "Host" : runtimeManager.IsServer ? "Server" : "Client";
                GUI.Box(new Rect(x, y, width + 110f, height), $"Netcode: {mode}");
                if (GUI.Button(new Rect(x, y + height + 8f, width, height), "Shutdown"))
                    Shutdown();
            }
        }

        public void StartHost()
        {
            if (!PrepareToStart())
                return;

            SetStatus(runtimeManager.StartHost() ? "Host started" : "Host failed. Check Console.");
        }

        public void StartClient()
        {
            if (!PrepareToStart())
                return;

            SetStatus(runtimeManager.StartClient() ? $"Client connecting to {connectAddress}:{port}" : "Client failed. Check Console.");
        }

        public void StartServer()
        {
            if (!PrepareToStart())
                return;

            SetStatus(runtimeManager.StartServer() ? "Server started" : "Server failed. Check Console.");
        }

        public void Shutdown()
        {
            EnsureNetworkManager();
            if (runtimeManager == null)
                return;

            runtimeManager.Shutdown();
            SetStatus("Shutdown complete");
        }

        bool PrepareToStart()
        {
            EnsureNetworkManager();
            if (runtimeManager == null)
            {
                SetStatus("No NetworkManager");
                return false;
            }

            if (playerPrefab == null)
            {
                SetStatus("Missing player prefab");
                Debug.LogError("Simple Multiplayer Bootstrap needs a player prefab assigned.", this);
                return false;
            }

            runtimeTransport.SetConnectionData(connectAddress, port);
            runtimeManager.NetworkConfig.PlayerPrefab = playerPrefab;
            runtimeManager.NetworkConfig.AutoSpawnPlayerPrefabClientSide = false;
            runtimeManager.NetworkConfig.EnableSceneManagement = false;

            try
            {
                runtimeManager.AddNetworkPrefab(playerPrefab);
            }
            catch (System.Exception)
            {
                // Netcode throws if the prefab is already registered. That is fine for repeated tests.
            }

            return true;
        }

        void EnsureNetworkManager()
        {
            runtimeManager = NetworkManager.Singleton;
            if (runtimeManager == null)
            {
                var managerObject = new GameObject("Network Manager");
                runtimeManager = managerObject.AddComponent<NetworkManager>();
                runtimeTransport = managerObject.AddComponent<UnityTransport>();
                runtimeManager.NetworkConfig = new NetworkConfig();
                runtimeManager.NetworkConfig.NetworkTransport = runtimeTransport;
            }
            else
            {
                runtimeTransport = runtimeManager.GetComponent<UnityTransport>();
                if (runtimeTransport == null)
                    runtimeTransport = runtimeManager.gameObject.AddComponent<UnityTransport>();

                if (runtimeManager.NetworkConfig == null)
                    runtimeManager.NetworkConfig = new NetworkConfig();

                runtimeManager.NetworkConfig.NetworkTransport = runtimeTransport;
            }

            if (playerPrefab != null)
                runtimeManager.NetworkConfig.PlayerPrefab = playerPrefab;
        }

        void SetStatus(string message)
        {
            status = message;
            Debug.Log($"Simple Multiplayer: {message}", this);
        }

        static bool ShortcutPressed(KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return false;

            return key switch
            {
                KeyCode.H => keyboard.hKey.wasPressedThisFrame,
                KeyCode.C => keyboard.cKey.wasPressedThisFrame,
                KeyCode.S => keyboard.sKey.wasPressedThisFrame,
                KeyCode.Escape => keyboard.escapeKey.wasPressedThisFrame,
                _ => false
            };
#else
            return Input.GetKeyDown(key);
#endif
        }
    }
}
