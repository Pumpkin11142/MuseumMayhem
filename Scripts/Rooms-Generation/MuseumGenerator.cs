using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Mirror;

public class MuseumGenerator : NetworkBehaviour
{
    [Header("Room Modules")]
    public GameObject spawnRoomPrefab;
    public List<GameObject> normalRooms;
    public List<GameObject> endingRooms;

    [Header("Gallery Items")]
    [Tooltip("Folder path in Resources folder containing gallery item prefabs (e.g., 'GalleryItems')")]
    public string galleryItemsResourcePath = "GalleryItems";

    [Header("Gallery Spawn Settings")]
    [Range(0f, 1f)]
    [Tooltip("Chance that each gallery spot will spawn an item (0 = never, 1 = always)")]
    public float gallerySpawnChance = 0.8f;

    [Tooltip("If true, each gallery container will only spawn one random item type")]
    public bool oneItemTypePerGallery = true;

    [Tooltip("Gallery container names to look for in rooms")]
    public List<string> galleryContainerNames = new List<string> { "gallery_1", "gallery_2", "gallery_3" };

    [Header("Generation Settings")]
    public int maxRooms = 10;
    public int maxPlacementAttempts = 20;
    public float cellSize = 1f;
    public bool forceEndingRooms = true;

    [Header("Debug")]
    public bool showDebugLogs = true;
    public bool showDetailedLogs = false;
    public bool showGalleryLogs = false;

    private List<Connector> availableConnectors = new();
    private List<GameObject> spawnedRooms = new();
    private HashSet<Vector2Int> occupiedCells = new();

    private List<Connector> spawnRoomConnectors = new();
    private Dictionary<Connector, int> connectorUsageCount = new();
    private Dictionary<Connector, Connector> connectedPairs = new();

    private Dictionary<string, GameObject> galleryItemPrefabs = new();

    void Start()
    {
        // Run generation only on server so all clients see same museum
        if (!isServer)
            return;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        LoadGalleryItemPrefabs();
        GenerateMuseum();
    }


    void LoadGalleryItemPrefabs()
    {
        galleryItemPrefabs.Clear();
        GameObject[] loadedPrefabs = Resources.LoadAll<GameObject>(galleryItemsResourcePath);

        foreach (var prefab in loadedPrefabs)
        {
            galleryItemPrefabs[prefab.name] = prefab;
            if (showGalleryLogs)
                Debug.Log($"Loaded gallery item prefab: {prefab.name}");
        }

        if (showDebugLogs)
            Debug.Log($"Loaded {galleryItemPrefabs.Count} gallery item prefabs from '{galleryItemsResourcePath}'");
    }

    void GenerateMuseum()
    {
        // Instantiate starting room locally, validate, then spawn on server
        var startRoom = InstantiateRoom(spawnRoomPrefab, Vector3.zero, Quaternion.identity);
        ConfirmSpawnRoom(startRoom);
        MarkOccupiedCells(startRoom);

        spawnRoomConnectors = startRoom.GetComponentsInChildren<Connector>().ToList();
        foreach (var con in spawnRoomConnectors)
            connectorUsageCount[con] = 0;

        int roomsToPlace = Mathf.Max(0, maxRooms - 1);
        if (spawnRoomConnectors.Count == 0)
        {
            Debug.LogError("Spawn room prefab has no connectors!");
            return;
        }

        int baseRoomsPerConnector = roomsToPlace / spawnRoomConnectors.Count;
        int extraRooms = roomsToPlace % spawnRoomConnectors.Count;

        // Distribute rooms
        for (int i = 0; i < spawnRoomConnectors.Count; i++)
        {
            var connector = spawnRoomConnectors[i];
            int targetRooms = baseRoomsPerConnector + (i < extraRooms ? 1 : 0);
            if (targetRooms > 0)
                PlaceRoomBranch(connector, targetRooms);
        }

        // Cap with ending rooms
        var allOpenConnectors = new List<Connector>(availableConnectors);
        availableConnectors.Clear();

        foreach (var con in allOpenConnectors)
        {
            if (!IsConnectorUsed(con))
                TryCapConnector(con);
        }

        // Populate galleries
        PopulateAllRoomsWithGalleryItems();

        Debug.Log($"Museum generated with {spawnedRooms.Count} rooms total");
    }

    // -------------------
    // ROOM SPAWNING (separate instantiate vs network-spawn)
    // -------------------
    /// <summary>
    /// Instantiate a room locally for placement/testing. DOES NOT network-spawn.
    /// </summary>
    private GameObject InstantiateRoom(GameObject prefab, Vector3 pos, Quaternion rot)
    {
        GameObject obj = Instantiate(prefab, pos, rot);
        if (showDebugLogs) Debug.Log($"Instantiated room (local): {prefab.name}");
        return obj;
    }

