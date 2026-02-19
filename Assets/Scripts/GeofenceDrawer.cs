using UnityEngine;
using Unity.Mathematics;
using CesiumForUnity;
using System.Collections.Generic;

public class GeofenceDrawer : MonoBehaviour
{
    [Header("References")]
    public CesiumGeoreference georeference;
    public Camera mainCamera;

    [Header("Visuals")]
    public Color geofenceColor = new Color(0f, 1f, 1f, 0.3f);
    public Color outlineColor = Color.cyan;

    private Vector3 worldCornerA;
    private Vector3 worldCornerB;
    private bool firstPointSet = false;
    private bool geofenceFinalized = false;

    private GameObject geofenceQuad;
    private LineRenderer outlineRenderer;
    public GeofenceSectorManager sectorManager;

    void Update()
    {
        // Lazy load — grab it the first time Update runs
        if (sectorManager == null)
            sectorManager = GetComponent<GeofenceSectorManager>();

        if (geofenceFinalized) return;

        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (!firstPointSet)
                {
                    worldCornerA = hit.point;
                    firstPointSet = true;
                    Debug.Log("First corner set at: " + worldCornerA);
                }
                else
                {
                    worldCornerB = hit.point;
                    FinalizeGeofence();
                }
            }
            else
            {
                Debug.LogWarning("Raycast did not hit anything. Make sure terrain has a collider.");
            }
        }

        if (firstPointSet && !geofenceFinalized)
        {
            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                DrawPreview(worldCornerA, hit.point);
            }
        }
    }

    void DrawPreview(Vector3 a, Vector3 b)
    {
        Vector3[] corners = GetRectCorners(a, b);

        if (outlineRenderer == null)
        {
            GameObject lineObj = new GameObject("GeofencePreview");
            outlineRenderer = lineObj.AddComponent<LineRenderer>();
            outlineRenderer.loop = true;
            outlineRenderer.positionCount = 4;
            outlineRenderer.widthMultiplier = 0.5f;
            outlineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            outlineRenderer.startColor = outlineColor;
            outlineRenderer.endColor = outlineColor;
            outlineRenderer.useWorldSpace = true;
        }

        outlineRenderer.SetPositions(corners);
    }

    void FinalizeGeofence()
    {
        if (sectorManager == null)
        {
            Debug.LogError("Cannot finalize — GeofenceSectorManager still not found!");
            return;
        }

        geofenceFinalized = true;

        if (outlineRenderer != null)
            Destroy(outlineRenderer.gameObject);

        Vector3[] corners = GetRectCorners(worldCornerA, worldCornerB);
        DrawGeofenceMesh(corners);

        double3[] geoCorners = ConvertToGeoCoordinates(corners);
        sectorManager.SetGeofenceAndSplit(geoCorners);

        Debug.Log("Geofence finalized.");
    }

    Vector3[] GetRectCorners(Vector3 a, Vector3 b)
    {
        float minX = Mathf.Min(a.x, b.x);
        float maxX = Mathf.Max(a.x, b.x);
        float minZ = Mathf.Min(a.z, b.z);
        float maxZ = Mathf.Max(a.z, b.z);
        float y = (a.y + b.y) / 2f + 0.5f;

        return new Vector3[]
        {
            new Vector3(minX, y, minZ),
            new Vector3(maxX, y, minZ),
            new Vector3(maxX, y, maxZ),
            new Vector3(minX, y, maxZ),
        };
    }

    void DrawGeofenceMesh(Vector3[] corners)
    {
        geofenceQuad = new GameObject("GeofenceMesh");
        MeshFilter mf = geofenceQuad.AddComponent<MeshFilter>();
        MeshRenderer mr = geofenceQuad.AddComponent<MeshRenderer>();

        Mesh mesh = new Mesh();
        mesh.vertices = corners;
        mesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateNormals();
        mf.mesh = mesh;

        Material mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = geofenceColor;
        mr.material = mat;

        GameObject outlineObj = new GameObject("GeofenceOutline");
        LineRenderer lr = outlineObj.AddComponent<LineRenderer>();
        lr.loop = true;
        lr.positionCount = 4;
        lr.SetPositions(corners);
        lr.widthMultiplier = 0.5f;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = outlineColor;
        lr.endColor = outlineColor;
        lr.useWorldSpace = true;
    }

    double3[] ConvertToGeoCoordinates(Vector3[] corners)
    {
        double3[] result = new double3[corners.Length];
        for (int i = 0; i < corners.Length; i++)
        {
            double3 unityPos = new double3(corners[i].x, corners[i].y, corners[i].z);
            double3 ecef = georeference.TransformUnityPositionToEarthCenteredEarthFixed(unityPos);
            double3 llh = CesiumWgs84Ellipsoid.EarthCenteredEarthFixedToLongitudeLatitudeHeight(ecef);
            result[i] = llh;
            Debug.Log($"Corner {i}: Lon={llh.x:F6}, Lat={llh.y:F6}, Alt={llh.z:F2}");
        }
        return result;
    }

    public void ResetGeofence()
    {
        firstPointSet = false;
        geofenceFinalized = false;
        sectorManager = null;

        if (geofenceQuad != null) Destroy(geofenceQuad);
        if (outlineRenderer != null) Destroy(outlineRenderer.gameObject);

        foreach (var name in new[] { "GeofencePreview", "GeofenceOutline", "GeofenceMesh" })
        {
            GameObject obj = GameObject.Find(name);
            if (obj != null) Destroy(obj);
        }

        Debug.Log("Geofence reset.");
    }
}