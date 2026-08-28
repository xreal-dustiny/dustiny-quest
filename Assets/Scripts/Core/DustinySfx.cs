using UnityEngine;

public static class DustinySfx
{
    private const string SourceName = "DustinySfxSource";

    private static AudioSource source;
    private static AudioClip yesClip;
    private static AudioClip noClip;
    private static AudioClip coinClip;
    private static AudioClip buyClip;

    public static void PlayYes()
    {
        Play(ref yesClip, "Yes");
    }

    public static void PlayNo()
    {
        Play(ref noClip, "No");
    }

    public static void PlayCoin()
    {
        Play(ref coinClip, "coin");
    }

    public static void PlayBuy()
    {
        Play(ref buyClip, "buy");
    }

    public static void RegisterClips(AudioClip yes, AudioClip no, AudioClip coin, AudioClip buy)
    {
        if (yes != null)
        {
            yesClip = yes;
        }

        if (no != null)
        {
            noClip = no;
        }

        if (coin != null)
        {
            coinClip = coin;
        }

        if (buy != null)
        {
            buyClip = buy;
        }
    }

    private static void Play(ref AudioClip cached, string clipName)
    {
        if (cached == null)
        {
            cached = LoadClip(clipName);
        }

        if (cached == null)
        {
            return;
        }

        AudioSource audioSource = EnsureSource();
        if (audioSource == null)
        {
            return;
        }

        audioSource.PlayOneShot(cached);
    }

    private static AudioClip LoadClip(string clipName)
    {
        AudioClip clip = Resources.Load<AudioClip>("DustinySfx/" + clipName);
        if (clip != null)
        {
            return clip;
        }

#if UNITY_EDITOR
        string[] candidates =
        {
            "Assets/Dustiny/Audio/SFX/" + clipName + ".mp3",
            "Assets/Dustiny/Audio/SFX/" + clipName + ".wav",
            "Assets/Dustiny/Audio/SFX/" + ToTitle(clipName) + ".mp3"
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(candidates[i]);
            if (clip != null)
            {
                return clip;
            }
        }
#endif

        AudioClip[] loaded = Resources.FindObjectsOfTypeAll<AudioClip>();
        for (int i = 0; i < loaded.Length; i++)
        {
            if (loaded[i] != null &&
                string.Equals(loaded[i].name, clipName, System.StringComparison.OrdinalIgnoreCase))
            {
                return loaded[i];
            }
        }

        return null;
    }

    private static string ToTitle(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + value.Substring(1);
    }

    private static AudioSource EnsureSource()
    {
        if (source != null)
        {
            return source;
        }

        GameObject existing = GameObject.Find(SourceName);
        GameObject host = existing != null ? existing : new GameObject(SourceName);
        Object.DontDestroyOnLoad(host);
        source = host.GetComponent<AudioSource>();
        if (source == null)
        {
            source = host.AddComponent<AudioSource>();
        }

        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.loop = false;
        source.volume = 1f;
        return source;
    }
}
