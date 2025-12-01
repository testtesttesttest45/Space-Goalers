// QuantumMenuUIController.cs
namespace Quantum.Menu
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using UnityEngine;
    using UnityEngine.Serialization;

    /// <summary>
    /// Registers and flips between menu screens; also owns the menu BGM AudioSource.
    /// </summary>
    public class QuantumMenuUIController : QuantumMonoBehaviour
    {
        [InlineHelp, SerializeField] protected QuantumMenuConfig _config;
        [FormerlySerializedAs("_connection"), InlineHelp, SerializeField] public QuantumMenuConnectionBehaviour Connection;
        [InlineHelp, SerializeField] protected QuantumMenuUIScreen[] _screens;

        protected Dictionary<Type, QuantumMenuUIScreen> _screenLookup;
        protected QuantumMenuUIPopup _popupHandler;
        protected QuantumMenuUIScreen _activeScreen;

        [InlineHelp] public QuantumMenuConnectArgs ConnectArgs;

        // ───────────────────────── MENU MUSIC ─────────────────────────
        [Header("Menu Music")]
        [SerializeField] private AudioSource _musicSource;
        [SerializeField] private AudioClip _menuLoop;
        [SerializeField, Range(0f, 1f)] private float _menuVolume = 0.45f;
        [SerializeField, Range(0.05f, 2f)] private float _fadeSeconds = 0.25f;
        private Coroutine _musicFadeCo;

        private float _baseMenuVolume => _menuVolume * MenuAudioBus.BGMVolume;

        protected virtual void Awake()
        {
            _screenLookup = new Dictionary<Type, QuantumMenuUIScreen>();
            foreach (var screen in _screens)
            {
                screen.Config = _config;
                screen.Config.Init();
                screen.Connection = Connection;
                screen.ConnectionArgs = ConnectArgs;
                screen.Controller = this;

                var t = screen.GetType();
                while (true)
                {
                    _screenLookup.Add(t, screen);
                    if (t.BaseType == null || typeof(QuantumMenuUIScreen).IsAssignableFrom(t) == false || t.BaseType == typeof(QuantumMenuUIScreen))
                    {
                        break;
                    }
                    t = t.BaseType;
                }

                if (screen is QuantumMenuUIPopup popupHandler)
                    _popupHandler = popupHandler;
            }

            foreach (var screen in _screens)
                screen.Init();

            if (_musicSource == null)
            {
                _musicSource = gameObject.GetComponent<AudioSource>();
                if (_musicSource == null) _musicSource = gameObject.AddComponent<AudioSource>();
            }
            _musicSource.playOnAwake = false;
            _musicSource.loop = true;
            _musicSource.spatialBlend = 0f;
            _musicSource.ignoreListenerPause = true;  // unaffected by timescale
            _musicSource.volume = 0f;

            MenuAudioBus.EnsureInit();
        }

        private void OnEnable()
        {
            MenuAudioBus.OnBGMVolumeChanged += HandleBGMChanged;
        }

        private void OnDisable()
        {
            MenuAudioBus.OnBGMVolumeChanged -= HandleBGMChanged;
        }

        protected virtual void Start()
        {
            if (_screens != null && _screens.Length > 0)
            {
                _screens[0].Show();
                _activeScreen = _screens[0];
                UpdateMenuMusicForScreen(_activeScreen, immediate: true);
            }
        }

        public virtual void Show<S>() where S : QuantumMenuUIScreen
        {
            if (_screenLookup.TryGetValue(typeof(S), out var result))
            {
                if (result.IsModal == false && _activeScreen != result && _activeScreen)
                    _activeScreen.Hide();

                if (_activeScreen != result)
                    result.Show();

                if (result.IsModal == false)
                {
                    _activeScreen = result;
                    UpdateMenuMusicForScreen(_activeScreen);
                }
            }
            else
            {
                Debug.LogError($"Show() - Screen type '{typeof(S).Name}' not found");
            }
        }

        public virtual S Get<S>() where S : QuantumMenuUIScreen
        {
            if (_screenLookup.TryGetValue(typeof(S), out var result))
                return result as S;

            Debug.LogError($"Show() - Screen type '{typeof(S).Name}' not found");
            return null;
        }

        public void Popup(string msg, string header = default)
        {
            if (_popupHandler == null)
                Debug.LogError("Popup() - no popup handler found");
            else
                _popupHandler.OpenPopup(msg, header);
        }

        public Task PopupAsync(string msg, string header = default)
        {
            if (_popupHandler == null)
            {
                Debug.LogError("Popup() - no popup handler found");
                return Task.CompletedTask;
            }
            return _popupHandler.OpenPopupAsync(msg, header);
        }

        public virtual async Task HandleConnectionResult(ConnectResult result, QuantumMenuUIController controller)
        {
            if (result.CustomResultHandling) return;

            if (result.Success)
            {
                controller.Show<QuantumMenuUIGameplay>();
            }
            else if (result.FailReason != ConnectFailReason.ApplicationQuit)
            {
                var popup = controller.PopupAsync(result.DebugMessage, "Connection Failed");
                if (result.WaitForCleanup != null)
                    await Task.WhenAll(result.WaitForCleanup, popup);
                else
                    await popup;

                controller.Show<QuantumMenuUIParty>();
            }
        }

        void UpdateMenuMusicForScreen(QuantumMenuUIScreen screen, bool immediate = false)
        {
            bool isGameplay = screen is QuantumMenuUIGameplay;
            if (!isGameplay) StartMenuLoop(immediate);
            else StopMenuLoop(immediate);
        }

        void StartMenuLoop(bool immediate)
        {
            if (_musicSource == null || _menuLoop == null) return;

            if (_musicSource.clip != _menuLoop)
                _musicSource.clip = _menuLoop;

            if (!_musicSource.isPlaying)
                _musicSource.Play();

            FadeMusicTo(_baseMenuVolume, immediate ? 0f : _fadeSeconds);
        }

        void StopMenuLoop(bool immediate)
        {
            if (_musicSource == null) return;
            if (!_musicSource.isPlaying && _musicSource.volume <= 0f) return;

            FadeMusicTo(0f, immediate ? 0f : _fadeSeconds, stopAfter: true);
        }

        void FadeMusicTo(float target, float seconds, bool stopAfter = false)
        {
            if (_musicFadeCo != null) StopCoroutine(_musicFadeCo);
            _musicFadeCo = StartCoroutine(FadeCo(target, Mathf.Max(0f, seconds), stopAfter));
        }

        System.Collections.IEnumerator FadeCo(float target, float seconds, bool stopAfter)
        {
            float start = _musicSource.volume;
            if (seconds <= 0f)
            {
                _musicSource.volume = target;
                if (stopAfter && target <= 0f) _musicSource.Stop();
                yield break;
            }

            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                _musicSource.volume = Mathf.Lerp(start, target, t / seconds);
                yield return null;
            }
            _musicSource.volume = target;
            if (stopAfter && target <= 0f) _musicSource.Stop();
        }

        void HandleBGMChanged(float v)
        {
            if (_musicSource) _musicSource.volume = _menuVolume * v;
        }
    }
}
