using Unity.Netcode;
using UnityEngine;

public class FirstPersonLook : MonoBehaviour
{
    [SerializeField] Transform character;
    public float sensitivity = 2;
    public float smoothing = 1.5f;
    public bool lockCursor = true;

    Vector2 velocity;
    Vector2 frameVelocity;

    void Reset()
    {
        // Get the character from the FirstPersonMovement in parents.
        character = GetComponentInParent<FirstPersonMovement>().transform;
    }

    void Start()
    {
        ApplyCursorLock();
    }

    void OnEnable()
    {
        ApplyCursorLock();
    }

    void OnDisable()
    {
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    void Update()
    {
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsListening)
            return;

        ApplyCursorLock();

        Vector2 mouseDelta = MiniFirstPersonInput.LookDelta * 0.05f;
        Vector2 rawFrameVelocity = Vector2.Scale(mouseDelta, Vector2.one * sensitivity);
        frameVelocity = Vector2.Lerp(frameVelocity, rawFrameVelocity, 1 / smoothing);
        velocity += frameVelocity;
        velocity.y = Mathf.Clamp(velocity.y, -90, 90);

        transform.localRotation = Quaternion.AngleAxis(-velocity.y, Vector3.right);
        character.localRotation = Quaternion.AngleAxis(velocity.x, Vector3.up);
    }

    void ApplyCursorLock()
    {
        if (!lockCursor)
            return;

        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsListening)
            return;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
