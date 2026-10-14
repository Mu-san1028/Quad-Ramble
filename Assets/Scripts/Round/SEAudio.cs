using System;
using UnityEngine;

public enum SEType
{
    Confirm,
    Cancel,
    Jump,
    Attack,
    Explosion,
    Countdown,
    RoundStart
}

public class SEAudio : MonoBehaviour
{
    public static SEAudio Instance { get; private set; }

    [Serializable]
    private class SEEntry
    {
        public SEType type;
        public AudioClip clip;

        [Range(0f, 1f)]
        public float volume = 1f;
    }

    [Header("SE一覧")]
    [SerializeField] private SEEntry[] entries = new SEEntry[0];

    [Header("再生用AudioSource")]
    [SerializeField] private AudioSource source;

    private bool seEnabled;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this.gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(this.gameObject);

        this.seEnabled = ReadSEEnabled();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void Play(SEType type)
    {
        if (!this.seEnabled || this.source == null)
        {
            return;
        }

        foreach (var entry in this.entries)
        {
            if (entry == null || entry.type != type)
            {
                continue;
            }

            if (entry.clip != null)
            {
                this.source.PlayOneShot(entry.clip, entry.volume);
            }

            return;
        }
    }

    private static bool ReadSEEnabled()
    {
        string[] args = Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(
                args[i],
                "-se",
                StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(
                args[i + 1],
                "off",
                StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(
                args[i + 1],
                "on",
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return true;
    }
}
