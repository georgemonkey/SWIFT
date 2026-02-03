using UnityEngine;

public class CameraController : MonoBehaviour
{
    public float panSpeed = 20f;
    public float zoomSpeed = 20f;
    public float rotationSpeed = 50f;

    void Update()
    {
        
        float h = Input.GetAxis("Horizontal");
        float s = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        transform.position += new Vector3(h,0,v) * panSpeed * Time.deltaTime;

        
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        transform.position += transform.forward * scroll * zoomSpeed;

        
        if(Input.GetKey(KeyCode.Q)) transform.Rotate(Vector3.up, -rotationSpeed * Time.deltaTime, Space.World);
        if(Input.GetKey(KeyCode.E)) transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);

    }
}