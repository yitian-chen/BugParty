namespace PixelCamera
{
    using UnityEngine;

    /// <summary>
    /// Example on how to ray cast events with the camera system.
    /// </summary>
    public class RayCastDemo : MonoBehaviour
    {
        public Camera ViewCamera;
        void Update()
        {
            bool castRay = false;
            Vector3 mousePosition = Vector3.zero;
            # if !ENABLE_INPUT_SYSTEM
                castRay = Input.GetMouseButtonDown(0);
                mousePosition = Input.mousePosition;
            #else
                castRay = UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame;
                mousePosition = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
            #endif

            if (castRay)
            {
                Debug.Log("Casting ray");
                var ray = this.ViewCamera.ScreenPointToRay(mousePosition); // This is the important part. Use the view camera to cast them.
                if (Physics.Raycast(ray, out var hit))
                {
                    Debug.Log("Ray hit " + hit.transform.name);
                    Debug.DrawRay(ray.origin, ray.direction * 100000000, Color.blue, 3);
                    Debug.DrawLine(hit.point, hit.point + Vector3.up, Color.red, 1f);
                }
            }
        }
    }
}