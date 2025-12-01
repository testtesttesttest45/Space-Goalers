using Quantum.Menu;
using UnityEngine;
using UnityEngine.UI;

public class UIButtonClickSFX : MonoBehaviour
{
    [Header("Click SFX")]
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 1f;

    static AudioSource _bus;

    void Awake()
    {
        var btn = GetComponent<Button>();
        if (!btn) { Debug.LogWarning($"[UIButtonClickSFX] No Button on {name}"); return; }
        btn.onClick.AddListener(Play);
    }

    public void Play()
    {
        if (!clip) return;
        EnsureBus();
        MenuAudioBus.EnsureInit();
        var v = volume * MenuAudioBus.SFXVolume;
        if (v <= 0f) return;
        _bus.PlayOneShot(clip, v);
    }

    static void EnsureBus()
    {
        if (_bus) return;
        var go = new GameObject("[UI SFX Bus]");
        Object.DontDestroyOnLoad(go);
        _bus = go.AddComponent<AudioSource>();
        _bus.playOnAwake = false;
        _bus.loop = false;
        _bus.spatialBlend = 0f;
        _bus.ignoreListenerPause = true;
        _bus.volume = 1f; // final loudness comes from PlayOneShot(volume)
    }

    void OnDestroy()
    {
        var btn = GetComponent<Button>();
        if (btn) btn.onClick.RemoveListener(Play);
    }
}
