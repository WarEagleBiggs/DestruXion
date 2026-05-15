using Unity.Netcode;
using UnityEngine;

namespace Destruxion.Multiplayer
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkPlayerOwnerGate : NetworkBehaviour
    {
        [SerializeField] float remotePositionLerp = 18f;
        [SerializeField] float remoteRotationLerp = 18f;

        readonly NetworkVariable<Vector3> networkPosition = new(
            Vector3.zero,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        readonly NetworkVariable<Quaternion> networkRotation = new(
            Quaternion.identity,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        Behaviour[] ownerOnlyBehaviours;
        Camera[] cameras;
        AudioListener[] audioListeners;
        Rigidbody body;
        bool hasSpawned;

        void Awake()
        {
            CacheReferences();
        }

        public override void OnNetworkSpawn()
        {
            CacheReferences();
            hasSpawned = true;

            if (IsOwner)
            {
                networkPosition.Value = transform.position;
                networkRotation.Value = transform.rotation;
            }

            ApplyOwnershipState();
        }

        public override void OnNetworkDespawn()
        {
            hasSpawned = false;
        }

        void Update()
        {
            if (!hasSpawned)
                return;

            if (IsOwner)
            {
                networkPosition.Value = transform.position;
                networkRotation.Value = transform.rotation;
                return;
            }

            var positionT = 1f - Mathf.Exp(-remotePositionLerp * Time.deltaTime);
            var rotationT = 1f - Mathf.Exp(-remoteRotationLerp * Time.deltaTime);
            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, networkPosition.Value, positionT),
                Quaternion.Slerp(transform.rotation, networkRotation.Value, rotationT));
        }

        void OnEnable()
        {
            if (hasSpawned)
                ApplyOwnershipState();
        }

        void CacheReferences()
        {
            body = GetComponent<Rigidbody>();
            cameras = GetComponentsInChildren<Camera>(true);
            audioListeners = GetComponentsInChildren<AudioListener>(true);

            ownerOnlyBehaviours = new Behaviour[]
            {
                GetComponent<FirstPersonMovement>(),
                GetComponent<Jump>(),
                GetComponent<Crouch>(),
                GetComponent<FirstPersonShoot>(),
                GetComponentInChildren<FirstPersonLook>(true),
                GetComponentInChildren<Zoom>(true),
                GetComponentInChildren<FirstPersonAudio>(true)
            };
        }

        void ApplyOwnershipState()
        {
            var isLocalPlayer = IsOwner;

            for (var i = 0; i < ownerOnlyBehaviours.Length; i++)
            {
                if (ownerOnlyBehaviours[i] != null)
                    ownerOnlyBehaviours[i].enabled = isLocalPlayer;
            }

            for (var i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null)
                    cameras[i].enabled = isLocalPlayer;
            }

            for (var i = 0; i < audioListeners.Length; i++)
            {
                if (audioListeners[i] != null)
                    audioListeners[i].enabled = isLocalPlayer;
            }

            if (body != null)
            {
                body.isKinematic = !isLocalPlayer;
                body.detectCollisions = true;
            }
        }
    }
}
