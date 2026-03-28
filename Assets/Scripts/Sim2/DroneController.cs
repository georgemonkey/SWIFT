using UnityEngine;
using Unity.Mathematics;
using CesiumForUnity;
using System.Collections;
using System.Collections.Generic;

public class DroneController : MonoBehaviour
{
    [Header("Status")]
    public int droneId;
    public float coveragePercent = 0f;
    public bool missionComplete = false;
    public bool hasLanded = false;
    public bool isLeader = false;
    public float batteryLevel = 100f;
    public float totalDistanceTraveled = 0f;
    public bool isTraveling = false;
    public bool missionStarted = false;

    [Header("Leader-Follower")]
    public List<DroneController> followers = new List<DroneController>();
    public float leaderPollRate = 1f;

    public PathPlanner.Algorithm Algorithm =>
        ParseAlgorithm(Variables.searchPattern);

    private GeofenceSectorManager.Sector sector;
    private CesiumGeoreference georeference;
    private List<(double lng, double lat)> waypoints;
    private int currentWaypointIndex = 0;
    private LidarSimulator lidar;
    private DroneAnchor anchor;

    private float travelSpeedDeg;
    private float searchSpeedDeg;

    private double homeLng;
    private double homeLat;

    private HashSet<string> coveredCells = new HashSet<string>();
    private int totalCells = 0;

    public double currentLng;
    public double currentLat;
    public double currentAlt = 150.0;

    private double sectorEntryLng;
    private double sectorEntryLat;

    private float pollTimer = 0f;

    private double lastLng, lastLat;
    private float stuckTimer = 0f;
    private const float STUCK_THRESHOLD = 5f;
    private const float MAX_DELTA_TIME = 0.05f;
    private const double SPACING_DEG = 0.000081;

    private GridOverlay gridOverlay;
    private MetricsOverlay metricsOverlay;
    private double lastTrackedLng, lastTrackedLat;

    // Track last cell marked to avoid redundant calls
    private int lastMarkedCellX = -1;
    private int lastMarkedCellY = -1;

    float BatteryDrainRate()
    {
        return isTraveling
            ? Variables.TRAVEL_DRAIN_RATE
            : Variables.SEARCH_DRAIN_RATE;
    }

    public void Initialize(GeofenceSectorManager.Sector s,
        CesiumGeoreference geo, int id)
    {
        sector = s;
        georeference = geo;
        droneId = id;
        isLeader = (id == 1);

        travelSpeedDeg = Variables.TRAVEL_SPEED_MS / 111320f;
        searchSpeedDeg = Variables.SEARCH_SPEED_MS / 111320f;

        lidar = GetComponent<LidarSimulator>();
        anchor = GetComponent<DroneAnchor>();

        if (anchor != null)
        {
            currentLng = anchor.Lng;
            currentLat = anchor.Lat;
            currentAlt = anchor.Alt;
            anchor.SetControlledByController(true);

            homeLng = anchor.Lng;
            homeLat = anchor.Lat;
        }

        sectorEntryLng = (sector.minLng + sector.maxLng) / 2.0;
        sectorEntryLat = sector.minLat;

        var algo = Algorithm;
        waypoints = PathPlanner.GeneratePath(sector, algo);

        double lngCells = (sector.maxLng - sector.minLng) / SPACING_DEG;
        double latCells = (sector.maxLat - sector.minLat) / SPACING_DEG;
        totalCells = Mathf.Max(1, (int)(lngCells * latCells));

        gridOverlay = FindObjectOfType<GridOverlay>();
        metricsOverlay = FindObjectOfType<MetricsOverlay>();
        lastTrackedLng = currentLng;
        lastTrackedLat = currentLat;
        totalDistanceTraveled = 0f;
        lastMarkedCellX = -1;
        lastMarkedCellY = -1;

        Debug.Log($"Drone {droneId} ready — " +
            $"{waypoints.Count} waypoints, " +
            $"grid: {lngCells:F0}x{latCells:F0} = {totalCells} cells, " +
            $"algorithm: {algo}, " +
            $"home: Lat={homeLat:F6}, Lng={homeLng:F6}");
    }

    public void StartMission()
    {
        if (missionStarted) return;
        missionStarted = true;
        StartCoroutine(FlyMission());
    }

    void Update()
    {
        if (!missionStarted) return;

        float safeDelta = Mathf.Min(Time.deltaTime, MAX_DELTA_TIME);
        batteryLevel -= BatteryDrainRate() * safeDelta;
        batteryLevel = Mathf.Max(0f, batteryLevel);

        if (batteryLevel <= Variables.rth && !missionComplete)
        {
            Debug.LogWarning($"Drone {droneId} RTH triggered at {batteryLevel:F1}%");
            StopAllCoroutines();
            missionComplete = true;
            StartCoroutine(ReturnToHome());
            return;
        }

        if (isLeader)
        {
            pollTimer += Time.deltaTime;
            if (pollTimer >= leaderPollRate)
            {
                pollTimer = 0f;
                MonitorFollowers();
            }
        }
    }

