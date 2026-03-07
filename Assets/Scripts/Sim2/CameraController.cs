using UnityEngine;

public class CameraController : MonoBehaviour
{
    public float panSpeed = 200f;
    public float zoomSpeed = 200f;
    public float rotationSpeed = 50f;

    void Update()
    {
        Debug.Log("skibs");
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        // pan relative to camera direction
        Vector3 move = transform.right * h + transform.forward * v;
        transform.position += move * panSpeed * Time.deltaTime;

        // zoom
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        transform.position += transform.forward * scroll * zoomSpeed;

        // rotate
        if (Input.GetKey(KeyCode.Q))
            transform.Rotate(Vector3.up, -rotationSpeed * Time.deltaTime, Space.Self);
        if (Input.GetKey(KeyCode.E))
            transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.Self);
    }
}