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

    private const double DEFAULT_SPACING = 0.0001;

    public static List<(double lng, double lat)> GeneratePath(
        GeofenceSectorManager.Sector sector,
        Algorithm algorithm,
        double spacing = DEFAULT_SPACING)
    {
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

        while (minLat < maxLat && minLng < maxLng)
        {
            waypoints.Add((minLng, minLat));
            waypoints.Add((maxLng, minLat));
            waypoints.Add((maxLng, maxLat));
            waypoints.Add((minLng, maxLat));
            waypoints.Add((minLng, minLat + spacing));

            minLat += spacing;
            maxLat -= spacing;
            minLng += spacing;
            maxLng -= spacing;
        }

        return waypoints;
    }

    // ?? Expanding Square ??????????????????????????????????????????
    static List<(double, double)> ExpandingSquare(
        GeofenceSectorManager.Sector s, double spacing)
    {
        var waypoints = new List<(double, double)>();

        double centerLng = (s.minLng + s.maxLng) / 2.0;
        double centerLat = (s.minLat + s.maxLat) / 2.0;

        double maxRadius = System.Math.Min(
            (s.maxLng - s.minLng) / 2.0,
            (s.maxLat - s.minLat) / 2.0) - spacing * 0.5;

        double radius = spacing;

        while (radius <= maxRadius)
        {
            waypoints.Add((centerLng - radius, centerLat - radius));
            waypoints.Add((centerLng + radius, centerLat - radius));
            waypoints.Add((centerLng + radius, centerLat + radius));
            waypoints.Add((centerLng - radius, centerLat + radius));
            waypoints.Add((centerLng - radius, centerLat - radius));
            radius += spacing;
        }

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

        int maxSteps = 500;
        int step = 0;

        double[][] directions = new double[][]
        {
            new double[] {  0,       spacing },
            new double[] {  0,      -spacing },
            new double[] {  spacing, 0       },
            new double[] { -spacing, 0       },
        };

        while (step < maxSteps)
        {
            string key = $"{currentLng:F6},{currentLat:F6}";
            if (!visited.Contains(key))
            {
                visited.Add(key);
                waypoints.Add((currentLng, currentLat));
            }

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

        return waypoints;
    }
}