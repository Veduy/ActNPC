using UnityEngine;

public class BillboardToCamera : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;

    private void LateUpdate()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (targetCamera == null)
            return;

        transform.LookAt(
            transform.position + targetCamera.transform.rotation * Vector3.forward,
            targetCamera.transform.rotation * Vector3.up
        );
    }
}
