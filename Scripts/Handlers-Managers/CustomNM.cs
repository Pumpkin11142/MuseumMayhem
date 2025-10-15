using UnityEngine;
using Mirror;

public class CustomNetworkManager : NetworkManager
{
    [Header("Dynamic Spawn System")]
    public DynamicSpawnSystem spawnSystem;

    [Header("Player Setup")]
    public bool disableRemotePlayerCamera = true;
    public bool disableRemotePlayerAudio = true;

    public override void OnStartServer()
    {
        base.OnStartServer();

        if (spawnSystem == null)
        {
            spawnSystem = FindObjectOfType<DynamicSpawnSystem>();
            if (spawnSystem == null)
            {
                Debug.LogError("No DynamicSpawnSystem found! Please add one to the scene.");
            }
        }
    }

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        // Get spawn position from dynamic spawn system
        Vector3 spawnPos = Vector3.zero;
        Quaternion spawnRot = Quaternion.identity;

        if (spawnSystem != null)
        {
            spawnPos = spawnSystem.GetSpawnPosition();
            // Optional: Make player face center or random direction
            spawnRot = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
        }
        else
        {
            // Fallback to default spawn position
            spawnPos = GetStartPosition().position;
            spawnRot = GetStartPosition().rotation;
        }

        // Instantiate player
        GameObject player = Instantiate(playerPrefab, spawnPos, spawnRot);

        // Setup player
        NetworkServer.AddPlayerForConnection(conn, player);

        Debug.Log($"Player spawned at {spawnPos}");
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        // Track position before player is destroyed
        if (conn.identity != null && spawnSystem != null)
        {
            spawnSystem.RemovePlayerPosition(conn.identity.transform.position);
        }

        base.OnServerDisconnect(conn);
    }

    public override void OnStopServer()
    {
        base.OnStopServer();

        // Clear all tracked positions when server stops
        if (spawnSystem != null)
        {
            spawnSystem.ClearAllPlayerPositions();
        }
    }
}