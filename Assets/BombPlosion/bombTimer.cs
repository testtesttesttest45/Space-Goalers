using System.Collections;
using UnityEngine;

public class bombTimer : MonoBehaviour
{
    [Header("VFX / SFX")]
    [SerializeField] private ParticleSystem SparksVfx;
    [SerializeField] private Transform detonationCord;
    [SerializeField] private GameObject Xplosion;
    [SerializeField] private AudioSource audio2D;
    [SerializeField] private AudioClip fuseLoop;
    [SerializeField] private AudioClip explodeClip;

    [Header("Cord Settings")]
    [SerializeField] private float airborneCordJitter = 0.01f; // tiny wiggle while flying
    [SerializeField] private float armedCordPullPerSec = 0.06f;
    [SerializeField] private float cordMaxPull = 0.20f;
    [SerializeField] private bool cordUseLocal = true;

    Coroutine _fuseCo;
    bool _visualOn;
    bool _armed;
    bool _exploded;

    Vector3 _cordStartLocal, _cordStartWorld;
    float _cordPulled;

    void Awake()
    {
        if (detonationCord)
        {
            _cordStartLocal = detonationCord.localPosition;
            _cordStartWorld = detonationCord.position;
        }
    }

    public void BeginFuseVisual()
    {
        if (_exploded) return;
        if (!_visualOn)
        {
            if (SparksVfx && !SparksVfx.isPlaying) SparksVfx.Play(true);
            if (audio2D && fuseLoop && !audio2D.isPlaying)
            {
                audio2D.loop = true;
                audio2D.clip = fuseLoop;
                audio2D.Play();
            }
            ResetCord();
            _visualOn = true;
        }
    }

    public void ArmFuse(float seconds)
    {
        if (_exploded) return;
        if (_armed) { StopCountdownOnly(); }
        _armed = true;
        _fuseCo = StartCoroutine(FuseAfter(seconds));
    }

    public void ExplodeNow(Vector3 worldPos)
    {
        if (_exploded) return;
        _exploded = true;
        StopAllFuse();
        if (explodeClip && audio2D) audio2D.PlayOneShot(explodeClip);
        if (Xplosion) Instantiate(Xplosion, worldPos, transform.rotation);
        Destroy(gameObject);
    }

    IEnumerator FuseAfter(float seconds)
    {
        float t = 0f;
        while (t < seconds && !_exploded)
        {
            t += Time.deltaTime;

            StepCord(armedCordPullPerSec * Time.deltaTime, true);

            yield return null;
        }

        if (!_exploded) ExplodeNow(GetCordTip());
    }

    void Update()
    {
        if (_exploded) return;

        if (_visualOn && !_armed)
        {
            StepCord(airborneCordJitter * Time.deltaTime, false);
        }
    }

    void ResetCord()
    {
        if (!detonationCord) return;
        if (cordUseLocal) detonationCord.localPosition = _cordStartLocal;
        else detonationCord.position = _cordStartWorld;
        _cordPulled = 0f;
        var r = detonationCord.GetComponentInChildren<Renderer>(true);
        if (r) r.enabled = true;
    }

    void StepCord(float delta, bool clamp)
    {
        if (!detonationCord || delta <= 0f) return;
        float room = Mathf.Max(0f, cordMaxPull - _cordPulled);
        float pull = clamp ? Mathf.Min(delta, room) : delta;

        if (cordUseLocal)
            detonationCord.localPosition += new Vector3(0f, -pull, 0f);
        else
            detonationCord.position += new Vector3(0f, -pull, 0f);

        _cordPulled = Mathf.Clamp(_cordPulled + pull, 0f, cordMaxPull);
    }

    Vector3 GetCordTip()
    {
        return detonationCord ? (cordUseLocal ? detonationCord.position : detonationCord.position)
                              : transform.position;
    }

    void StopCountdownOnly()
    {
        if (_fuseCo != null) { StopCoroutine(_fuseCo); _fuseCo = null; }
        _armed = false;
    }

    void StopAllFuse()
    {
        StopCountdownOnly();
        if (SparksVfx) SparksVfx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (audio2D && audio2D.loop) { audio2D.Stop(); audio2D.loop = false; audio2D.clip = null; }
        _visualOn = false;
    }
}
