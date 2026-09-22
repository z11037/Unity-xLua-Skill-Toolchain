using UnityEngine;

public class CameraTargetRotationLock : MonoBehaviour
{
    private void LateUpdate()
    {
        transform.rotation = Quaternion.identity;
    }
}