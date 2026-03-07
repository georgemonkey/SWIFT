using UnityEngine;
using Unity.Mathematics;
using CesiumForUnity;

public class DroneTrail : MonoBehaviour
{
    [Header("Trail Settings")]
    public float trailTime = 8f;
    public float trailWidth = 0.5f;
    public Color trailColor = Color.cyan;

    private TrailRenderer trail;
    private DroneController controller;

    void Start()
    {
        controller = GetComponent<DroneController>();

        trail = gameObject.AddComponent<TrailRenderer>();
        trail.time = trailTime;
        trail.startWidth = trailWidth;
        trail.endWidth = 0f;
        trail.minVertexDistance = 0.5f;
        trail.autodestruct = false;

        // Fading gradient
        UpdateTrailColor(trailColor);

        trail.material = new Material(
            Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Unlit/Color"));

        // Start disabled — enable when drone starts moving
        trail.enabled = false;
    }

    void LateUpdate()
    {
        if (controller == null) return;

        // Enable trail once moving
        if (!trail.enabled)
        {
            bool isMoving = controller.IsTraveling() ||
                controller.coveragePercent > 0f;
            if (isMoving) trail.enabled = true;
        }

        // Update trail color based on mode
        if (trail.enabled)
        {
            Color targetColor = controller.IsTraveling()
                ? Color.yellow
                : trailColor;
            UpdateTrailColor(targetColor);
        }
    }

    void UpdateTrailColor(Color color)
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(color, 0f),
                new GradientColorKey(color, 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.8f, 0f),
                new GradientAlphaKey(0f,   1f)
            }
        );
        if (trail != null)
            trail.colorGradient = g;
    }

    public void ResetTrail()
    {
        if (trail != null)
        {
            trail.Clear();
            trail.enabled = false;
        }
    }
}