    IEnumerator FlyMission()
    {
        Debug.Log($"Drone {droneId} taking off...");
        yield return StartCoroutine(TakeOff());

        isTraveling = true;
        Debug.Log($"Drone {droneId} traveling to sector...");
        yield return StartCoroutine(
            FlyToWaypoint(sectorEntryLng, sectorEntryLat, travelSpeedDeg, markGrid: false));

        isTraveling = false;
        Debug.Log($"Drone {droneId} searching...");

        while (currentWaypointIndex < waypoints.Count)
        {
            if (batteryLevel <= Variables.rth) yield break;

            var target = waypoints[currentWaypointIndex];

            // Fly to waypoint — mark grid cells continuously during flight
            yield return StartCoroutine(
                FlyToWaypoint(target.lng, target.lat, searchSpeedDeg, markGrid: true));

            // Coverage tracking
            string cell = $"{target.lng:F5},{target.lat:F5}";
            coveredCells.Add(cell);
            coveragePercent = Mathf.Min(
                (float)coveredCells.Count / totalCells * 100f, 100f);

            // Distance traveled
            double dLng = target.lng - lastTrackedLng;
            double dLat = target.lat - lastTrackedLat;
            double distMeters = System.Math.Sqrt(dLng * dLng + dLat * dLat) * 111320.0;
            totalDistanceTraveled += (float)distMeters;
            lastTrackedLng = target.lng;
            lastTrackedLat = target.lat;

            // Metrics overlay redundant coverage
            if (metricsOverlay != null)
                metricsOverlay.RecordCellVisit(cell);

            Variables.totalWaypointsCompleted++;

            if (lidar != null)
            {
                lidar.Scan(currentLng, currentLat);
                Variables.totalDetections = lidar.GetDetectedObjects().Count;
            }

            currentWaypointIndex++;
        }

        missionComplete = true;
        Debug.Log($"Drone {droneId} route complete. " +
            $"Coverage: {coveragePercent:F1}%, Battery: {batteryLevel:F1}%.");

        yield return StartCoroutine(ReturnToHome());
    }

    IEnumerator TakeOff()
    {
        double targetAlt = currentAlt;
        currentAlt = targetAlt - 50.0;
        float climbRate = Variables.altitudeDelateRate;
        isTraveling = true;

        while (currentAlt < targetAlt)
        {
            float safeDelta = Mathf.Min(Time.deltaTime, MAX_DELTA_TIME);
            currentAlt += climbRate * safeDelta;
            UpdatePosition();
            yield return null;
        }
        currentAlt = targetAlt;
        isTraveling = false;
    }

    IEnumerator ReturnToHome()
    {
        isTraveling = true;
        yield return StartCoroutine(
            FlyToWaypoint(homeLng, homeLat, travelSpeedDeg, markGrid: false));
        isTraveling = false;
        yield return StartCoroutine(Land());
        hasLanded = true;
        Debug.Log($"Drone {droneId} landed. Final battery: {batteryLevel:F1}%");
    }

    IEnumerator Land()
    {
        double groundAlt = currentAlt - 50.0;
        float descentRate = Variables.altitudeDelateRate;

        while (currentAlt > groundAlt)
        {
            float safeDelta = Mathf.Min(Time.deltaTime, MAX_DELTA_TIME);
            currentAlt -= descentRate * safeDelta;
            currentAlt = System.Math.Max(currentAlt, groundAlt);
            UpdatePosition();
            yield return null;
        }
    }

    // ?????????????????????????????????????????????????????????????
    // FLY TO WAYPOINT
    // markGrid: true during search phase — marks every cell the
    // drone passes through, not just the endpoint
    // markGrid: false during travel and RTH
    // ?????????????????????????????????????????????????????????????

    IEnumerator FlyToWaypoint(double targetLng, double targetLat,
        float speedDeg, bool markGrid = false)
    {
        while (true)
        {
            double dlng = targetLng - currentLng;
            double dlat = targetLat - currentLat;
            double dist = System.Math.Sqrt(dlng * dlng + dlat * dlat);

            if (dist < 0.000005) break;

            float safeDelta = Mathf.Min(Time.deltaTime, MAX_DELTA_TIME);
            double step = speedDeg * safeDelta;
            double ratio = System.Math.Min(step / dist, 1.0);

            currentLng += dlng * ratio;
            currentLat += dlat * ratio;

            // Mark current cell every frame during search
            if (markGrid && gridOverlay != null)
                TryMarkCurrentCell();

            UpdatePosition();
            yield return null;
        }

        currentLng = targetLng;
        currentLat = targetLat;

        // Mark final cell
        if (markGrid && gridOverlay != null)
            TryMarkCurrentCell();

        UpdatePosition();
    }

    // ?????????????????????????????????????????????????????????????
    // MARK CURRENT CELL
    // Only calls MarkCellSearched when drone moves to a new cell
    // avoids redundant calls every frame when sitting in same cell
    // ?????????????????????????????????????????????????????????????

