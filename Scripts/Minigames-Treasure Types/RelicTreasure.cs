using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using Mirror;
using ProximityPrompts;
using System.Collections;

public class RelicTreasure : NetworkBehaviour
{
    [Header("References")]
    public GameObject uiPrefab;
    public ProximityPrompt prompt;

    [Header("Value Settings")]
    [Tooltip("How much score this relic is worth on success")]
    public float baseValue = 2500f;

    [Header("Minigame Settings")]
    [Tooltip("How long the player has to complete extraction (seconds)")]
    public float maxExtractionTime = 10f;
    [Tooltip("Time it takes to fully extract the relic if stable")]
    public float extractionTime = 5f;
    [Tooltip("How quickly the relic line moves around (0 = static)")]
    public float relicMoveSpeed = 2f;
    [Tooltip("How quickly the player bar drifts left")]
    public float barDriftSpeed = 1f;
    [Tooltip("How much control pressing space/left click adds to the right")]
    public float inputPushPower = 2f;
    [Tooltip("Distance threshold between relic and bar for steady control")]
    public float successThreshold = 0.08f;
    public float offsetR = 1f;
    public float offsetL = 1f;

    [Header("Visuals")]
    public Renderer relicRenderer; // optional – for crumble fade

    // runtime
    private GameObject uiInstance;
    private RectTransform relicLine;
    private RectTransform holdZone;
    private Slider progressSlider;
    private bool isExtracting;
    private bool isCrumbled;
    private uint currentPlayerId;

    private void Start()
    {
        if (prompt == null)
            prompt = GetComponent<ProximityPrompt>();

        if (prompt != null)
            prompt.Triggered += OnPromptTriggered;
    }

    private void OnDestroy()
    {
        if (prompt != null)
            prompt.Triggered -= OnPromptTriggered;
    }

    // ------------------------------------------------------------
    // Entry point – works for host and client-only
    // ------------------------------------------------------------
    private void OnPromptTriggered(NetworkIdentity player)
    {
        if (isCrumbled || isExtracting) return;

        if (isServer)
        {
            if (player == null) return;
            StartExtraction(player);
        }
        else if (player != null && player.isLocalPlayer)
        {
            CmdRequestExtraction();
        }
    }

    [Command(requiresAuthority = false)]
    private void CmdRequestExtraction(NetworkConnectionToClient sender = null)
    {
        if (isCrumbled || isExtracting) return;
        if (sender?.identity == null) return;

        StartExtraction(sender.identity);
    }

    // ------------------------------------------------------------
    // SERVER: start extraction and notify client
    // ------------------------------------------------------------
    private void StartExtraction(NetworkIdentity player)
    {
        isExtracting = true;
        if (prompt != null) prompt.enabled = false;

        currentPlayerId = player.netId;

        TargetStartExtraction(player.connectionToClient);
        RpcSetPromptActive(false);
    }

    [ClientRpc]
    private void RpcSetPromptActive(bool active)
    {
        if (prompt != null)
            prompt.enabled = active;
    }

    // ------------------------------------------------------------
    // CLIENT: UI setup and gameplay
    // ------------------------------------------------------------
    [TargetRpc]
    private void TargetStartExtraction(NetworkConnectionToClient conn)
    {
        var localPlayer = NetworkClient.localPlayer;
        if (localPlayer == null)
        {
            Debug.LogError("[RelicTreasure] No local player found.");
            return;
        }

        var playerUI = localPlayer.transform.Find("PlayerUI");
        if (playerUI == null)
        {
            Debug.LogError("[RelicTreasure] PlayerUI not found under player prefab.");
            return;
        }

        if (uiPrefab == null)
        {
            Debug.LogError("[RelicTreasure] No UI prefab assigned.");
            return;
        }

        uiInstance = Instantiate(uiPrefab, playerUI);
        relicLine = uiInstance.transform.Find("BackgroundPanel/RelicLine")?.GetComponent<RectTransform>();
        holdZone = uiInstance.transform.Find("BackgroundPanel/HoldZone")?.GetComponent<RectTransform>();
        progressSlider = uiInstance.transform.Find("ProgressSlider")?.GetComponent<Slider>();

        if (relicLine == null || holdZone == null || progressSlider == null)
        {
            Debug.LogError("[RelicTreasure] Missing UI components!");
            return;
        }

        StartCoroutine(ExtractionRoutine());
    }

