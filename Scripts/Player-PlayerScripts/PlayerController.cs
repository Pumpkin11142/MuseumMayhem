using Mirror;
 using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.UI;

[RequireComponent(typeof(CharacterController))]
public class PlayerParkourController : NetworkBehaviour
{
    [Header("Input (assign InputActionReferences)")]
    public InputActionReference moveAction;
    public InputActionReference jumpAction;
    public InputActionReference sprintAction;
    public InputActionReference crouchAction;

    [Header("References")]
    public Transform cameraTransform;
    public Transform cameraPivot;
    public Transform groundCheck;
    public Slider staminaSlider;

    [Header("Movement")]
    public float walkSpeed = 4f;
    public float sprintSpeed = 8f;
    public float acceleration = 12f;
    public float deceleration = 16f;
    private Vector3 lastMoveDir;
    public float rotationSmoothTime = 0.08f;

    [Header("Jump / Gravity")]
    public float gravity = -20f;
    public float jumpForce = 7f;
    public float coyoteTime = 0.15f;
    public float jumpBufferTime = 0.12f;

    [Header("Vaulting")]
    public float maxVaultHeight = 1.2f;
    public float minVaultHeight = 0.3f;
    public float vaultForwardDistance = 1.2f;
    public float vaultDuration = 0.38f;
    public LayerMask obstacleLayers;
    public enum GroundMaskMode
    {
        Exclude,
        Include
    }

    [Header("Ground Check")]
    public float groundCheckRadius = 0.25f;
    [Tooltip("Whether the Ground Layers mask lists layers to exclude (legacy) or include when probing for ground")]
    public GroundMaskMode groundMaskMode = GroundMaskMode.Exclude;
    [FormerlySerializedAs("excludedGroundLayers")]
    public LayerMask groundLayers;

    [Header("Stamina")]
    public float maxStamina = 100f;
    public float staminaRegenRate = 15f;
    public float sprintDrainRate = 20f;
    public float jumpCost = 10f;
    public float vaultCost = 15f;
    public float regenDelay = 1.5f;

    [Header("External Modifiers")]
    [Tooltip("If true, player cannot move or rotate (used by relic extraction)")]
    public bool freezeMovement = false;

    [Tooltip("Multiplier applied to movement speed (used for heavy items like statues)")]
    [Range(0.1f, 1f)] public float speedMultiplier = 1f;

    [Tooltip("Extra stamina drain multiplier when carrying heavy objects")]
    public float carryStaminaMultiplier = 1.5f;

    [Tooltip("Jump force multiplier when carrying heavy objects")]
    public float carryJumpMultiplier = 0.75f;

    public LayerMask defaultExcludeLayers;
    public LayerMask carryExcludeLayers;

    // Synced variables
    [SyncVar]
    float stamina;
    [SyncVar]
    float lastStaminaUseTime;
    [SyncVar]
    bool isVaulting;

    // internals
    public CharacterController cc;
    Vector3 velocity;
    float verticalVelocity;
    float currentSpeed;
    float speedVelocity;
    float turnSmoothVelocity;
    float lastGroundedTime;
    float lastJumpPressedTime;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        stamina = maxStamina;

        defaultExcludeLayers = cc.excludeLayers;

