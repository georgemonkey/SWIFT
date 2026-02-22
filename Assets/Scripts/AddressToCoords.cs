using UnityEngine;
using UnityEngine.Networking;
using CesiumForUnity;
using TMPro;
using System.Collections;
using System;
using Unity.Mathematics;

public class AddressToCoords : MonoBehaviour
{
    [Header("References")]
    public CesiumGeoreference cesiumGeoreference;
    public TMP_InputField addressInput;
    public TMP_Text statusText;

    [Header("Camera Reference")]
    public GameObject cesiumCameraObject; // drag DynamicCamera here

    public void OnSearchButtonClicked()
    {
        string address = addressInput.text.Trim();
        if (string.IsNullOrEmpty(address))
        {
            SetStatus("Please enter an address.");
            return;
        }
        SetStatus("Searching...");
        StartCoroutine(GetCoordinatesFromAddress(address));
    }

    IEnumerator GetCoordinatesFromAddress(string address)
    {
        string encodedAddress = UnityWebRequest.EscapeURL(address);
        string url = $"https://nominatim.openstreetmap.org/search?format=json&q={encodedAddress}&limit=1";

        using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
        {
            webRequest.SetRequestHeader("User-Agent", "UnityCesiumApp/1.0");
            webRequest.SetRequestHeader("Accept-Language", "en");
            webRequest.timeout = 10;

            yield return webRequest.SendWebRequest();

            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                SetStatus($"Network error: {webRequest.error}");
                yield break;
            }

            string json = webRequest.downloadHandler.text;
            Debug.Log($"Response: {json}");

            if (string.IsNullOrEmpty(json) || json == "[]")
            {
                SetStatus("Address not found.");
                yield break;
            }

            try
            {
                string firstResult = json.TrimStart('[').TrimEnd(']');
                int objEnd = firstResult.IndexOf("},");
                if (objEnd > 0)
                    firstResult = firstResult.Substring(0, objEnd + 1);

                string latStr = ExtractValue(firstResult, "lat");
                string lonStr = ExtractValue(firstResult, "lon");

                if (string.IsNullOrEmpty(latStr) || string.IsNullOrEmpty(lonStr))
                {
                    SetStatus("Could not parse coordinates.");
                    yield break;
                }

                double lat = double.Parse(latStr,
                    System.Globalization.CultureInfo.InvariantCulture);
                double lon = double.Parse(lonStr,
                    System.Globalization.CultureInfo.InvariantCulture);

                MoveToLocation(lat, lon);
            }
            catch (Exception e)
            {
                SetStatus("Parse error. Check console.");
                Debug.LogError($"Parse error: {e.Message}");
            }
        }
    }

    void MoveToLocation(double lat, double lon)
    {
        bool moved = false;

        // Method 1 — Try CesiumGlobeAnchor on the camera object
        if (cesiumCameraObject != null)
        {
            CesiumGlobeAnchor anchor = cesiumCameraObject
                .GetComponent<CesiumGlobeAnchor>();

            if (anchor != null)
            {
                anchor.longitudeLatitudeHeight =
                    new double3(lon, lat, 500);
                Debug.Log($"Moved camera anchor to: {lat}, {lon}");
                moved = true;
            }
            else
            {
                // Try finding anchor on children
                anchor = cesiumCameraObject
                    .GetComponentInChildren<CesiumGlobeAnchor>();
                if (anchor != null)
                {
                    anchor.longitudeLatitudeHeight =
                        new double3(lon, lat, 500);
                    Debug.Log($"Moved child camera anchor to: {lat}, {lon}");
                    moved = true;
                }
            }
        }

        // Method 2 — Move georeference origin directly
        if (!moved && cesiumGeoreference != null)
        {
            cesiumGeoreference.latitude = lat;
            cesiumGeoreference.longitude = lon;
            cesiumGeoreference.height = 500;
            Debug.Log($"Moved georeference to: {lat}, {lon}");
            moved = true;
        }

        if (moved)
            SetStatus($"Moved to: {lat:F5}, {lon:F5}");
        else
            SetStatus("Could not move camera — check references.");
    }

    string ExtractValue(string json, string key)
    {
        string search = $"\"{key}\":\"";
        int start = json.IndexOf(search);
        if (start == -1) return null;
        start += search.Length;
        int end = json.IndexOf("\"", start);
        if (end == -1) return null;
        return json.Substring(start, end - start);
    }

    void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
        Debug.Log(message);
    }
}