    // ------------------------------------------------------------
    // CLIENT: minigame loop
    // ------------------------------------------------------------
    private IEnumerator ExtractionRoutine()
    {
        float progress = 0f;
        float relicPos = 0.5f;
        float barPos = 0.5f;
        float elapsed = 0f;

        while (elapsed < maxExtractionTime)
        {
            elapsed += Time.deltaTime;

            // relic movement
            relicPos = Mathf.PingPong(Time.time * relicMoveSpeed, 1f);

            // player control
            bool pressing = Mouse.current.leftButton.isPressed;
            barPos -= Time.deltaTime * barDriftSpeed;
            if (pressing)
                barPos += Time.deltaTime * inputPushPower;

            float halfWidth = 0.05f;
            barPos = Mathf.Clamp(barPos, offsetL - halfWidth, offsetR - halfWidth);

            // UI update
            if (relicLine && holdZone)
            {
                relicLine.anchorMin = new Vector2(relicPos, relicLine.anchorMin.y);
                relicLine.anchorMax = new Vector2(relicPos, relicLine.anchorMax.y);
                holdZone.anchorMin = new Vector2(barPos - halfWidth, holdZone.anchorMin.y);
                holdZone.anchorMax = new Vector2(barPos + halfWidth, holdZone.anchorMax.y);
            }

            float dist = Mathf.Abs(relicPos - barPos);
            if (dist < successThreshold)
                progress += Time.deltaTime / extractionTime;
            else
                progress -= Time.deltaTime / (extractionTime * 2f);

            progress = Mathf.Clamp01(progress);
            if (progressSlider) progressSlider.value = progress;

            if (progress >= 1f) break;

            yield return null;
        }

        Destroy(uiInstance);

        if (progress >= 1f)
            CmdExtractionComplete();
        else
            CmdExtractionFailed();
    }

    // ------------------------------------------------------------
    // SERVER: results
    // ------------------------------------------------------------
    [Command(requiresAuthority = false)]
    private void CmdExtractionComplete()
    {
        if (isCrumbled) return;

        Debug.Log("[RelicTreasure] Extraction successful.");

        // look up the player’s PlayerRound on their PlayerModel child
        if (NetworkServer.spawned.TryGetValue(currentPlayerId, out NetworkIdentity ni))
        {
            var pr = ni.GetComponentInChildren<PlayerRound>();
            if (pr != null)
            {
                pr.AddValueServer(baseValue);
            }
            else
            {
                Debug.LogWarning("[RelicTreasure] Could not find PlayerRound on player!");
            }
        }

        NetworkServer.Destroy(gameObject);
    }

    [Command(requiresAuthority = false)]
    private void CmdExtractionFailed()
    {
        if (isCrumbled) return;
        isCrumbled = true;

        Debug.Log("[RelicTreasure] Relic crumbled due to failure.");
        RpcRelicCrumble();
    }

    [ClientRpc]
    private void RpcRelicCrumble()
    {
        if (prompt != null)
            prompt.enabled = false;

        if (relicRenderer != null)
            StartCoroutine(CrumbleFadeRoutine());
        else
            gameObject.SetActive(false);
    }

    private IEnumerator CrumbleFadeRoutine()
    {
        float t = 0f;
        Material mat = relicRenderer.material;

        while (t < 1f)
        {
            t += Time.deltaTime / 2f;
            Color c = mat.color;
            c.a = Mathf.Lerp(1f, 0f, t);
            mat.color = c;
            yield return null;
        }

        gameObject.SetActive(false);
    }
}