        // Create a mask that also ignores the Statue layer
        int statueLayer = LayerMask.NameToLayer("Statue");
        carryExcludeLayers = defaultExcludeLayers | (1 << statueLayer);
    }


    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        // Enable input only for local player
        moveAction?.action?.Enable();
        jumpAction?.action?.Enable();
        sprintAction?.action?.Enable();
        crouchAction?.action?.Enable();

        // Setup camera for local player
        if (Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
    }

    void OnDisable()
    {
        if (isLocalPlayer)
        {
            moveAction?.action?.Disable();
            jumpAction?.action?.Disable();
            sprintAction?.action?.Disable();
            crouchAction?.action?.Disable();
        }
    }

    void Update()
    {
        // Only local player processes input
        if (!isLocalPlayer)
        {
            // Remote players just update UI
            UpdateStaminaUI();
            return;
        }

        // Freeze player input if relic or cutscene is active
        if (freezeMovement)
        {
            UpdateStaminaUI(); // keep UI updating
            return;
        }

        if (isVaulting)
        {
            ApplyGravity();
            cc.Move(velocity * Time.deltaTime);
            UpdateStaminaUI();
            return;
        }

        bool grounded = IsGrounded();
        if (grounded)
            lastGroundedTime = Time.time;

        Vector2 rawMove = moveAction != null ? moveAction.action.ReadValue<Vector2>() : Vector2.zero;
        Vector3 moveInput = new Vector3(rawMove.x, 0f, rawMove.y);

        bool sprintHeld = sprintAction != null && sprintAction.action.ReadValue<float>() > 0.5f;
        bool jumpPressed = jumpAction != null && jumpAction.action.WasPressedThisFrame();

        if (jumpPressed)
            lastJumpPressedTime = Time.time;

        // handle stamina drain for sprint
        bool isSprinting = sprintHeld && stamina > 0.1f && moveInput.magnitude > 0.1f;
        if (isSprinting)
        {
            float scaledDrain = sprintDrainRate * carryStaminaMultiplier * Time.deltaTime;
            CmdUseStamina(scaledDrain);
        }

        // ===== MOVEMENT DIRECTION =====
        Vector3 camForward = cameraTransform != null ? Vector3.Scale(cameraTransform.forward, new Vector3(1, 0, 1)).normalized : Vector3.forward;
        Vector3 camRight = cameraTransform != null ? cameraTransform.right : Vector3.right;
        Vector3 inputDir = (camRight * moveInput.x + camForward * moveInput.z).normalized;

        // Store last movement direction for deceleration
        if (inputDir.sqrMagnitude > 0.001f)
            lastMoveDir = inputDir;

        // Determine target speed
        float baseSpeed = (moveInput.magnitude > 0.1f) ? (isSprinting ? sprintSpeed : walkSpeed) : 0f;
        float targetSpeed = baseSpeed * speedMultiplier;

        // Choose accel/decel
        float rate = (targetSpeed > currentSpeed) ? acceleration : deceleration;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, rate * Time.deltaTime);

        // Movement
        Vector3 horizontalVel = (moveInput.magnitude > 0.1f) ? inputDir * currentSpeed : lastMoveDir * currentSpeed;

        // rotation smoothing
        if (inputDir.sqrMagnitude > 0.001f)
        {
            float targetAngle = Mathf.Atan2(inputDir.x, inputDir.z) * Mathf.Rad2Deg;
            float smoothAngle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref turnSmoothVelocity, rotationSmoothTime);
            transform.rotation = Quaternion.Euler(0f, smoothAngle, 0f);
        }

        // vaulting
        if ((Time.time - lastJumpPressedTime) <= jumpBufferTime && TryVault(out Vector3 vaultTarget))
        {
            if (stamina >= vaultCost)
            {
                CmdUseStamina(vaultCost);
                CmdStartVault(vaultTarget);
                lastJumpPressedTime = -999f;
                return;
            }
        }

        // jumping
        bool wantJump = (Time.time - lastJumpPressedTime) <= jumpBufferTime;
        bool canJump = (Time.time - lastGroundedTime) <= coyoteTime;
        if (wantJump && canJump && stamina >= jumpCost)
        {
            verticalVelocity = jumpForce * carryJumpMultiplier;
            CmdUseStamina(jumpCost);
            lastJumpPressedTime = -999f;
        }

        ApplyGravity();
        velocity = horizontalVel + Vector3.up * verticalVelocity;
        cc.Move(velocity * Time.deltaTime);

        RegenerateStamina();
        UpdateStaminaUI();
    }

    void ApplyGravity()
    {
        if (IsGrounded() && verticalVelocity <= 0f)
            verticalVelocity = -2f;
        else
            verticalVelocity += gravity * Time.deltaTime;
    }

    bool IsGrounded()
    {
        // how far below the feet we consider "ground"
        const float castDistance = 0.15f;

        // start slightly above to avoid starting inside geometry
        Vector3 origin = groundCheck.position + Vector3.up * 0.05f;

        int includeMask;

        if (groundMaskMode == GroundMaskMode.Include)
        {
            includeMask = groundLayers.value;
            if (includeMask == 0)
            {
                includeMask = Physics.DefaultRaycastLayers;
            }
        }
        else
        {
            includeMask = ~groundLayers.value;
        }

        if (Physics.SphereCast(
                origin,
                groundCheckRadius,
                Vector3.down,
                out RaycastHit hit,
                castDistance,
                includeMask,
                QueryTriggerInteraction.Ignore))
        {
            // ignore carryables explicitly (in case they share layers)
            if (hit.collider.GetComponentInParent<StatueTreasure>() != null)
                return false;

            // only treat reasonably-flat surfaces as ground
            if (hit.normal.y >= 0.2f)  // ~78° max slope; tune as needed
                return true;
        }

        // Fallback to CharacterController grounding in case physics query misses due to setup
        if (cc != null && cc.isGrounded)
            return true;

        return false;
    }

    // ====== Stamina Network Commands ======
    [Command]
    void CmdUseStamina(float amount)
    {
        stamina = Mathf.Max(0f, stamina - amount);
        lastStaminaUseTime = Time.time;
    }

    void RegenerateStamina()
    {
        if (!isLocalPlayer) return;

        if (Time.time - lastStaminaUseTime >= regenDelay)
        {
            float newStamina = Mathf.Min(maxStamina, stamina + staminaRegenRate * Time.deltaTime);
            if (Mathf.Abs(newStamina - stamina) > 0.01f)
            {
                CmdRegenerateStamina(newStamina);
            }
        }
    }

    [Command]
    void CmdRegenerateStamina(float newStamina)
    {
        stamina = newStamina;
    }

    void UpdateStaminaUI()
    {
        if (staminaSlider != null && isLocalPlayer)
        {
            staminaSlider.maxValue = maxStamina;
            staminaSlider.value = stamina;
        }
    }

    // ====== Vaulting Network Commands ======
    bool TryVault(out Vector3 vaultTarget)
    {
        vaultTarget = Vector3.zero;
        if (!IsGrounded()) return false;

        Vector3 origin = transform.position + Vector3.up * (minVaultHeight + 0.2f);
        Vector3 dir = transform.forward;
        if (!Physics.Raycast(origin, dir, out RaycastHit hit, vaultForwardDistance + cc.radius, obstacleLayers))
            return false;

        Vector3 topProbeOrigin = hit.point + Vector3.up * (maxVaultHeight + 0.5f) + dir * 0.2f;
        if (!Physics.Raycast(topProbeOrigin, Vector3.down, out RaycastHit topHit, maxVaultHeight + 1f, obstacleLayers))
            return false;

        float heightFromFeet = topHit.point.y - transform.position.y;
        if (heightFromFeet < minVaultHeight || heightFromFeet > maxVaultHeight)
            return false;

        Vector3 landing = topHit.point + dir * 0.4f + Vector3.up * 0.1f;
        if (Physics.CheckSphere(landing + Vector3.up * 0.5f, cc.radius, obstacleLayers))
            return false;

        vaultTarget = landing;
        return true;
    }

    [Command]
    void CmdStartVault(Vector3 target)
    {
        RpcStartVault(target);
    }

    [ClientRpc]
    void RpcStartVault(Vector3 target)
    {
        StartCoroutine(DoVault(target));
    }

    IEnumerator DoVault(Vector3 target)
    {
        isVaulting = true;
        cc.enabled = false;
        Vector3 start = transform.position;
        float t = 0f;
        while (t < vaultDuration)
        {
            float alpha = t / vaultDuration;
            float ease = alpha * alpha * (3f - 2f * alpha);
            transform.position = Vector3.Lerp(start, target, ease) + Vector3.up * Mathf.Sin(ease * Mathf.PI) * 0.35f;
            t += Time.deltaTime;
            yield return null;
        }
        transform.position = target;
        cc.enabled = true;
        verticalVelocity = 0f;
        isVaulting = false;
    }

    void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }
    }
}