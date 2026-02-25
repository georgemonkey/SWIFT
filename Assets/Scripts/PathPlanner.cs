using UnityEngine;
using System.Collections.Generic;

public static class PathPlanner
{
    public enum Algorithm
    {
        Lawnmower,
        Spiral,
        ExpandingSquare,
        RandomWalk
    }

    // 9m spacing — matches 5.2m LIDAR radius with slight overlap
    private const double DEFAULT_SPACING = 0.000081;

    public static List<(double lng, double lat)> GeneratePath(
        GeofenceSectorManager.Sector sector,
        Algorithm algorithm,
        double spacing = DEFAULT_SPACING)
    {
        double latRange = sector.maxLat - sector.minLat;
        double lngRange = sector.maxLng - sector.minLng;
        double latM = latRange * 111320.0;
        double lngM = lngRange * 111320.0;

        Debug.Log($"PathPlanner: Sector size = {latM:F1}m x {lngM:F1}m, " +
            $"spacing = {spacing * 111320:F1}m, " +
            $"algorithm = {algorithm}");

        // Auto-scale spacing if still too large for sector
        double minDimension = System.Math.Min(latRange, lngRange);
        if (spacing >= minDimension / 2.0)
        {
            spacing = minDimension / 4.0;
            Debug.LogWarning($"Spacing auto-scaled to " +
                $"{spacing * 111320:F1}m to fit sector.");
        }

        switch (algorithm)
        {
            case Algorithm.Lawnmower: return Lawnmower(sector, spacing);
            case Algorithm.Spiral: return Spiral(sector, spacing);
            case Algorithm.ExpandingSquare: return ExpandingSquare(sector, spacing);
            case Algorithm.RandomWalk: return RandomWalk(sector, spacing);
            default: return Lawnmower(sector, spacing);
        }
    }

    // ?? Lawnmower ?????????????????????????????????????????????????
    static List<(double, double)> Lawnmower(
        GeofenceSectorManager.Sector s, double spacing)
    {
        var waypoints = new List<(double, double)>();
        double buffer = spacing * 0.5;
        bool goingUp = true;
        double currentLng = s.minLng + buffer;

        while (currentLng <= s.maxLng - buffer)
        {
            if (goingUp)
            {
                waypoints.Add((currentLng, s.minLat + buffer));
                waypoints.Add((currentLng, s.maxLat - buffer));
            }
            else
            {
                waypoints.Add((currentLng, s.maxLat - buffer));
                waypoints.Add((currentLng, s.minLat + buffer));
            }
            goingUp = !goingUp;
            currentLng += spacing;
        }

        Debug.Log($"Lawnmower generated {waypoints.Count} waypoints.");
        return waypoints;
    }

    // ?? Spiral ????????????????????????????????????????????????????
    static List<(double, double)> Spiral(
        GeofenceSectorManager.Sector s, double spacing)
    {
        var waypoints = new List<(double, double)>();

        double minLat = s.minLat, maxLat = s.maxLat;
        double minLng = s.minLng, maxLng = s.maxLng;
        double buf = spacing * 0.5;

        minLat += buf; maxLat -= buf;
        minLng += buf; maxLng -= buf;

        int maxIterations = 1000;
        int iteration = 0;

        while (minLat < maxLat && minLng < maxLng && iteration < maxIterations)
        {
            // Bottom: left to right
            waypoints.Add((minLng, minLat));
            waypoints.Add((maxLng, minLat));

            // Right: bottom to top
            waypoints.Add((maxLng, minLat));
            waypoints.Add((maxLng, maxLat));

            // Top: right to left
            waypoints.Add((maxLng, maxLat));
            waypoints.Add((minLng, maxLat));

            // Left: top to bottom (stop before bottom to avoid overlap)
            waypoints.Add((minLng, maxLat));
            waypoints.Add((minLng, minLat + spacing));

            minLat += spacing;
            maxLat -= spacing;
            minLng += spacing;
            maxLng -= spacing;

            iteration++;
        }

        Debug.Log($"Spiral generated {waypoints.Count} waypoints.");
        return waypoints;
    }

    // ?? Expanding Square ??????????????????????????????????????????
    static List<(double, double)> ExpandingSquare(
        GeofenceSectorManager.Sector s, double spacing)
    {
        var waypoints = new List<(double, double)>();

        double centerLng = (s.minLng + s.maxLng) / 2.0;
        double centerLat = (s.minLat + s.maxLat) / 2.0;

        // Always start at center
        waypoints.Add((centerLng, centerLat));

        double maxRadius = System.Math.Min(
            (s.maxLng - s.minLng) / 2.0,
            (s.maxLat - s.minLat) / 2.0);

        double radius = spacing;

        while (radius <= maxRadius)
        {
            // SW ? SE ? NE ? NW ? back to SW
            waypoints.Add((centerLng - radius, centerLat - radius));
            waypoints.Add((centerLng + radius, centerLat - radius));
            waypoints.Add((centerLng + radius, centerLat + radius));
            waypoints.Add((centerLng - radius, centerLat + radius));
            waypoints.Add((centerLng - radius, centerLat - radius));

            radius += spacing;
        }

        Debug.Log($"Expanding Square generated {waypoints.Count} waypoints.");
        return waypoints;
    }

    // ?? Random Walk ???????????????????????????????????????????????
    static List<(double, double)> RandomWalk(
        GeofenceSectorManager.Sector s, double spacing)
    {
        var waypoints = new List<(double, double)>();
        var visited = new HashSet<string>();

        double currentLng = (s.minLng + s.maxLng) / 2.0;
        double currentLat = (s.minLat + s.maxLat) / 2.0;

        int maxSteps = 1000;
        int step = 0;

        double[][] directions = new double[][]
        {
            new double[] {  0,        spacing },  // N
            new double[] {  0,       -spacing },  // S
            new double[] {  spacing,  0       },  // E
            new double[] { -spacing,  0       },  // W
        };

        while (step < maxSteps)
        {
            string key = $"{currentLng:F6},{currentLat:F6}";
            if (!visited.Contains(key))
            {
                visited.Add(key);
                waypoints.Add((currentLng, currentLat));
            }

            // Prefer unvisited neighbors
            var unvisited = new List<int>();
            for (int i = 0; i < directions.Length; i++)
            {
                double nextLng = currentLng + directions[i][0];
                double nextLat = currentLat + directions[i][1];
                string nextKey = $"{nextLng:F6},{nextLat:F6}";

                if (!visited.Contains(nextKey)
                    && nextLng >= s.minLng && nextLng <= s.maxLng
                    && nextLat >= s.minLat && nextLat <= s.maxLat)
                    unvisited.Add(i);
            }

            if (unvisited.Count > 0)
            {
                int choice = unvisited[Random.Range(0, unvisited.Count)];
                currentLng += directions[choice][0];
                currentLat += directions[choice][1];
            }
            else
            {
                // All neighbors visited — pick any valid direction
                bool moved = false;
                for (int i = 0; i < directions.Length; i++)
                {
                    double nextLng = currentLng + directions[i][0];
                    double nextLat = currentLat + directions[i][1];
                    if (nextLng >= s.minLng && nextLng <= s.maxLng
                        && nextLat >= s.minLat && nextLat <= s.maxLat)
                    {
                        currentLng = nextLng;
                        currentLat = nextLat;
                        moved = true;
                        break;
                    }
                }
                if (!moved) break;
            }

            step++;
        }

        Debug.Log($"Random Walk generated {waypoints.Count} waypoints.");
        return waypoints;
    }
}