using UnityEngine;
using Unity.Mathematics;
using CesiumForUnity;
using System.Collections;
using System.Collections.Generic;

public class DroneController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 10f;

    [Header("Path Planning")]
    public PathPlanner.Algorithm algorithm = PathPlanner.Algorithm.Lawnmower;

    [Header("Status")]
    public int droneId;
    public float coveragePercent = 0f;
    public bool missionComplete = false;
    public bool isLeader = false;

    [Header("Leader-Follower")]
    public List<DroneController> followers = new List<DroneController>();
    public float leaderPollRate = 1f;

    private GeofenceSectorManager.Sector sector;
    private CesiumGeoreference georeference;
    private List<(double lng, double lat)> waypoints;
    private int currentWaypointIndex = 0;
    private LidarSimulator lidar;

    private HashSet<string> coveredCells = new HashSet<string>();
    private int totalCells = 0;

    public double currentLng;
    public double currentLat;
    public double currentAlt = 150.0;

    private bool missionStarted = false;
    private float pollTimer = 0f;

    // Stuck detection
    private double lastLng, lastLat;
    private float stuckTimer = 0f;
    private const float STUCK_THRESHOLD = 5f;

    public void Initialize(GeofenceSectorManager.Sector s,
        CesiumGeoreference geo, int id)
    {
        sector = s;
        georeference = geo;
        droneId = id;
        isLeader = (id == 1);

        lidar = GetComponent<LidarSimulator>();

        DroneAnchor anchor = GetComponent<DroneAnchor>();
        if (anchor != null)
        {
            currentLng = anchor.Lng;
            currentLat = anchor.Lat;
            currentAlt = anchor.Alt;
            anchor.SetControlledByController(true);
        }

        waypoints = PathPlanner.GeneratePath(sector, algorithm);
        totalCells = waypoints.Count;

        Debug.Log($"Drone {droneId} initialized — " +
            $"{waypoints.Count} waypoints, algorithm: {algorithm}");
    }

    public void StartMission()
    {
        if (missionStarted) return;
        missionStarted = true;
        StartCoroutine(FlyMission());
    }

    void Update()
    {
        if (!isLeader || !missionStarted) return;

        pollTimer += Time.deltaTime;
        if (pollTimer >= leaderPollRate)
        {
            pollTimer = 0f;
            MonitorFollowers();
        }
    }

    IEnumerator FlyMission()
    {
        Debug.Log($"Drone {droneId} taking off...");
        yield return StartCoroutine(TakeOff());

        while (currentWaypointIndex < waypoints.Count)
        {
            var target = waypoints[currentWaypointIndex];
            yield return StartCoroutine(FlyToWaypoint(target.lng, target.lat));

            string cell = $"{target.lng:F5},{target.lat:F5}";
            coveredCells.Add(cell);
            coveragePercent = (float)coveredCells.Count / totalCells * 100f;

            if (lidar != null)
                lidar.Scan(currentLng, currentLat);

            currentWaypointIndex++;
        }

        missionComplete = true;
        Debug.Log($"Drone {droneId} complete. Coverage: {coveragePercent:F1}%");
    }

    IEnumerator TakeOff()
    {
        double targetAlt = currentAlt;
        currentAlt = targetAlt - 50.0;

        while (currentAlt < targetAlt)
        {
            currentAlt += 5.0 * Time.deltaTime;
            UpdatePosition();
            yield return null;
        }
        currentAlt = targetAlt;
    }

    IEnumerator FlyToWaypoint(double targetLng, double targetLat)
    {
        while (true)
        {
            double dlng = targetLng - currentLng;
            double dlat = targetLat - currentLat;
            double dist = System.Math.Sqrt(dlng * dlng + dlat * dlat);

            if (dist < 0.000005) break;

            double step = moveSpeed * Time.deltaTime * 0.00001;
            double ratio = System.Math.Min(step / dist, 1.0);

            currentLng += dlng * ratio;
            currentLat += dlat * ratio;

            UpdatePosition();
            yield return null;
        }

        currentLng = targetLng;
        currentLat = targetLat;
        UpdatePosition();
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

    // ?? Leader-Follower ???????????????????????????????????????????
    void MonitorFollowers()
    {
        foreach (var follower in followers)
        {
            if (follower == null) continue;

            if (follower.IsStuck())
            {
                Debug.LogWarning($"Leader: Drone {follower.droneId} stuck. " +
                    $"Reassigning.");
                ReassignSector(follower);
            }

            if (follower.missionComplete)
                AssignAdditionalSector(follower);
        }
    }

    void ReassignSector(DroneController stuckDrone)
    {
        DroneController busiest = null;
        int mostWaypoints = 0;

        foreach (var follower in followers)
        {
            if (follower == stuckDrone || follower.missionComplete) continue;
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
                $"from Drone {busiest.droneId} to Drone {stuckDrone.droneId}");
        }
    }

    void AssignAdditionalSector(DroneController idleDrone)
    {
        foreach (var follower in followers)
        {
            if (follower.missionComplete || follower == idleDrone) continue;
            if (follower.GetRemainingWaypoints() > 20)
            {
                var split = follower.SplitRemainingWaypoints();
                idleDrone.AssignNewWaypoints(split);
                idleDrone.StartMission();
                Debug.Log($"Leader: Drone {idleDrone.droneId} " +
                    $"assisting Drone {follower.droneId}");
                return;
            }
        }
    }

    // ?? Stuck Detection ???????????????????????????????????????????
    public bool IsStuck()
    {
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
        var secondHalf = waypoints.GetRange(
            splitPoint, waypoints.Count - splitPoint);
        waypoints.RemoveRange(splitPoint, waypoints.Count - splitPoint);
        return secondHalf;
    }

    public void AssignNewWaypoints(List<(double lng, double lat)> newWaypoints)
    {
        waypoints = newWaypoints;
        currentWaypointIndex = 0;
        missionComplete = false;
        totalCells = newWaypoints.Count;
        coveredCells.Clear();
    }

    public (double lng, double lat) GetPosition() => (currentLng, currentLat);
    public float GetCoverage() => coveragePercent;
}