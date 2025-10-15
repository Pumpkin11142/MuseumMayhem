using Mirror;
using ProximityPrompts;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum RoundState { WaitingForPlayers, CountdownToStart, InProgress, Ended }

public class RoundManager : NetworkBehaviour
{
    public static RoundManager Instance;
    public static RoundUI RoundUI;

    [Header("Round timing (seconds)")]
    public int baseSeconds = 120;
    public int addSecondsPerPlayer = 60;
    public int maxSeconds = 300;
    public int countdownSeconds = 5; // short pre-round countdown

    [Header("Player Requirements")]
    public int minPlayersToStart = 2;

    [Header("Podium Presentation")]
    public GameObject podiumPrefab;          // prefab with Spot1/2/3 + CameraPivot
    public Transform podiumSpawnPoint;       // assign in scene
    public float podiumDisplayDelay = 1.5f;  // short delay before showing podium

    [SyncVar(hook = nameof(OnRoundStateChanged))] public RoundState roundState = RoundState.WaitingForPlayers;
    [SyncVar(hook = nameof(OnRoundTimeChanged))] public float roundTimeRemaining = 0f;

    [SyncVar(hook = nameof(OnPromptsActiveChanged))]
    private bool promptsActive = true;

    void OnPromptsActiveChanged(bool oldVal, bool newVal)
    {
        ApplyPromptState(newVal);
    }

    [SyncVar] public float roundStartTime = 0f; // server time when round started

    // server-only collections
    private readonly List<PlayerRound> players = new List<PlayerRound>();
    private bool roundStarted = false;

    void Awake() { if (Instance == null) Instance = this; }

    #region Server API (called from PlayerRound)
    [Server]
    public void RegisterPlayer(PlayerRound player)
    {
        if (!players.Contains(player))
        {
            players.Add(player);

            // Check if we should start the round
            if (!roundStarted && players.Count >= minPlayersToStart && roundState == RoundState.WaitingForPlayers)
            {
                roundStarted = true;
                StartCoroutine(StartCountdownRoutine());
            }
        }
    }

    [Server]
    public void UnregisterPlayer(PlayerRound player)
    {
        players.Remove(player);
    }

    [Server]
    void SetPromptsActive(bool active)
    {
        promptsActive = active;       // syncs to all current + future clients
        ApplyPromptState(active);     // apply on the server too (for host testing)
    }

    [Server]
    public void NotifyPlayerReady(PlayerRound player)
    {
        // No longer needed for starting rounds, but keep for future use
    }
    #endregion

    #region Round Lifecycle (server)
    [Server]
    IEnumerator StartCountdownRoutine()
    {
        SetPromptsActive(false);   // lock interactions during countdown

        roundState = RoundState.CountdownToStart;
        int c = countdownSeconds;
        while (c > 0)
        {
            RpcUpdateCountdown(c);
            yield return new WaitForSeconds(1f);
            c--;
        }

        SetPromptsActive(true);    // unlock when the round begins
        StartRound();
    }

    [Server]
    void StartRound()
    {
        roundState = RoundState.InProgress;
        roundStartTime = (float)NetworkTime.time;
        int playerCount = Mathf.Max(1, players.Count);
        int duration = Mathf.Clamp(baseSeconds + (playerCount - 1) * addSecondsPerPlayer, baseSeconds, maxSeconds);
        roundTimeRemaining = duration;
        StartCoroutine(RoundTimer());
    }

    [Server]
    IEnumerator RoundTimer()
    {
        while (roundTimeRemaining > 0f)
        {
            yield return new WaitForSeconds(1f);
            roundTimeRemaining -= 1f;
        }
        EndRound();
    }

    [Server]
    void EndRound()
    {
        roundState = RoundState.Ended;
        SetPromptsActive(false);   // lock again

        var ranked = players.OrderByDescending(p => p.totalValue).ToList();
        List<uint> top3 = ranked.Take(3).Select(p => p.netIdentity.netId).ToList();
        List<string> names = ranked.Take(3).Select(p => p.playerName).ToList();
        List<float> values = ranked.Take(3).Select(p => p.totalValue).ToList();

        RpcShowWinners(top3.ToArray(), names.ToArray(), values.ToArray());
    }
    #endregion

