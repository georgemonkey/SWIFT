using UnityEngine;
using UnityEngine.UI; // Or TMPro if using TextMeshPro
using CesiumForUnity;
using UnityEngine.Networking;
using System.Collections;
using System;
using TMPro;

[Serializable]
public class GeocodeResult {
    public string lat;
    public string lon;
}

public class AddressToCoords : MonoBehaviour
{
    public CesiumGeoreference cesiumGeoreference;
    public TMP_InputField addressInput;

    public void OnSearchButtonClicked()
    {
        StartCoroutine(GetCoordinatesFromAddress(addressInput.text));
    }

    IEnumerator GetCoordinatesFromAddress(string address)
    {
        // Construct the URL (URL-encode the address to handle spaces)
        string url = $"https://nominatim.openstreetmap.org/search?format=json&q={UnityWebRequest.EscapeURL(address)}&limit=1";

        using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
        {
            // Set a User-Agent (Required by Nominatim's terms of service)
            webRequest.SetRequestHeader("User-Agent", "UnityCesiumApp/1.0");

            yield return webRequest.SendWebRequest();

            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                // Parse the JSON (Nominatim returns an array)
                string json = webRequest.downloadHandler.text;
                if (json != "[]")
                {
                    // Basic parsing: extracting lat/lon from the first result
                    // Note: For complex JSON, use a library like Newtonsoft or a wrapper class
                    string latStr = ExtractValue(json, "lat");
                    string lonStr = ExtractValue(json, "lon");

                    double lat = double.Parse(latStr);
                    double lon = double.Parse(lonStr);

                    // Update Cesium Georeference
                    cesiumGeoreference.latitude = lat;
                    cesiumGeoreference.longitude = lon;
                    
                    Debug.Log($"Moved to: {lat}, {lon}");
                }
                else
                {
                    Debug.LogError("Address not found.");
                }
            }
            else
            {
                Debug.LogError("Error: " + webRequest.error);
            }
        }
    }

    // Simple helper to grab values from the JSON string without external plugins
    string ExtractValue(string json, string key)
    {
        string search = $"\"{key}\":\"";
        int start = json.IndexOf(search) + search.Length;
        int end = json.IndexOf("\"", start);
        return json.Substring(start, end - start);
    }
}