using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

public class ThirdPersonSmoothCamera : MonoBehaviour
{
    [Header("Target")]
    public Vector3 offset = new Vector3(0f, 1.6f, -3.2f);

    [Header("Follow smoothing")]
    public float followSmoothTime = 0.12f;
    Vector3 followVelocity;

    [Header("Orbit")]
    public InputActionReference lookAction;
    public InputActionReference aimAction;
    public float sensitivity = 1.8f;
    public float pitchMin = -30f;
    public float pitchMax = 60f;
    public float distanceMin = 1.2f;
    public float distanceMax = 4.5f;
    public float zoomSpeed = 2f;

    [Header("Control Lock")]
    public bool freezeCamera = false;

    float yaw;
    float pitch;
    float currentDistance;

    [Header("Collision")]
    public LayerMask collisionMask = ~0;
    public float collisionRadius = 0.2f;
    public float collisionOffset = 0.2f;

    [Header("Auto-Find Settings")]
    [Tooltip("Automatically find local player on start")]
    public bool autoFindLocalPlayer = true;

    [Tooltip("Tag to search for when finding player")]
    public string playerTag = "Player";

    private Transform target;
    private bool isSetup = false;

    void Start()
    {
        currentDistance = -offset.z;

        Vector3 angles = transform.eulerAngles;
        yaw = angles.y;
        pitch = angles.x;

        lookAction?.action?.Enable();
        aimAction?.action?.Enable();

        if (autoFindLocalPlayer)
        {
            // Try to find local player immediately
            FindLocalPlayer();

            // If not found, keep trying
            if (target == null)
            {
                InvokeRepeating(nameof(FindLocalPlayer), 0.5f, 0.5f);
            }
        }
    }

    void FindLocalPlayer()
    {
        // Find all objects with player tag
        GameObject[] players = GameObject.FindGameObjectsWithTag(playerTag);

        foreach (GameObject playerObj in players)
        {
            // Check if this player has NetworkBehaviour and is the local player
            NetworkBehaviour netBehaviour = playerObj.GetComponent<NetworkBehaviour>();

            if (netBehaviour != null && netBehaviour.isLocalPlayer)
            {
                // Found our local player!
                SetTarget(playerObj.transform);
                CancelInvoke(nameof(FindLocalPlayer));
                return;
            }
        }
    }

    public void SetTarget(Transform newTarget)
    {
        // Find camera pivot first
        Transform pivot = newTarget.Find("CameraPivot");
        target = pivot != null ? pivot : newTarget;

        // Reset orientation to default view
        ResetCameraOrientation();

        isSetup = true;
        Debug.Log($"[NetworkedCamera] Target set to {target.name} (camera reset)");
    }

    void OnDisable()
    {
        lookAction?.action?.Disable();
        aimAction?.action?.Disable();
    }

    void LateUpdate()
    {
        if (!isSetup || target == null) return;

        if (target == null)
        {
            // Try to find player again if we lost reference
            if (autoFindLocalPlayer && !IsInvoking(nameof(FindLocalPlayer)))
            {
                InvokeRepeating(nameof(FindLocalPlayer), 0.5f, 0.5f);
            }
            return;
        }

        // Read look only when aim button is held
        bool aiming = aimAction != null && aimAction.action.ReadValue<float>() > 0.5f;
        if (aiming && lookAction != null)
        {
            Vector2 look = lookAction.action.ReadValue<Vector2>();
            yaw += look.x * sensitivity;
            pitch -= look.y * sensitivity;
            pitch = Mathf.Clamp(pitch, pitchMin, pitchMax);
        }

        // desired camera position in world
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 desiredLocal = rot * new Vector3(0f, 0f, -currentDistance);
        Vector3 desiredWorld = (target.position + Vector3.up * offset.y) + desiredLocal + Vector3.up * (offset.y - 0f);

        // collision: cast from pivot to desiredWorld
        Vector3 pivot = target.position + Vector3.up * offset.y;
        Vector3 dir = (desiredWorld - pivot).normalized;
        float desiredDist = Vector3.Distance(pivot, desiredWorld);
        RaycastHit hit;
        float correctedDist = desiredDist;
        if (Physics.SphereCast(pivot, collisionRadius, dir, out hit, desiredDist + collisionOffset, collisionMask, QueryTriggerInteraction.Ignore))
        {
            correctedDist = Mathf.Max(0.5f, hit.distance - collisionOffset);
        }

        Vector3 correctedWorld = pivot + dir * correctedDist;

        // smooth follow
        transform.position = Vector3.SmoothDamp(transform.position, correctedWorld, ref followVelocity, followSmoothTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, rot, 1f - Mathf.Exp(-12f * Time.deltaTime));
    }

    void ResetCameraOrientation()
    {
        // A comfortable default behind the player
        yaw = target.eulerAngles.y; // look where the player faces
        pitch = 15f;                // slight downward angle
        currentDistance = -offset.z;

        // Instantly place the camera without smoothing lag
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 desiredLocal = rot * new Vector3(0f, 0f, -currentDistance);
        Vector3 desiredWorld = target.position + Vector3.up * offset.y + desiredLocal;
        transform.SetPositionAndRotation(desiredWorld, rot);

        followVelocity = Vector3.zero; // clear damping memory
    }

    void OnDrawGizmos()
    {
        if (target != null)
        {
            Gizmos.color = Color.cyan;
            Vector3 pivot = target.position + Vector3.up * offset.y;
            Gizmos.DrawWireSphere(pivot, 0.2f);
            Gizmos.DrawLine(pivot, transform.position);
        }
    }
}