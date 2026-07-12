using UnityEngine;

public class UIManager : MonoBehaviour
{
    public GameObject startUI;
    public GameObject navInformUI;
    public GameObject navInformOKUI;

    void Start()
    {
        startUI.SetActive(true);
        navInformUI.SetActive(false);
        navInformOKUI.SetActive(false);
    }

    public void ToNavInform()
    {
        startUI.SetActive(false);
        navInformUI.SetActive(true);
    }

    public void ToNavInformOK()
    {
        navInformUI.SetActive(false);
        navInformOKUI.SetActive(true);
    }
}