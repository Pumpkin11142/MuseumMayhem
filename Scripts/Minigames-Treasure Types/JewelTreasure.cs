using System.Collections;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using ProximityPrompts;

public class JewelTreasure : NetworkBehaviour
{
    [Header("Values")]
    public float baseValue = 1500f;
    [Range(0f, 1f)] public float shardValueFraction = 0.3f;
    [Range(0f, 1f)] public float prickChance = 0.2f;

    [Header("Physics")]
    public float popForce = 5f;
    public float popTorque = 2f;
    public float catchWindow = 2.0f;
    public float groundY = 0.1f;
    public float shardExplosionForce = 3f;
    public float shardUpwardForce = 1f;

    [Header("Prefabs")]
    public GameObject shardPrefab;
    public GameObject particlePrefab;
    public GameObject catchUIPrefab;

    private ProximityPrompt prompt;
    private Rigidbody rb;
    private bool airborne = false;
    private bool caught = false;
    private bool resolving = false;

    private GameObject uiInstance;
    private Coroutine serverPopCoroutine;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (!rb) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;

        prompt = GetComponent<ProximityPrompt>();
        if (prompt)
            prompt.Triggered += OnPromptTriggered;
    }

    void OnDestroy()
    {
        if (prompt)
            prompt.Triggered -= OnPromptTriggered;
    }

    // This now runs on ALL clients when the RPC fires
    void OnPromptTriggered(NetworkIdentity player)
    {
        Debug.Log($"[JewelTreasure] OnPromptTriggered called. isServer={isServer}, player={player?.name}, resolving={resolving}");

        // Server: handle the logic of determining prick and broadcasting
        if (isServer && !resolving)
        {
            if (player == null) return;
            resolving = true;

            // random prick
            bool pricked = Random.value < prickChance;
            Debug.Log($"[JewelTreasure] Server calling RPC. pricked={pricked}, playerId={player.netId}");
            StartServerPop(pricked, player.netId);
            RpcStartJewelPop(pricked, player.netId);
        }

        // Client: if this is MY player who triggered it, start locally (for pure clients)
        if (!isServer && !resolving)
        {
            bool isLocalTrigger = false;

            if (player != null)
            {
                isLocalTrigger = player.isLocalPlayer;
            }
            else if (prompt && prompt.CurrentPlayer != null)
            {
                isLocalTrigger = prompt.CurrentPlayer.isLocalPlayer;
            }

            if (isLocalTrigger)
            {
                Debug.Log($"[JewelTreasure] Local client starting - requesting server pop");
                CmdRequestPop();
            }
        }
    }

    [Command(requiresAuthority = false)]
    void CmdRequestPop(NetworkConnectionToClient sender = null)
    {
        if (resolving || sender?.identity == null) return;
        resolving = true;

        bool pricked = Random.value < prickChance;
        uint triggeringPlayer = sender.identity.netId;
        Debug.Log($"[JewelTreasure] Server received pop request. pricked={pricked}, playerId={triggeringPlayer}");
        StartServerPop(pricked, triggeringPlayer);
        RpcStartJewelPop(pricked, triggeringPlayer);
    }

    [ClientRpc]
    void RpcStartJewelPop(bool pricked, uint playerId)
    {
        Debug.Log($"[JewelTreasure] RpcStartJewelPop received. pricked={pricked}, playerId={playerId}");

        caught = false;
        airborne = true;
        if (rb)
        {
            rb.isKinematic = false;
        }

        if (pricked)
        {
            // later: play prick VFX or small flash
        }
        StartCoroutine(LocalPopRoutine(pricked, playerId));
    }

    // ---------- CLIENT ----------
    IEnumerator LocalPopRoutine(bool pricked, uint playerId)
    {
        _ = pricked; // currently unused but kept for future VFX logic

        bool isLocalTrigger = NetworkClient.localPlayer != null && NetworkClient.localPlayer.netId == playerId;
        InputAction click = null;

        // Spawn UI tracker for the triggering player only
        if (isLocalTrigger && catchUIPrefab && ProximityPromptService.Instance)
        {
            ProximityPromptService.Instance.SpawnWorldLinkedUI(catchUIPrefab, transform, out uiInstance);
        }

        if (isLocalTrigger)
        {
            click = new InputAction(type: InputActionType.Button, binding: "<Mouse>/leftButton");
            click.Enable();
        }

        while (airborne)
        {
            if (isLocalTrigger && !caught && click != null && click.triggered)
            {
                CmdAttemptCatch(playerId);
            }

            yield return null;
        }

        if (isLocalTrigger && click != null)
        {
            click.Disable();
            click.Dispose();
        }

        if (uiInstance)
        {
            Destroy(uiInstance);
            uiInstance = null;
        }
    }

    // ---------- SERVER RESOLUTION ----------
    void StartServerPop(bool pricked, uint playerId)
    {
        if (!isServer) return;

        caught = false;
        airborne = true;

        if (serverPopCoroutine != null)
        {
            StopCoroutine(serverPopCoroutine);
        }

        if (rb)
        {
            rb.isKinematic = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.AddForce(Vector3.up * popForce, ForceMode.VelocityChange);
            rb.AddTorque(Random.insideUnitSphere * popTorque, ForceMode.VelocityChange);
        }

        serverPopCoroutine = StartCoroutine(ServerPopRoutine(playerId));
    }

    IEnumerator ServerPopRoutine(uint playerId)
    {
        float elapsed = 0f;

        while (airborne)
        {
            elapsed += Time.deltaTime;

            if (!caught && (transform.position.y <= groundY || elapsed >= catchWindow))
            {
                ServerResolveShatter(playerId);
                yield break;
            }

            yield return null;
        }

        serverPopCoroutine = null;
    }

    [Command(requiresAuthority = false)]
    void CmdAttemptCatch(uint playerId, NetworkConnectionToClient sender = null)
    {
        if (sender?.identity == null || sender.identity.netId != playerId) return;
        ServerResolveCatch(playerId);
    }

    [Command(requiresAuthority = false)]
    void CmdShatter(uint playerId, NetworkConnectionToClient sender = null)
    {
        if (sender?.identity == null || sender.identity.netId != playerId) return;
        ServerResolveShatter(playerId);
    }

    void ServerResolveCatch(uint playerId)
    {
        if (!isServer || caught) return;

        caught = true;
        airborne = false;

        if (serverPopCoroutine != null)
        {
            StopCoroutine(serverPopCoroutine);
            serverPopCoroutine = null;
        }

        if (rb)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (NetworkServer.spawned.TryGetValue(playerId, out NetworkIdentity ni))
        {
            var pr = ni.GetComponentInChildren<PlayerRound>();
            if (pr) pr.AddValueServer(baseValue);
        }

        RpcOnCaught();
        resolving = false;
    }

    void ServerResolveShatter(uint playerId)
    {
        if (!isServer || caught) return;

        caught = false;
        airborne = false;

        if (serverPopCoroutine != null)
        {
            StopCoroutine(serverPopCoroutine);
            serverPopCoroutine = null;
        }

        if (rb)
        {
            rb.isKinematic = true;
        }

        Vector3 shatterPos = transform.position;
        int shardCount = Random.Range(4, 6);

        for (int i = 0; i < shardCount; i++)
        {
            Vector3 randomOffset = Random.insideUnitSphere * 0.2f;
            var shard = Instantiate(shardPrefab, shatterPos + randomOffset, Random.rotation);

            // Add explosion force to shard
            Rigidbody shardRb = shard.GetComponent<Rigidbody>();
            if (shardRb)
            {
                Vector3 explosionDir = (shard.transform.position - shatterPos).normalized;
                explosionDir.y = 0; // flatten to horizontal plane
                if (explosionDir.magnitude < 0.1f)
                    explosionDir = Random.insideUnitSphere;

                Vector3 force = explosionDir.normalized * shardExplosionForce + Vector3.up * shardUpwardForce;
                shardRb.AddForce(force, ForceMode.VelocityChange);
                shardRb.AddTorque(Random.insideUnitSphere * popTorque * 0.5f, ForceMode.VelocityChange);
            }

            NetworkServer.Spawn(shard);
        }

        // Spawn particle effect
        if (particlePrefab)
        {
            var particles = Instantiate(particlePrefab, shatterPos, Quaternion.identity);
            NetworkServer.Spawn(particles);
            Destroy(particles, 3f); // Clean up after 3 seconds
        }

        if (NetworkServer.spawned.TryGetValue(playerId, out NetworkIdentity ni))
        {
            var pr = ni.GetComponentInChildren<PlayerRound>();
            if (pr) pr.AddValueServer(baseValue * shardValueFraction);
        }

        RpcOnShattered();
        resolving = false;
    }

    // ---------- CLIENT FEEDBACK ----------
    [ClientRpc]
    void RpcOnCaught()
    {
        airborne = false;
        caught = true;
        if (rb) rb.isKinematic = true;
        if (uiInstance)
        {
            Destroy(uiInstance);
            uiInstance = null;
        }
        // disable mesh or play sparkle animation
        var rend = GetComponentInChildren<MeshRenderer>();
        if (rend) rend.enabled = false;
        if (prompt) prompt.enabled = false;
    }

    [ClientRpc]
    void RpcOnShattered()
    {
        airborne = false;
        caught = false;
        if (rb) rb.isKinematic = true;
        if (uiInstance)
        {
            Destroy(uiInstance);
            uiInstance = null;
        }
        var rend = GetComponentInChildren<MeshRenderer>();
        if (rend) rend.enabled = false;
        if (prompt) prompt.enabled = false;
        var coll = GetComponentInChildren<Collider>();
        if (coll) coll.enabled = false;
    }
}