using UnityEngine;

public class PhysicalButton : MonoBehaviour
{
    public UIManager uiManager;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Hand"))
        {
            if (uiManager != null)
            {
                uiManager.ToNavInform();
            }
        }
    }
}