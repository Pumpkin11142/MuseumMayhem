using UnityEngine;

public enum RoomType { Straight, Turn, T, Other }

public class RoomData : MonoBehaviour
{
    [Header("Room Grid Size")]
    public int width = 4;
    public int depth = 4;

    [Header("Generation Settings")]
    public RoomType roomType = RoomType.Straight;

    [Range(1, 10)]
    public int spawnWeight = 5; // higher = more common
}
