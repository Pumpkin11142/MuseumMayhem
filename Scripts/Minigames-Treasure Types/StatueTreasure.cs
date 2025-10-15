using Mirror;
using ProximityPrompts;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(ProximityPrompt))]
[RequireComponent(typeof(Rigidbody))]
public class StatueTreasure : NetworkBehaviour
{
    [Header("Statue Settings")]
    [Tooltip("Base value of the statue if delivered intact")]
    public float baseValue = 5000f;

    [Tooltip("Fraction of value kept if dropped/cracked")]
    [Range(0f, 1f)] public float crackedValueFraction = 0.6f;

    [Tooltip("Fraction of value kept if shattered")]
    [Range(0f, 1f)] public float shatteredValueFraction = 0.3f;

    [Tooltip("Force needed to shatter statue on impact")]
    public float shatterForce = 10f;

    [Tooltip("Time after drop before statue can be picked up again")]
    public float pickupCooldown = 2f;

    [Header("Carry Settings")]
    public Vector3 carryOffset = new Vector3(0, 0.5f, 0.95f);
    public float followSpeed = 10f;           // position lerp speed (server-side)
    public float rotateFollowSpeed = 12f;     // rotation lerp speed (server-side)

    private ProximityPrompt prompt;
    private Rigidbody rb;
    private Collider[] colliders;

    [SyncVar] private bool isCarried;
    [SyncVar] private uint carrierNetId;
    [SyncVar] private bool isCracked;

    private float lastDropTime;

