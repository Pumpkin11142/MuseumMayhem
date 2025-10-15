using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;

public class CustomNetworkManager : NetworkRoomManager
{
    [Header("Dynamic Spawn System")]
    public DynamicSpawnSystem spawnSystem;

    [Header("Matchmaking Settings")]
    [Tooltip("How many players need to be ready before a match starts.")]
    public int playersPerMatch = 2;
    [Tooltip("Delay (in seconds) before loading the gameplay scene once everyone is ready.")]
    public float matchStartDelay = 2f;

    Coroutine matchStartRoutine;

    public override void OnStartServer()
    {
        base.OnStartServer();
        EnsureSpawnSystem();
    }

    public override void OnRoomServerSceneChanged(string sceneName)
    {
        base.OnRoomServerSceneChanged(sceneName);

        if (sceneName == GameplayScene)
        {
            EnsureSpawnSystem();
            ClearTrackedSpawnPositions();
        }
        else if (sceneName == RoomScene || sceneName == OfflineScene)
        {
            spawnSystem = null;
            CancelMatchStartIfNeeded();
        }
    }

    public override void OnRoomServerConnect(NetworkConnectionToClient conn)
    {
        base.OnRoomServerConnect(conn);
        NotifyRoomPlayerStateChanged();
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        if (conn != null && conn.identity != null && spawnSystem != null)
        {
            spawnSystem.RemovePlayerPosition(conn.identity.transform.position);
        }

        base.OnServerDisconnect(conn);

        if (IsInRoomScene())
        {
            NotifyRoomPlayerStateChanged();
        }
    }

    public override GameObject OnRoomServerCreateGamePlayer(NetworkConnectionToClient conn, GameObject roomPlayer)
    {
        return CreateGamePlayer();
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        ClearTrackedSpawnPositions();
        CancelMatchStartIfNeeded();
    }

    void EnsureSpawnSystem()
    {
        if (spawnSystem == null)
        {
            spawnSystem = FindObjectOfType<DynamicSpawnSystem>();
            if (spawnSystem == null)
            {
                Debug.LogWarning("No DynamicSpawnSystem found. Players will fall back to the default start positions.");
            }
        }
    }

    GameObject CreateGamePlayer()
    {
        EnsureSpawnSystem();

        Vector3 spawnPos = Vector3.zero;
        Quaternion spawnRot = Quaternion.identity;

        if (spawnSystem != null)
        {
            spawnPos = spawnSystem.GetSpawnPosition();
            spawnRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        }
        else
        {
            Transform startPosition = GetStartPosition();
            if (startPosition != null)
            {
                spawnPos = startPosition.position;
                spawnRot = startPosition.rotation;
            }
        }

        return Instantiate(playerPrefab, spawnPos, spawnRot);
    }

    bool IsInRoomScene()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        return activeScene.path == RoomScene || activeScene.name == RoomScene;
    }

    void ClearTrackedSpawnPositions()
    {
        if (spawnSystem != null)
        {
            spawnSystem.ClearAllPlayerPositions();
        }
    }

    public void NotifyRoomPlayerStateChanged()
    {
        if (!NetworkServer.active || !IsInRoomScene())
            return;

        if (matchStartRoutine != null && !ShouldStartMatch())
        {
            CancelMatchStartIfNeeded();
            return;
        }

        if (matchStartRoutine == null && ShouldStartMatch())
        {
            matchStartRoutine = StartCoroutine(BeginMatchAfterDelay());
        }
    }

    bool ShouldStartMatch()
    {
        if (string.IsNullOrWhiteSpace(GameplayScene))
        {
            Debug.LogWarning("GameplayScene is not set on the NetworkRoomManager. Unable to start matches.");
            return false;
        }

        if (numPlayers == 0)
            return false;

        int readyCount = 0;
        foreach (NetworkRoomPlayer roomPlayer in roomSlots)
        {
            if (roomPlayer != null && roomPlayer.readyToBegin)
            {
                readyCount++;
            }
        }

        if (readyCount < playersPerMatch)
            return false;

        return readyCount == numPlayers;
    }

    IEnumerator BeginMatchAfterDelay()
    {
        NotifyMatchFound();

        if (matchStartDelay > 0f)
        {
            yield return new WaitForSeconds(matchStartDelay);
        }

        matchStartRoutine = null;
        ServerChangeScene(GameplayScene);
    }

    void NotifyMatchFound()
    {
        foreach (NetworkRoomPlayer roomPlayer in roomSlots)
        {
            if (roomPlayer is MatchmakingRoomPlayer matchmakingPlayer && roomPlayer.readyToBegin)
            {
                matchmakingPlayer.TargetMatchFound(matchmakingPlayer.connectionToClient, matchStartDelay);
            }
        }
    }

    void CancelMatchStartIfNeeded()
    {
        if (matchStartRoutine != null)
        {
            StopCoroutine(matchStartRoutine);
            matchStartRoutine = null;

            foreach (NetworkRoomPlayer roomPlayer in roomSlots)
            {
                if (roomPlayer is MatchmakingRoomPlayer matchmakingPlayer)
                {
                    matchmakingPlayer.TargetMatchCountdownCancelled(matchmakingPlayer.connectionToClient);
                }
            }
        }
    }
}
