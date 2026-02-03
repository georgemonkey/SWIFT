using UnityEngine;
using System.Collections.Generic;

public class DroneAgent : MonoBehaviour
{
    public float speed = 3f;
    public List<Vector3> waypoints = new List<Vector3>();
    private int current = 0;

    void Start()
    {
        // example waypoints
        waypoints.Add(new Vector3(5,1,5));
        waypoints.Add(new Vector3(-5,1,5));
        waypoints.Add(new Vector3(-5,1,-5));
        waypoints.Add(new Vector3(5,1,-5));
    }

    void Update()
    {
        if(current >= waypoints.Count) return;

        Vector3 target = waypoints[current];
        Vector3 dir = (target - transform.position).normalized;
        transform.position += dir * speed * Time.deltaTime;

        if(Vector3.Distance(transform.position, target) < 0.2f)
            current++;
    }
}