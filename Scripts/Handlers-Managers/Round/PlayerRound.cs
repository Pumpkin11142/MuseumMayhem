using UnityEngine;
using Mirror;

public class PlayerRound : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnNameChanged))] public string playerName = "Player";
    [SyncVar(hook = nameof(OnValueChanged))] public float totalValue = 0f; // total stolen value
    [SyncVar(hook = nameof(OnScoreChanged))] public int score = 0; // optional integer score
    [SyncVar(hook = nameof(OnReadyChanged))] public bool isReady = false;

    public override void OnStartServer()
    {
        // register server-side so RoundManager knows about us
        RoundManager.Instance?.RegisterPlayer(this);
    }

    public override void OnStopServer()
    {
        RoundManager.Instance?.UnregisterPlayer(this);
    }

    public override void OnStartClient()
    {
        // for testing: auto-set name and auto-ready when local player appears
        if (isLocalPlayer)
        {
            CmdSetPlayerName($"Player_{netId}");
            // for quick tests with 1 player, auto-ready:
            CmdSetReady();
        }
    }

    [Server]
    public void AddValueServer(float value)
    {
        totalValue += value;
        Debug.Log($"[PlayerRound] Server added {value}. Total now: {totalValue}");
    }

    // Client -> Server commands
    [Command]
    public void CmdSetPlayerName(string newName)
    {
        playerName = newName;
    }

    [Command]
    public void CmdSetReady()
    {
        if (!isReady)
        {
            isReady = true;
            RoundManager.Instance?.NotifyPlayerReady(this);
        }
    }

    // Called by client minigame when a theft succeeds (client reports result to server)
    // You can add validation server-side later if needed.
    [Command]
    public void CmdReportSteal(float value)
    {
        totalValue += value;
        score += Mathf.RoundToInt(value); // optional scoring rule
    }

    #region SyncVar hooks (client)
    void OnNameChanged(string oldName, string newName) { /* update UI */ }
    void OnValueChanged(float oldVal, float newVal) { /* update UI */ }
    void OnScoreChanged(int oldScore, int newScore) { /* update UI */ }
    void OnReadyChanged(bool oldReady, bool newReady) { /* update UI */ }
    #endregion
}
