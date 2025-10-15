using Mirror;
using ProximityPrompts;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class PaintingTreasure : NetworkBehaviour
{
    [Header("Painting Settings")]
    public float baseValue = 1000f;
    [Range(0f, 1f)] public float ripPenalty = 0.25f;
    public float requiredScroll = 8f;
    public float scrollMultiplier = 1f;

    [Header("Input")]
    public InputActionReference rollAction;

    [Header("UI")]
    [Tooltip("Prefab with BackgroundPanel + ProgressSlider")]
    public GameObject progressUIPrefab;

    private ProximityPrompt prompt;
    private bool localIsRolling = false;
    private Slider progressSlider;
    private GameObject uiInstance;

    [SyncVar(hook = nameof(OnStolenChanged))] private bool isStolen;
    private readonly HashSet<uint> activePlayers = new();

    void Start()
    {
        prompt = GetComponent<ProximityPrompt>();
        if (prompt != null)
            prompt.Triggered += OnPromptTriggered;
    }

    void OnDestroy()
    {
        if (prompt != null)
            prompt.Triggered -= OnPromptTriggered;
    }

    // This now runs on ALL clients when the RPC fires
    void OnPromptTriggered(NetworkIdentity player)
    {
        // Only the server should handle the logic of tracking players
        if (isServer)
        {
            if (isStolen || player == null) return;

            uint id = player.netId;
            activePlayers.Add(id);

            // Send TargetRPC to the specific player who triggered it
            TargetStartScroll(player.connectionToClient);
        }

        // Local client behavior: if this is MY player who triggered it, start rolling
        // This handles the case where we're a pure client (not host)
        if (player != null && player.isLocalPlayer && !localIsRolling && !isStolen)
        {
            StartCoroutine(LocalRollRoutine());
        }
    }

    // CLIENT - This TargetRPC is now just a backup for host scenarios
    [TargetRpc]
    void TargetStartScroll(NetworkConnectionToClient conn)
    {
        // Only start if not already rolling (this prevents double-start on host)
        if (!localIsRolling && !isStolen)
        {
            StartCoroutine(LocalRollRoutine());
        }
    }

    IEnumerator LocalRollRoutine()
    {
        localIsRolling = true;
        if (prompt) prompt.enabled = false;

        GameObject uiInstance = null;
        RectTransform uiRect = null;

        if (progressUIPrefab != null && ProximityPromptService.Instance != null)
        {
            uiRect = ProximityPromptService.Instance.SpawnWorldLinkedUI(progressUIPrefab, transform, out uiInstance);
            if (uiInstance)
                progressSlider = uiInstance.GetComponentInChildren<Slider>();
        }

        var action = rollAction?.action;
        action?.Enable();

        float progress = 0f;

        while (localIsRolling && !isStolen)
        {
            float scrollDelta = action != null ? action.ReadValue<float>() : 0f;
            if (Mathf.Abs(scrollDelta) > 0.001f)
                progress += Mathf.Abs(scrollDelta) * scrollMultiplier * Time.deltaTime * 60f;

            if (progressSlider)
                progressSlider.value = Mathf.Clamp01(progress / requiredScroll);

            if (progress >= requiredScroll)
            {
                localIsRolling = false;
                CmdFinishRolling();
            }

            yield return null;
        }

        action?.Disable();

        if (uiInstance)
            Destroy(uiInstance);

        if (!isStolen && prompt)
            prompt.enabled = true;
    }

    [Command(requiresAuthority = false)]
    void CmdFinishRolling(NetworkConnectionToClient sender = null)
    {
        if (isStolen) return;

        uint winnerId = sender.identity.netId;
        bool contested = activePlayers.Count > 1;
        float awardedValue = contested ? baseValue * (1f - ripPenalty) : baseValue;

        isStolen = true;

        if (NetworkServer.spawned.TryGetValue(winnerId, out NetworkIdentity winnerNI))
        {
            var pr = winnerNI.GetComponentInChildren<PlayerRound>();
            if (pr) pr.AddValueServer(baseValue);
        }

        RpcOnPaintingStolen();
        activePlayers.Clear();
    }

    [ClientRpc]
    void RpcOnPaintingStolen()
    {
        var rend = GetComponentInChildren<MeshRenderer>();
        if (rend) rend.enabled = false;
        if (prompt) prompt.enabled = false;

        var coll = GetComponentInChildren<Collider>();
        if (coll) coll.enabled = false;
    }

    void OnStolenChanged(bool _, bool newVal)
    {
        if (newVal)
        {
            if (prompt) prompt.enabled = false;
            var rend = GetComponentInChildren<MeshRenderer>();
            if (rend) rend.enabled = false;
        }
    }
}