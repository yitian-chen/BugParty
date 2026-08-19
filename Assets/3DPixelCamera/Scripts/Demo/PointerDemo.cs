namespace PixelCamera
{
    using UnityEngine;

    /// <summary>
    /// Example on how to use mouse events with the camera system.
    /// </summary>
    public class PointerDemo : MonoBehaviour
    {
        // https://docs.unity3d.com/ScriptReference/MonoBehaviour.OnMouseEnter.html
        Renderer rend;

        void Start()
        {
            this.rend = this.GetComponent<Renderer>();
        }

        void OnMouseEnter()
        {
            this.rend.material.color = Color.red;
        }

        void OnMouseOver()
        {
            this.rend.material.color -= new Color(0.1F, 0, 0) * Time.deltaTime;
        }

        void OnMouseExit()
        {
            this.rend.material.color = Color.white;
        }
    }
}