    private void TryMarkCurrentCell()
    {
        if (gridOverlay == null) return;

        float minLng = (float)gridOverlay.GetMinLng();
        float minLat = (float)gridOverlay.GetMinLat();
        float size = gridOverlay.cellSizeDeg;

        int cellX = Mathf.RoundToInt(((float)currentLng - minLng) / size - 0.5f);
        int cellY = Mathf.RoundToInt(((float)currentLat - minLat) / size - 0.5f);

        // Only call if we moved to a new cell
        if (cellX == lastMarkedCellX && cellY == lastMarkedCellY) return;

        lastMarkedCellX = cellX;
        lastMarkedCellY = cellY;

        gridOverlay.MarkCellSearched((float)currentLat, (float)currentLng);
    }

    void UpdatePosition()
    {
        if (georeference == null) return;

        double3 ecef = CesiumWgs84Ellipsoid
            .LongitudeLatitudeHeightToEarthCenteredEarthFixed(
                new double3(currentLng, currentLat, currentAlt));
        double3 unity = georeference
            .TransformEarthCenteredEarthFixedPositionToUnity(ecef);

        transform.position = new Vector3(
            (float)unity.x, (float)unity.y, (float)unity.z);
    }

    void MonitorFollowers()
    {
        foreach (var follower in followers)
        {
            if (follower == null) continue;
            if (!follower.missionStarted || follower.isTraveling) continue;
            if (follower.IsStuck()) ReassignSector(follower);
            if (follower.missionComplete) AssignAdditionalSector(follower);
        }
    }

    void ReassignSector(DroneController stuckDrone)
    {
        DroneController busiest = null;
        int mostWaypoints = 0;

        foreach (var follower in followers)
        {
            if (follower == stuckDrone ||
                follower.missionComplete ||
                follower.isTraveling ||
                !follower.missionStarted) continue;

            int remaining = follower.GetRemainingWaypoints();
            if (remaining > mostWaypoints)
            {
                mostWaypoints = remaining;
                busiest = follower;
            }
        }

        if (busiest != null && mostWaypoints > 10)
        {
            var split = busiest.SplitRemainingWaypoints();
            stuckDrone.AssignNewWaypoints(split);
            Debug.Log($"Leader: Reassigned {split.Count} waypoints " +
                $"from Drone {busiest.droneId} to {stuckDrone.droneId}");
        }
    }

    void AssignAdditionalSector(DroneController idleDrone)
    {
        foreach (var follower in followers)
        {
            if (follower.missionComplete || follower == idleDrone) continue;
            if (follower.isTraveling || !follower.missionStarted) continue;
            if (follower.GetRemainingWaypoints() > 20)
            {
                var split = follower.SplitRemainingWaypoints();
                idleDrone.AssignNewWaypoints(split);
                idleDrone.StartMission();
                Debug.Log($"Leader: Drone {idleDrone.droneId} assisting Drone {follower.droneId}");
                return;
            }
        }
    }

    public bool IsStuck()
    {
        if (isTraveling || !missionStarted) return false;

        double dist = System.Math.Sqrt(
            System.Math.Pow(currentLng - lastLng, 2) +
            System.Math.Pow(currentLat - lastLat, 2));

        if (dist < 0.000001f)
            stuckTimer += Time.deltaTime;
        else
        {
            stuckTimer = 0f;
            lastLng = currentLng;
            lastLat = currentLat;
        }

        return stuckTimer > STUCK_THRESHOLD && !missionComplete;
    }

    public int GetRemainingWaypoints() =>
        waypoints == null ? 0 : waypoints.Count - currentWaypointIndex;

    public List<(double lng, double lat)> SplitRemainingWaypoints()
    {
        int remaining = waypoints.Count - currentWaypointIndex;
        int splitPoint = currentWaypointIndex + remaining / 2;
        var secondHalf = waypoints.GetRange(splitPoint, waypoints.Count - splitPoint);
        waypoints.RemoveRange(splitPoint, waypoints.Count - splitPoint);
        return secondHalf;
    }

    public void AssignNewWaypoints(List<(double lng, double lat)> newWaypoints)
    {
        waypoints = newWaypoints;
        currentWaypointIndex = 0;
        missionComplete = false;
        hasLanded = false;
        totalCells = newWaypoints.Count;
        coveredCells.Clear();
    }

    PathPlanner.Algorithm ParseAlgorithm(string pattern)
    {
        switch (pattern.ToLower().Trim())
        {
            case "lawnmower": return PathPlanner.Algorithm.Lawnmower;
            case "spiral": return PathPlanner.Algorithm.Spiral;
            case "expanding square": return PathPlanner.Algorithm.ExpandingSquare;
            case "random walk": return PathPlanner.Algorithm.RandomWalk;
            default:
                Debug.LogWarning($"Unknown algorithm '{pattern}', defaulting to Lawnmower.");
                return PathPlanner.Algorithm.Lawnmower;
        }
    }

    public (double lng, double lat) GetPosition() => (currentLng, currentLat);
    public float GetCoverage() => coveragePercent;
    public float GetBattery() => batteryLevel;
    public bool IsTraveling() => isTraveling;
}