    /// <summary>
    /// Confirm the instantiated room and spawn it on the network (server-only).
    /// Adds to spawnedRooms list and marks occupied cells.
    /// </summary>
    [Server]
    private void ConfirmSpawnRoom(GameObject room)
    {
        if (room == null) return;
        // Ensure it has a NetworkIdentity on the prefab (required for Mirror spawn).
        NetworkServer.Spawn(room);
        spawnedRooms.Add(room);
        if (showDebugLogs) Debug.Log($"NetworkSpawned room: {room.name}");
    }

    // -------------------
    // ROOM GENERATION
    // -------------------
    void PlaceRoomBranch(Connector startConnector, int targetRooms)
    {
        List<Connector> branchConnectors = new List<Connector> { startConnector };
        int roomsPlaced = 0;

        while (branchConnectors.Count > 0 && roomsPlaced < targetRooms)
        {
            int index = Random.Range(0, branchConnectors.Count);
            var connector = branchConnectors[index];
            branchConnectors.RemoveAt(index);

            if (IsConnectorUsed(connector)) continue;

            bool shouldPlaceEnding = (roomsPlaced >= targetRooms - 1);

            GameObject placedRoom = null;
            Connector usedNewConnector = null;

            if (shouldPlaceEnding && forceEndingRooms && endingRooms.Count > 0)
            {
                (placedRoom, usedNewConnector) = TryPlaceRoomFromList(endingRooms, connector);
            }
            else if (normalRooms.Count > 0)
            {
                (placedRoom, usedNewConnector) = TryPlaceRoomFromList(normalRooms, connector);
            }

            if (placedRoom != null)
            {
                roomsPlaced++;
                MarkConnectorsAsUsed(connector, usedNewConnector);

                if (spawnRoomConnectors.Contains(startConnector))
                    connectorUsageCount[startConnector]++;

                var newConnectors = GetAvailableConnectorsFromRoom(placedRoom, usedNewConnector);
                branchConnectors.AddRange(newConnectors);
                availableConnectors.AddRange(newConnectors);
            }
            else
            {
                // couldn't place off this connector; keep it as available
                availableConnectors.Add(connector);
            }
        }

        foreach (var con in branchConnectors)
            if (!IsConnectorUsed(con))
                availableConnectors.Add(con);
    }

    (GameObject room, Connector usedConnector) TryPlaceRoomFromList(List<GameObject> roomList, Connector existingConnector)
    {
        foreach (var prefab in WeightedShuffleList(roomList))
        {
            var result = TryPlaceRoom(prefab, existingConnector);
            if (result.room != null)
                return result;
        }
        return (null, null);
    }

    (GameObject room, Connector usedConnector) TryPlaceRoom(GameObject prefab, Connector existing)
    {
        var roomData = prefab.GetComponent<RoomData>();
        if (!roomData)
        {
            Debug.LogError($"{prefab.name} missing RoomData!");
            return (null, null);
        }

        for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
        {
            // instantiate locally for testing placement (do NOT network spawn yet)
            var newRoom = InstantiateRoom(prefab, Vector3.zero, Quaternion.identity);
            var newConnectors = newRoom.GetComponentsInChildren<Connector>().ToList();

            bool placed = false;
            Connector successfulNewConnector = null;

            foreach (var newCon in newConnectors.OrderBy(x => Random.value))
            {
                if (IsConnectorUsed(newCon)) continue;

                for (int rot = 0; rot < 4; rot++)
                {
                    newRoom.transform.rotation = Quaternion.Euler(0, rot * 90f, 0);
                    AlignRoomToConnector(newRoom, newCon, existing);

                    float dot = Vector3.Dot(newCon.transform.forward, -existing.transform.forward);
                    if (dot < 0.95f) continue;

                    if (CanPlaceRoom(newRoom))
                    {
                        // placement validated — now confirm spawn on the network (server)
                        ConfirmSpawnRoom(newRoom);
                        // after spawn, mark occupied cells on server state
                        MarkOccupiedCells(newRoom);
                        placed = true;
                        successfulNewConnector = newCon;
                        break;
                    }
                }
                if (placed) break;
            }

            if (placed)
            {
                return (newRoom, successfulNewConnector);
            }
            else
            {
                // nothing worked — destroy the local test object (it wasn't network-spawned)
                Destroy(newRoom);
            }
        }

        return (null, null);
    }

    bool TryCapConnector(Connector connector)
    {
        var result = TryPlaceRoomFromList(endingRooms, connector);
        if (result.room != null)
        {
            MarkConnectorsAsUsed(connector, result.usedConnector);
            return true;
        }
        return false;
    }

    // -------------------
    // HELPERS
    // -------------------
    void AlignRoomToConnector(GameObject newRoom, Connector newCon, Connector existing)
    {
        Quaternion targetRotation = Quaternion.LookRotation(-existing.transform.forward, Vector3.up);
        Vector3 offset = existing.transform.position - newCon.transform.position;
        newRoom.transform.position += offset;
    }

