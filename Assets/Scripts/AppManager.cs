using UnityEngine;

/// <summary>
/// Keeps the DustinyManager root alive between scenes.
/// Attach this. together with the other data managers to one DustinyManager object.
/// </summary>
public class AppManager : MonoBehaviour
{
    public static AppManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            return;
        }

        if (Instance != this)
        {
            Destroy(gameObject);
        }
    }
}
