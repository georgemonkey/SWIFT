using UnityEngine;

public static class Variables
{
    public static float rth = 20f;
    public static int droneCount = 4;
    public static int activeDroneCount = 4;
    public static float altitudeDelateRate = 5f;
    public static float seperationDistance = 10f;
    public static string aggression = "Gentle";
    public static string searchPattern = "Lawnmower";

    // Physical constants
    public const float TRAVEL_SPEED_MS = 35.76f;   // 80 mph in m/s
    public const float SEARCH_SPEED_MS = 13.41f;   // 30 mph in m/s
    public const float DRONE_SIZE_M = 0.1524f;  // 6 inches in meters

    // Battery drain %/s based on v² scaling + hover penalty
    public const float TRAVEL_DRAIN_RATE = 0.1235f;
    public const float SEARCH_DRAIN_RATE = 0.1042f;
    public const float DRONE_VISUAL_SCALE = 5f; // scale up for visibility

    // Mission stats (written during simulation, read by stats overlay)
    public static float missionElapsedTime = 0f;
    public static float totalAreaCoveredSqFt = 0f;
    public static int totalDetections = 0;
    public static int totalWaypointsCompleted = 0;
}