    #region Client RPCs (UI hooks)
    [ClientRpc]
    void RpcUpdateCountdown(int secondsLeft)
    {
        // broadcast each tick to local UI
        if (RoundUI != null)
            RoundUI.PlayCountdown(secondsLeft);
    }

    [ClientRpc]
    void RpcShowWinners(uint[] winnerIds, string[] names, float[] values)
    {
        StartCoroutine(ClientPodiumSequence(winnerIds, names, values));
    }

    IEnumerator ClientPodiumSequence(uint[] winnerIds, string[] names, float[] values)
    {
        yield return new WaitForSeconds(1.5f); // small delay before showing

        foreach (var cam in FindObjectsOfType<ThirdPersonSmoothCamera>())
        {
            cam.enabled = false;
        }

        if (podiumPrefab == null)
        {
            Debug.LogWarning("No podiumPrefab assigned to RoundManager.");
            yield break;
        }

        // Spawn local podium scene
        GameObject podium = Instantiate(podiumPrefab, podiumSpawnPoint.position, podiumSpawnPoint.rotation);
        Transform[] spots =
        {
        podium.transform.Find("Spot1Placement"),
        podium.transform.Find("Spot2Placement"),
        podium.transform.Find("Spot3Placement")
    };

        // Clone and pose the top 3 players
        for (int i = 0; i < winnerIds.Length && i < spots.Length; i++)
        {
            if (NetworkClient.spawned.TryGetValue(winnerIds[i], out NetworkIdentity ni))
            {
                Transform modelTransform = ni.transform.Find("PlayerModel"); // direct child name
                var controller = ni.GetComponentInChildren<PlayerParkourController>(true);

                if (controller) controller.enabled = false; // stop movement input

                if (modelTransform)
                {
                    GameObject clone = Instantiate(modelTransform.gameObject, spots[i].position, spots[i].rotation);
                    var anim = clone.GetComponentInChildren<Animator>();
                    if (anim) anim.Play("VictoryPose"); // optional animation
                }

                // hide the original local model
                if (ni.isLocalPlayer && modelTransform)
                    modelTransform.gameObject.SetActive(false);
            }
        }

        // Move cameras for all players to the ceremony pivot
        Transform camPivot = podium.transform.Find("CameraPivot");
        if (camPivot)
        {
            foreach (var cam in FindObjectsOfType<ThirdPersonSmoothCamera>())
                cam.SetTarget(camPivot);
        }

        // Announce winners in log or console
        for (int i = 0; i < names.Length && i < spots.Length; i++)
        {
            Debug.Log($"🏆 {i + 1}. {names[i]} — ${values[i]:F0}");
        }

        // TODO (when lobby exists):
        //   Instantiate a UI panel with a "Go Back to Lobby" button here.
        //   It would call CmdReturnToLobby() or trigger a scene change when pressed.
    }

    // Disables all proximity prompts in the scene (client-side)
    [ClientRpc]
    void RpcSetAllPromptsActive(bool active)
    {
        foreach (var prompt in FindObjectsOfType<ProximityPrompt>())
        {
            prompt.enabled = active;
        }
    }
    #endregion

    [Client]
    void ApplyPromptState(bool active)
    {
        foreach (var prompt in FindObjectsOfType<ProximityPrompt>())
            prompt.enabled = active;
    }

    #region SyncVar hooks (client-side)
    void OnRoundStateChanged(RoundState oldState, RoundState newState)
    {
        // Handle late joiners syncing to countdown
        if (newState == RoundState.CountdownToStart && oldState == RoundState.WaitingForPlayers)
        {
            // Calculate how much countdown time has elapsed
            // Note: This is approximate since we don't sync exact countdown progress
            if (RoundUI != null)
            {
                // Just show a quick sync message or jump into the round
                Debug.Log("Joining countdown in progress...");
            }
        }
    }

    void OnRoundTimeChanged(float oldVal, float newVal) { /* UI can react */ }
    #endregion
}