    // server-side follow target
    private Transform serverFollowTarget;
    private Coroutine serverFollowRoutine;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        colliders = GetComponentsInChildren<Collider>();
        prompt = GetComponent<ProximityPrompt>();
    }

    void Start()
    {
        if (prompt != null)
            prompt.Triggered += OnPromptTriggered;
    }

    void OnDestroy()
    {
        if (prompt != null)
            prompt.Triggered -= OnPromptTriggered;
    }

    // ------------------------------------------------------------
    // Interaction entry point – client sends request, server acts
    // ------------------------------------------------------------
    void OnPromptTriggered(NetworkIdentity player)
    {
        if (player == null) return;

        // Only the local player should ask; server will validate/execute.
        if (player.isLocalPlayer)
            CmdRequestCarry(player.netId);
    }

    [Command(requiresAuthority = false)]
    void CmdRequestCarry(uint requesterId)
    {
        if (isCarried) return;
        if (Time.time - lastDropTime < pickupCooldown) return;

        if (!NetworkServer.spawned.TryGetValue(requesterId, out NetworkIdentity carrier))
            return;

        StartCarry(carrier);
    }

    // ------------------------------------------------------------
    // SERVER: begin carrying (authoritative physics + follow loop)
    // ------------------------------------------------------------
    [Server]
    void StartCarry(NetworkIdentity player)
    {
        isCarried = true;
        carrierNetId = player.netId;

        // Find a good follow anchor on the carrier (their controller/model)
        var controller = player.GetComponentInChildren<PlayerParkourController>();
        serverFollowTarget = controller != null ? controller.transform : player.transform;

        // Freeze physics server-side and make non-blocking
        rb.isKinematic = true;
        foreach (var col in colliders)
            col.isTrigger = true;

        // disable prompts everywhere
        RpcSetPromptActive(false);

        // tell that specific client to apply UI + movement modifiers
        TargetBeginCarry(player.connectionToClient);

        // start server follow loop so server drives position & triggers
        if (serverFollowRoutine != null) StopCoroutine(serverFollowRoutine);
        serverFollowRoutine = StartCoroutine(ServerFollowRoutine());
    }

    // server moves statue each frame to carrier (replicated by NetworkTransform)
    [Server]
    IEnumerator ServerFollowRoutine()
    {
        while (isCarried && serverFollowTarget != null)
        {
            // target pose
            Vector3 targetPos = serverFollowTarget.position + serverFollowTarget.TransformDirection(carryOffset);
            Quaternion targetRot = serverFollowTarget.rotation;

            // move & rotate smoothly
            transform.position = Vector3.Lerp(transform.position, targetPos, Mathf.Clamp01(followSpeed * Time.deltaTime));
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Mathf.Clamp01(rotateFollowSpeed * Time.deltaTime));

            yield return null;
        }
        serverFollowRoutine = null;
    }

    // ------------------------------------------------------------
    // CLIENT: local player carrying (UI + slowdown only)
    // ------------------------------------------------------------
    [TargetRpc]
    void TargetBeginCarry(NetworkConnectionToClient conn)
    {
        var localPlayer = NetworkClient.localPlayer;
        if (localPlayer == null) return;

        var controller = localPlayer.GetComponentInChildren<PlayerParkourController>();
        controller.cc.excludeLayers = controller.carryExcludeLayers;
        if (controller != null)
        {
            controller.speedMultiplier = 0.4f;
            controller.carryStaminaMultiplier = 2f;
            controller.carryJumpMultiplier = 0.6f;

            // client handles drop input; the server will do the actual drop
            StartCoroutine(ClientDropWatcher(controller));
        }
    }

    // locally watch for drop input and ask server to drop
    IEnumerator ClientDropWatcher(PlayerParkourController controller)
    {
        while (isCarried)
        {
            bool dropPressed = Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
            bool staminaEmpty = (controller.staminaSlider != null && controller.staminaSlider.value <= 1f);

            if (dropPressed || staminaEmpty)
            {
                CmdDropStatue(NetworkClient.localPlayer.netId);
                break;
            }
            controller.cc.excludeLayers = controller.defaultExcludeLayers;
            yield return null;
        }

        // restore modifiers locally
        controller.speedMultiplier = 1f;
        controller.carryStaminaMultiplier = 1f;
        controller.carryJumpMultiplier = 1f;
    }

    [ClientRpc]
    void RpcSetPromptActive(bool active)
    {
        if (prompt != null)
            prompt.enabled = active;
    }

    // ------------------------------------------------------------
    // SERVER: dropping (unfreeze & solidify server-side)
    // ------------------------------------------------------------
    [Command(requiresAuthority = false)]
    void CmdDropStatue(uint playerId)
    {
        if (!isCarried) return;
        if (carrierNetId != playerId) return; // only carrier can drop

        isCarried = false;
        lastDropTime = Time.time;

        // stop follow loop
        if (serverFollowRoutine != null) { StopCoroutine(serverFollowRoutine); serverFollowRoutine = null; }
        serverFollowTarget = null;

        // restore physics on server
        rb.isKinematic = false;
        foreach (var col in colliders)
            col.isTrigger = false;

        // notify all clients to restore prompt after cooldown
        RpcDropStatue(transform.position, transform.rotation);
    }

    [ClientRpc]
    void RpcDropStatue(Vector3 pos, Quaternion rot)
    {
        // keep server authoritative; this is just to re-enable prompt later and fix local modifiers if needed
        StartCoroutine(EnablePromptAfterDelay());
    }

    IEnumerator EnablePromptAfterDelay()
    {
        yield return new WaitForSeconds(pickupCooldown);
        if (prompt != null)
            prompt.enabled = true;
    }

    // ------------------------------------------------------------
    // SERVER: impacts (only meaningful if someone somehow collides while carried)
    // ------------------------------------------------------------
    void OnCollisionEnter(Collision collision)
    {
        if (!isServer) return;

        // While carried we set colliders to triggers; collisions shouldn’t happen.
        // But if they do (e.g., misconfigured collider), handle cracks/shatter.
        if (isCarried) return;

        float impact = collision.relativeVelocity.magnitude;

        if (impact > shatterForce * 1.5f)
        {
            RpcStatueShatter();
        }
        else if (impact > shatterForce * 0.75f && !isCracked)
        {
            isCracked = true;
            RpcStatueCrack();
        }
    }

    [ClientRpc]
    void RpcStatueCrack()
    {
        Debug.Log($"{name} cracked! Value reduced.");
        // TODO: crack VFX
    }

    [ClientRpc]
    void RpcStatueShatter()
    {
        Debug.Log($"{name} shattered! Partial shards remain.");
        Destroy(gameObject, 0.5f);
    }

    // ------------------------------------------------------------
    // DELIVERY: detect exit zone, reward, destroy (server-only)
    // ------------------------------------------------------------
    void OnTriggerEnter(Collider other)
    {
        if (!isServer) return;
        if (!isCarried) return;
        if (!other.CompareTag("DeliveryZone")) return;

        DeliverStatue();
    }

    [Server]
    void DeliverStatue()
    {
        isCarried = false;

        // stop server follow
        if (serverFollowRoutine != null) { StopCoroutine(serverFollowRoutine); serverFollowRoutine = null; }
        serverFollowTarget = null;

        // reward carrier
        if (NetworkServer.spawned.TryGetValue(carrierNetId, out NetworkIdentity carrier))
        {
            var pr = carrier.GetComponentInChildren<PlayerRound>();
            if (pr != null)
            {
                float reward = isCracked ? baseValue * crackedValueFraction : baseValue;
                pr.AddValueServer(reward);
            }

            var controller = carrier.GetComponentInChildren<PlayerParkourController>();
            if (controller != null)
            {
                // make sure client modifiers are sane on the carrier
                TargetRestoreMovement(carrier.connectionToClient);
            }
        }

        NetworkServer.Destroy(gameObject);
        Debug.Log("[StatueTreasure] Statue delivered!");
    }

    [TargetRpc]
    void TargetRestoreMovement(NetworkConnectionToClient conn)
    {
        var localPlayer = NetworkClient.localPlayer;
        var controller = localPlayer != null ? localPlayer.GetComponentInChildren<PlayerParkourController>() : null;
        controller.cc.excludeLayers = controller.defaultExcludeLayers;
        if (controller != null)
        {
            controller.speedMultiplier = 1f;
            controller.carryStaminaMultiplier = 1f;
            controller.carryJumpMultiplier = 1f;
        }
    }
}
