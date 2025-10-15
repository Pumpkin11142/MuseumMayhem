using Mirror;
using UnityEngine;

/// <summary>
/// Delivery zone for physically carried treasures (e.g., Statues).
/// Awards value when the statue enters and despawns it.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class DeliveryZone : NetworkBehaviour
{
    [Tooltip("Visual feedback prefab (e.g., sparkle or flash)")]
    public GameObject turnInEffect;

    private void OnTriggerEnter(Collider other)
    {
        if (!isServer) return;

        // Only statues are carried and should be deliverable
        var statue = other.GetComponent<StatueTreasure>();
        if (statue == null) return;

        // Only process if it's currently being carried (avoid early turn-ins)
        if (!statue.isActiveAndEnabled) return;

        HandleStatueDelivery(statue);
    }

    private void HandleStatueDelivery(StatueTreasure statue)
    {
        float value = statue.baseValue;

        // reward the carrier
        if (NetworkServer.spawned.TryGetValue(statue.netId, out NetworkIdentity id))
        {
            // no-op: we actually need the *carrier* identity, not the statue's
        }

        // Try to find the player near the statue
        var closestPlayer = FindClosestPlayer(statue.transform.position);
        if (closestPlayer != null)
        {
            var pr = closestPlayer.GetComponentInChildren<PlayerRound>();
            if (pr != null)
            {
                // award full or reduced value based on damage
                float award = statueCracked(statue) ? statue.baseValue * statue.crackedValueFraction : statue.baseValue;
                pr.AddValueServer(award);
            }
        }

        // feedback
        if (turnInEffect)
        {
            var fx = Instantiate(turnInEffect, statue.transform.position, Quaternion.identity);
            NetworkServer.Spawn(fx);
            Destroy(fx, 2f);
        }

        NetworkServer.Destroy(statue.gameObject);
        Debug.Log("[DeliveryZone] Statue delivered!");
    }

    private bool statueCracked(StatueTreasure statue)
    {
        // Optional helper – in case you track cracked state
        var field = typeof(StatueTreasure).GetField("isCracked", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return field != null && (bool)field.GetValue(statue);
    }

    private NetworkIdentity FindClosestPlayer(Vector3 position)
    {
        float closestDist = float.MaxValue;
        NetworkIdentity closest = null;

        foreach (var id in NetworkServer.spawned.Values)
        {
            if (id == null || !id.isLocalPlayer) continue;
            if (id.GetComponent<PlayerRound>() == null) continue;

            float d = Vector3.Distance(position, id.transform.position);
            if (d < closestDist)
            {
                closestDist = d;
                closest = id;
            }
        }

        return closest;
    }

    private void OnDrawGizmosSelected()
    {
        var box = GetComponent<BoxCollider>();
        if (box == null) return;
        Gizmos.color = new Color(0f, 1f, 0f, 0.25f);
        Gizmos.DrawCube(transform.position + box.center, box.size);
    }
}
