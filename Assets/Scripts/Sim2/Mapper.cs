using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class MapboxWorldMap : MonoBehaviour
{
    [Header("UI Components")]
    public TMP_InputField locationInput;
    public RawImage mapDisplay;
    public Button searchButton;
    public Button zoomInButton;
    public Button zoomOutButton;
    public Button topDownButton;
    public Button angledButton;
    public TextMeshProUGUI coordinatesText;
    public TextMeshProUGUI statusText;
    
    [Header("Geofence UI")]
    public Button addGeofenceButton;
    public TMP_InputField geofenceNameInput;
    public TMP_InputField geofenceRadiusInput;
    public Toggle allowedZoneToggle;
    public Transform geofenceListPanel;
    public GameObject geofenceItemPrefab;
    
    [Header("Map Settings")]
    public int mapWidth = 1280;
    public int mapHeight = 720;
    public float zoomLevel = 14f;
    public float bearing = 0f;
    public float pitch = 0f;
    
    [Header("Animation")]
    public float transitionSpeed = 0.5f;
    
    [Header("Geofencing")]
    public bool enableGeofence = true;
    public List<GeofenceZone> geofenceZones = new List<GeofenceZone>();
    public Color allowedZoneColor = new Color(0, 1, 0, 0.5f);
    public Color restrictedZoneColor = new Color(1, 0, 0, 0.5f);
    
    [Header("Style")]
    public MapStyle stylePreset = MapStyle.SatelliteStreets;
    public bool useRetina = true;
    
    private string accessToken = "pk.eyJ1IjoicGFydGhhbXJhZGthciIsImEiOiJjbWwzNzZ6d3AwcWg1M2RveDdweHZ0NDJ6In0.-ccTvXollt75_16xo-_BgQ";
    private string currentLocation = "New York";
    private Vector2 currentCoords = new Vector2(-74.0060f, 40.7128f); // Default NYC
    private bool selectingGeofenceLocation = false;
    
    [System.Serializable]
    public class GeofenceZone
    {
        public string zoneName;
        public Vector2 centerCoords;
        public float radiusKm;
        public bool isAllowed = true;
    }
    
    public enum MapStyle
    {
        Dark,
        Satellite,
        SatelliteStreets,
        Streets,
        Outdoors
    }
    
    void Start()
    {
        // Button listeners
        if (searchButton != null) searchButton.onClick.AddListener(SearchLocation);
        if (zoomInButton != null) zoomInButton.onClick.AddListener(ZoomIn);
        if (zoomOutButton != null) zoomOutButton.onClick.AddListener(ZoomOut);
        if (topDownButton != null) topDownButton.onClick.AddListener(SetTopDownView);
        if (angledButton != null) angledButton.onClick.AddListener(SetAngledView);
        if (addGeofenceButton != null) addGeofenceButton.onClick.AddListener(StartGeofenceSelection);
        
        // Load initial map
        UpdateStatus("Loading map...", Color.cyan);
        StartCoroutine(FetchMapImage(currentCoords.x, currentCoords.y));
    }
    
    public void SearchLocation()
    {
        string location = locationInput.text;
        if (string.IsNullOrEmpty(location))
        {
            UpdateStatus("Enter a location!", Color.yellow);
            return;
        }
        
        currentLocation = location;
        UpdateStatus("Searching...", Color.cyan);
        StartCoroutine(GeocodeAndLoadMap(location));
    }
    
    IEnumerator GeocodeAndLoadMap(string location)
    {
        string geocodeUrl = $"https://api.mapbox.com/geocoding/v5/mapbox.places/{UnityWebRequest.EscapeURL(location)}.json?access_token={accessToken}";
        
        using (UnityWebRequest request = UnityWebRequest.Get(geocodeUrl))
        {
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                string json = request.downloadHandler.text;
                Vector2 coords = ParseCoordinates(json);
                
                if (coords != Vector2.zero)
                {
                    currentCoords = coords;
                    yield return StartCoroutine(FetchMapImage(coords.x, coords.y));
                    CheckGeofence(coords);
                    UpdateStatus("Map loaded!", Color.green);
                }
                else
                {
                    UpdateStatus("Location not found", Color.red);
                }
            }
            else
            {
                UpdateStatus($"Error: {request.error}", Color.red);
            }
        }
    }
    
    Vector2 ParseCoordinates(string json)
    {
        try
        {
            int centerIndex = json.IndexOf("\"center\":[");
            if (centerIndex == -1) return Vector2.zero;
            
            int startBracket = json.IndexOf('[', centerIndex + 10);
            int endBracket = json.IndexOf(']', startBracket);
            string coordsString = json.Substring(startBracket + 1, endBracket - startBracket - 1);
            
            string[] coords = coordsString.Split(',');
            float lon = float.Parse(coords[0]);
            float lat = float.Parse(coords[1]);
            
            return new Vector2(lon, lat);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Parse error: {e.Message}");
            return Vector2.zero;
        }
    }
    
    IEnumerator FetchMapImage(float longitude, float latitude)
    {
        string style = GetStyleString();
        string retinaTag = useRetina ? "@2x" : "";
        
        // Simple URL without overlay for now (to fix display)
        string url = $"https://api.mapbox.com/styles/v1/mapbox/{style}/static/" +
                    $"{longitude},{latitude},{zoomLevel},{bearing},{pitch}/" +
                    $"{mapWidth}x{mapHeight}{retinaTag}?" +
                    $"access_token={accessToken}";
        
        Debug.Log($"Fetching map: {url}");
        
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
        {
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                Texture2D mapTexture = DownloadHandlerTexture.GetContent(request);
                if (mapDisplay != null)
                {
                    mapDisplay.texture = mapTexture;
                    Debug.Log("Map texture applied successfully!");
                }
                
                if (coordinatesText != null)
                {
                    float altitude = GetAltitudeFromZoom(zoomLevel);
                    coordinatesText.text = $"LAT: {latitude:F4}° | LON: {longitude:F4}° | ALT: {altitude:F0}m";
                }
            }
            else
            {
                Debug.LogError($"Map fetch failed: {request.error}");
                UpdateStatus($"Failed to load map: {request.error}", Color.red);
            }
        }
    }
    
    void CheckGeofence(Vector2 position)
    {
        if (!enableGeofence || geofenceZones.Count == 0)
        {
            UpdateStatus("Open Airspace", Color.cyan);
            return;
        }
        
        foreach (var zone in geofenceZones)
        {
            float distance = CalculateDistance(position, zone.centerCoords);
            
            if (distance <= zone.radiusKm)
            {
                if (!zone.isAllowed)
                {
                    UpdateStatus($"⚠ RESTRICTED: {zone.zoneName}", Color.red);
                }
                else
                {
                    UpdateStatus($"✓ Allowed: {zone.zoneName}", Color.green);
                }
                return;
            }
        }
        
        UpdateStatus("Open Airspace", Color.cyan);
    }
    
    float CalculateDistance(Vector2 point1, Vector2 point2)
    {
        float lat1 = point1.y * Mathf.Deg2Rad;
        float lat2 = point2.y * Mathf.Deg2Rad;
        float dLat = (point2.y - point1.y) * Mathf.Deg2Rad;
        float dLon = (point2.x - point1.x) * Mathf.Deg2Rad;
        
        float a = Mathf.Sin(dLat / 2) * Mathf.Sin(dLat / 2) +
                  Mathf.Cos(lat1) * Mathf.Cos(lat2) *
                  Mathf.Sin(dLon / 2) * Mathf.Sin(dLon / 2);
        
        float c = 2 * Mathf.Atan2(Mathf.Sqrt(a), Mathf.Sqrt(1 - a));
        return 6371 * c;
    }
    
    float GetAltitudeFromZoom(float zoom)
    {
        return Mathf.Pow(2, 20 - zoom) * 100f;
    }
    
    void UpdateStatus(string message, Color color)
    {
        if (statusText != null)
        {
            statusText.text = message;
            statusText.color = color;
        }
        Debug.Log($"Status: {message}");
    }
    
    string GetStyleString()
    {
        switch (stylePreset)
        {
            case MapStyle.Dark: return "dark-v11";
            case MapStyle.Satellite: return "satellite-v9";
            case MapStyle.SatelliteStreets: return "satellite-streets-v12";
            case MapStyle.Streets: return "streets-v12";
            case MapStyle.Outdoors: return "outdoors-v12";
            default: return "satellite-streets-v12";
        }
    }
    
    // ===== GEOFENCE SELECTION =====
    
    public void StartGeofenceSelection()
    {
        selectingGeofenceLocation = true;
        UpdateStatus("Click on map to place geofence zone", Color.yellow);
    }
    
    void Update()
    {
        // Allow clicking on map to select geofence location
        if (selectingGeofenceLocation && Input.GetMouseButtonDown(0))
        {
            if (RectTransformUtility.RectangleContainsScreenPoint(
                mapDisplay.rectTransform, Input.mousePosition))
            {
                Vector2 clickPos = GetMapCoordinatesFromClick(Input.mousePosition);
                CreateGeofenceAtPosition(clickPos);
                selectingGeofenceLocation = false;
            }
        }
    }
    
    Vector2 GetMapCoordinatesFromClick(Vector2 screenPos)
    {
        // Convert screen click to map coordinates
        RectTransform rt = mapDisplay.rectTransform;
        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rt, screenPos, null, out localPoint);
        
        // Normalize to 0-1 range
        Vector2 normalizedPos = new Vector2(
            (localPoint.x + rt.rect.width / 2) / rt.rect.width,
            (localPoint.y + rt.rect.height / 2) / rt.rect.height
        );
        
        // Convert to approximate lat/lon offset from center
        // This is a rough approximation - for production use proper map projection
        float lonOffset = (normalizedPos.x - 0.5f) * 0.1f * (22 - zoomLevel);
        float latOffset = (0.5f - normalizedPos.y) * 0.1f * (22 - zoomLevel);
        
        return new Vector2(
            currentCoords.x + lonOffset,
            currentCoords.y + latOffset
        );
    }
    
    void CreateGeofenceAtPosition(Vector2 coords)
    {
        string zoneName = string.IsNullOrEmpty(geofenceNameInput?.text) 
            ? $"Zone {geofenceZones.Count + 1}" 
            : geofenceNameInput.text;
        
        float radius = 2f; // Default 2km
        if (geofenceRadiusInput != null && !string.IsNullOrEmpty(geofenceRadiusInput.text))
        {
            float.TryParse(geofenceRadiusInput.text, out radius);
        }
        
        bool isAllowed = allowedZoneToggle != null ? allowedZoneToggle.isOn : true;
        
        GeofenceZone newZone = new GeofenceZone
        {
            zoneName = zoneName,
            centerCoords = coords,
            radiusKm = radius,
            isAllowed = isAllowed
        };
        
        geofenceZones.Add(newZone);
        UpdateStatus($"Geofence '{zoneName}' added!", Color.green);
        
        // Refresh map with new zone
        StartCoroutine(FetchMapImage(currentCoords.x, currentCoords.y));
        
        // Add to list UI
        AddGeofenceToList(newZone);
        
        // Clear inputs
        if (geofenceNameInput != null) geofenceNameInput.text = "";
        if (geofenceRadiusInput != null) geofenceRadiusInput.text = "";
    }
    
    void AddGeofenceToList(GeofenceZone zone)
    {
        if (geofenceListPanel == null || geofenceItemPrefab == null) return;
        
        GameObject item = Instantiate(geofenceItemPrefab, geofenceListPanel);
        TextMeshProUGUI itemText = item.GetComponentInChildren<TextMeshProUGUI>();
        if (itemText != null)
        {
            string status = zone.isAllowed ? "✓ ALLOWED" : "⚠ RESTRICTED";
            itemText.text = $"{zone.zoneName} | {zone.radiusKm}km | {status}";
            itemText.color = zone.isAllowed ? allowedZoneColor : restrictedZoneColor;
        }
        
        Button deleteBtn = item.GetComponentInChildren<Button>();
        if (deleteBtn != null)
        {
            deleteBtn.onClick.AddListener(() => RemoveGeofence(zone, item));
        }
    }
    
    void RemoveGeofence(GeofenceZone zone, GameObject itemUI)
    {
        geofenceZones.Remove(zone);
        Destroy(itemUI);
        StartCoroutine(FetchMapImage(currentCoords.x, currentCoords.y));
        UpdateStatus($"Removed {zone.zoneName}", Color.yellow);
    }
    
    // ===== ZOOM & VIEW CONTROLS =====
    
    public void ZoomIn()
    {
        zoomLevel = Mathf.Min(zoomLevel + 1f, 22f);
        StartCoroutine(FetchMapImage(currentCoords.x, currentCoords.y));
    }
    
    public void ZoomOut()
    {
        zoomLevel = Mathf.Max(zoomLevel - 1f, 0f);
        StartCoroutine(FetchMapImage(currentCoords.x, currentCoords.y));
    }
    
    public void SetTopDownView()
    {
        pitch = 0f;
        bearing = 0f;
        StartCoroutine(FetchMapImage(currentCoords.x, currentCoords.y));
    }
    
    public void SetAngledView()
    {
        pitch = 60f;
        StartCoroutine(FetchMapImage(currentCoords.x, currentCoords.y));
    }
}