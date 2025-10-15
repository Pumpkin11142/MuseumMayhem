using UnityEngine;
using Mirror;
using ProximityPrompts; // Make sure this matches your namespace

/// <summary>
/// Example script that spawns a cube prefab when this prompt is triggered.
/// Attach this to an object that already has a ProximityPrompt.
/// </summary>
public class SpawnCubeOnPrompt : NetworkBehaviour
{
    [Header("Prefab to Spawn")]
    public GameObject cubePrefab;

    [Header("Spawn Settings")]
    [Tooltip("How high above the prompt object the cube spawns")]
    public float spawnHeight = 2f;

    private ProximityPrompt prompt;

    private void Start()
    {
        prompt = GetComponent<ProximityPrompt>();

        if (prompt == null)
        {
            Debug.LogError("[SpawnCubeOnPrompt] No ProximityPrompt found on this object!");
            return;
        }

        // Subscribe to the Triggered event
        prompt.Triggered += OnPromptTriggered;
    }

    private void OnDestroy()
    {
        if (prompt != null)
            prompt.Triggered -= OnPromptTriggered;
    }

    /// <summary>
    /// Called when the player triggers the proximity prompt.
    /// </summary>
    private void OnPromptTriggered(NetworkIdentity player)
    {
        // Make sure we only run this logic on the server (Mirror best practice)
        if (!isServer) return;

        // Check prefab validity
        if (cubePrefab == null)
        {
            Debug.LogWarning("[SpawnCubeOnPrompt] No cube prefab assigned!");
            return;
        }

        // Calculate spawn position above this object
        Vector3 spawnPos = transform.position + Vector3.up * spawnHeight;

        // Spawn the cube
        GameObject cube = Instantiate(cubePrefab, spawnPos, Quaternion.identity);

        // Add physics if not already present
        if (cube.GetComponent<Rigidbody>() == null)
        {
            var rb = cube.AddComponent<Rigidbody>();
            rb.mass = 1f;
        }

        // Spawn over the network (since you're using Mirror)
        NetworkServer.Spawn(cube);

        Debug.Log($"[SpawnCubeOnPrompt] Spawned cube at {spawnPos}");
    }
}