    List<Connector> GetAvailableConnectorsFromRoom(GameObject room, Connector usedConnector)
    {
        return room.GetComponentsInChildren<Connector>()
            .Where(c => !IsConnectorUsed(c) && c != usedConnector).ToList();
    }

    bool CanPlaceRoom(GameObject room)
    {
        foreach (var cell in GetRoomCells(room))
            if (occupiedCells.Contains(cell)) return false;
        return true;
    }

    void MarkOccupiedCells(GameObject room)
    {
        foreach (var cell in GetRoomCells(room))
            occupiedCells.Add(cell);
    }

    List<Vector2Int> GetRoomCells(GameObject room)
    {
        var data = room.GetComponent<RoomData>();
        var cells = new List<Vector2Int>();
        if (!data) return cells;

        Vector3 pos = room.transform.position;
        Vector3 right = room.transform.right;
        Vector3 forward = room.transform.forward;

        float halfWidth = (data.width - 1) * 0.5f;
        float halfDepth = (data.depth - 1) * 0.5f;

        for (int x = 0; x < data.width; x++)
            for (int z = 0; z < data.depth; z++)
            {
                float xOffset = (x - halfWidth) * cellSize;
                float zOffset = (z - halfDepth) * cellSize;
                Vector3 cellWorldPos = pos + right * xOffset + forward * zOffset;

                Vector2Int gridCell = new Vector2Int(
                    Mathf.RoundToInt(cellWorldPos.x / cellSize),
                    Mathf.RoundToInt(cellWorldPos.z / cellSize)
                );
                cells.Add(gridCell);
            }
        return cells;
    }

    void MarkConnectorsAsUsed(Connector con1, Connector con2)
    {
        if (con1 != null && con2 != null)
        {
            connectedPairs[con1] = con2;
            connectedPairs[con2] = con1;
        }
    }

    bool IsConnectorUsed(Connector connector) => connector != null && connectedPairs.ContainsKey(connector);

    List<GameObject> WeightedShuffleList(List<GameObject> list)
    {
        var expanded = new List<GameObject>();
        foreach (var item in list)
        {
            var data = item.GetComponent<RoomData>();
            int weight = data ? data.spawnWeight : 1;
            for (int i = 0; i < weight; i++) expanded.Add(item);
        }
        return expanded.OrderBy(x => Random.value).ToList();
    }

    // -------------------
    // GALLERY
    // -------------------
    void PopulateAllRoomsWithGalleryItems()
    {
        if (galleryItemPrefabs.Count == 0) return;
        foreach (var room in spawnedRooms)
            PopulateRoomWithGalleryItems(room);
    }

    int PopulateRoomWithGalleryItems(GameObject room)
    {
        int itemsSpawned = 0;
        foreach (string galleryName in galleryContainerNames)
        {
            Transform galleryContainer = FindDeepChild(room.transform, galleryName);
            if (galleryContainer != null)
                itemsSpawned += PopulateGalleryContainer(galleryContainer);
        }
        return itemsSpawned;
    }

    int PopulateGalleryContainer(Transform galleryContainer)
    {
        int itemsSpawned = 0;
        var placeholderObjects = new List<Transform>();
        for (int i = 0; i < galleryContainer.childCount; i++)
            placeholderObjects.Add(galleryContainer.GetChild(i));

        List<string> availableItemTypes = placeholderObjects
            .Where(p => galleryItemPrefabs.ContainsKey(p.name))
            .Select(p => p.name).Distinct().ToList();

        string selectedItemType = oneItemTypePerGallery && availableItemTypes.Count > 0
            ? availableItemTypes[Random.Range(0, availableItemTypes.Count)]
            : null;

        foreach (Transform placeholder in placeholderObjects)
        {
            if (!galleryItemPrefabs.ContainsKey(placeholder.name)) continue;
            if (oneItemTypePerGallery && placeholder.name != selectedItemType) continue;
            if (Random.value > gallerySpawnChance) continue;

            GameObject prefab = galleryItemPrefabs[placeholder.name];
            GameObject spawnedItem = Instantiate(prefab, placeholder.position, placeholder.rotation, placeholder.parent);

            // Spawn gallery items on network (server-only)
            if (NetworkServer.active)
            {
                NetworkServer.Spawn(spawnedItem);
            }

            // destroy placeholder (use Destroy, not DestroyImmediate)
            Destroy(placeholder.gameObject);
            itemsSpawned++;
        }
        return itemsSpawned;
    }

    Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            var found = FindDeepChild(child, name);
            if (found != null) return found;
        }
        return null;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1, 0, 0, 0.3f);
        foreach (var cell in occupiedCells)
            Gizmos.DrawCube(new Vector3(cell.x * cellSize, 0, cell.y * cellSize),
                            new Vector3(cellSize * 0.9f, 0.1f, cellSize * 0.9f));
    }
}
