using UnityEngine;

public class HeadTracker : MonoBehaviour
{
    public UIManager uiManager;
    public float threshold = 0.5f;

    void Update()
    {
        if (uiManager.navInformUI.activeSelf)
        {
           float dot = Vector3.Dot(transform.forward, Vector3.down);

            if (dot > threshold)
            {
                uiManager.ToNavInformOK();
            }
        }
    }
}