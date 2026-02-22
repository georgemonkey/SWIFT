using UnityEngine;
using Unity.Mathematics;
using CesiumForUnity;

public class DroneAnchor : MonoBehaviour
{
    public double Lng { get; private set; }
    public double Lat { get; private set; }
    public double Alt { get; private set; }

    private CesiumGeoreference georeference;
    private bool controlledByController = false;

    public void Initialize(double longitude, double latitude,
        double alt, CesiumGeoreference geo)
    {
        Lng = longitude;
        Lat = latitude;
        Alt = alt;
        georeference = geo;
    }

    public void SetControlledByController(bool controlled)
    {
        controlledByController = controlled;
    }

    void Update()
    {
        if (controlledByController || georeference == null) return;

        double3 ecef = CesiumWgs84Ellipsoid
            .LongitudeLatitudeHeightToEarthCenteredEarthFixed(
                new double3(Lng, Lat, Alt));
        double3 unity = georeference
            .TransformEarthCenteredEarthFixedPositionToUnity(ecef);

        transform.position = new Vector3(
            (float)unity.x, (float)unity.y, (float)unity.z);
    }
}