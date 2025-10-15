using UnityEngine;
using Mirror;
using System.Collections.Generic;
using System.Linq;

public class DynamicSpawnSystem : NetworkBehaviour
{
    [Header("Spawn Settings")]
    [Tooltip("Center point for spawn area")]
    public Transform spawnCenter;

    [Tooltip("Radius around center to spawn players")]
    public float spawnRadius = 10f;

    [Tooltip("Minimum distance between players when spawning")]
    public float minPlayerDistance = 3f;

    [Tooltip("Height above ground to check for valid spawn")]
    public float spawnHeight = 5f;

    [Tooltip("Maximum attempts to find a valid spawn point")]
    public int maxSpawnAttempts = 30;

    [Header("Validation")]
    [Tooltip("Layers that count as valid ground")]
    public LayerMask groundLayers = ~0;

    [Tooltip("Layers to check for obstructions")]
    public LayerMask obstructionLayers = ~0;

    [Tooltip("Radius to check for player collisions")]
    public float playerCheckRadius = 1f;

    [Tooltip("Height of player capsule for clearance check")]
    public float playerHeight = 2f;

    [Header("Fallback")]
    [Tooltip("Fallback spawn points if dynamic spawning fails")]
    public Transform[] fallbackSpawnPoints;

    [Tooltip("Use round-robin for fallback points")]
    public bool useFallbackRoundRobin = true;

    private int fallbackIndex = 0;
    private List<Vector3> activePlayerPositions = new List<Vector3>();

    void Start()
    {
        if (spawnCenter == null)
            spawnCenter = transform;
    }

    /// <summary>
    /// Find a valid spawn position for a new player
    /// </summary>
    public Vector3 GetSpawnPosition()
    {
        // Try to find a dynamic spawn point
        for (int attempt = 0; attempt < maxSpawnAttempts; attempt++)
        {
            Vector3 candidatePos = GenerateRandomPosition();

            if (IsValidSpawnPoint(candidatePos))
            {
                activePlayerPositions.Add(candidatePos);
                return candidatePos;
            }
        }

        // If all attempts fail, use fallback
        Debug.LogWarning($"Failed to find dynamic spawn after {maxSpawnAttempts} attempts. Using fallback.");
        return GetFallbackSpawnPosition();
    }

    /// <summary>
    /// Generate a random position within the spawn radius
    /// </summary>
    Vector3 GenerateRandomPosition()
    {
        // Random point in circle
        Vector2 randomCircle = Random.insideUnitCircle * spawnRadius;
        Vector3 randomPos = spawnCenter.position + new Vector3(randomCircle.x, spawnHeight, randomCircle.y);

        return randomPos;
    }

    /// <summary>
    /// Check if a position is valid for spawning
    /// </summary>
    bool IsValidSpawnPoint(Vector3 position)
    {
        // 1. Check if there's ground below
        if (!Physics.Raycast(position, Vector3.down, out RaycastHit groundHit, spawnHeight + 5f, groundLayers))
        {
            return false; // No ground found
        }

        Vector3 groundPosition = groundHit.point;

        // 2. Check if ground is relatively flat (optional but recommended)
        if (Vector3.Angle(groundHit.normal, Vector3.up) > 30f)
        {
            return false; // Too steep
        }

        // 3. Adjust to ground level + small offset
        Vector3 spawnPos = groundPosition + Vector3.up * 0.5f;

        // 4. Check for overhead obstructions
        if (Physics.Raycast(spawnPos, Vector3.up, playerHeight, obstructionLayers))
        {
            return false; // Something above blocking spawn
        }

        // 5. Check for obstructions at spawn point (capsule check)
        if (Physics.CheckCapsule(
            spawnPos + Vector3.up * playerCheckRadius,
            spawnPos + Vector3.up * (playerHeight - playerCheckRadius),
            playerCheckRadius,
            obstructionLayers))
        {
            return false; // Obstruction at spawn point
        }

        // 6. Check distance from other players
        if (!IsfarEnoughFromPlayers(spawnPos))
        {
            return false; // Too close to another player
        }

        // 7. Check distance from already spawned positions this session
        foreach (Vector3 existingPos in activePlayerPositions)
        {
            if (Vector3.Distance(spawnPos, existingPos) < minPlayerDistance)
            {
                return false;
            }
        }

        return true; // All checks passed!
    }

    /// <summary>
    /// Check if position is far enough from existing players
    /// </summary>
    bool IsfarEnoughFromPlayers(Vector3 position)
    {
        // Find all player objects in scene
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");

        foreach (GameObject player in players)
        {
            if (Vector3.Distance(position, player.transform.position) < minPlayerDistance)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Get a fallback spawn position
    /// </summary>
    Vector3 GetFallbackSpawnPosition()
    {
        if (fallbackSpawnPoints == null || fallbackSpawnPoints.Length == 0)
        {
            Debug.LogWarning("No fallback spawn points assigned! Using spawn center.");
            return spawnCenter.position + Vector3.up * 2f;
        }

        Vector3 spawnPos;

        if (useFallbackRoundRobin)
        {
            // Round-robin through fallback points
            spawnPos = fallbackSpawnPoints[fallbackIndex].position;
            fallbackIndex = (fallbackIndex + 1) % fallbackSpawnPoints.Length;
        }
        else
        {
            // Random fallback point
            spawnPos = fallbackSpawnPoints[Random.Range(0, fallbackSpawnPoints.Length)].position;
        }

        return spawnPos;
    }

    /// <summary>
    /// Remove a player position when they disconnect
    /// </summary>
    public void RemovePlayerPosition(Vector3 position)
    {
        activePlayerPositions.RemoveAll(pos => Vector3.Distance(pos, position) < 0.1f);
    }

    /// <summary>
    /// Clear all tracked player positions
    /// </summary>
    public void ClearAllPlayerPositions()
    {
        activePlayerPositions.Clear();
    }

    // Visualize spawn area in editor
    void OnDrawGizmosSelected()
    {
        if (spawnCenter == null) return;

        // Draw spawn radius
        Gizmos.color = Color.green;
        DrawCircle(spawnCenter.position, spawnRadius, 32);

        // Draw height indicator
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(spawnCenter.position + Vector3.up * spawnHeight, 0.5f);

        // Draw min distance indicator
        Gizmos.color = Color.red;
        DrawCircle(spawnCenter.position, minPlayerDistance, 16);

        // Draw fallback points
        if (fallbackSpawnPoints != null)
        {
            Gizmos.color = Color.cyan;
            foreach (Transform point in fallbackSpawnPoints)
            {
                if (point != null)
                {
                    Gizmos.DrawWireSphere(point.position, 1f);
                    Gizmos.DrawLine(point.position, point.position + Vector3.up * 2f);
                }
            }
        }
    }

    void DrawCircle(Vector3 center, float radius, int segments)
    {
        float angleStep = 360f / segments;
        Vector3 prevPoint = center + new Vector3(radius, 0, 0);

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * angleStep * Mathf.Deg2Rad;
            Vector3 newPoint = center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(prevPoint, newPoint);
            prevPoint = newPoint;
        }
    }
}