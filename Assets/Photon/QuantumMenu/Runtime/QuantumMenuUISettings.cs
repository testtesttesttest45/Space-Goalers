// QuantumMenuUISettings.cs
namespace Quantum.Menu
{
    using System;
    using System.Collections.Generic;
#if QUANTUM_ENABLE_TEXTMESHPRO
    using Dropdown = TMPro.TMP_Dropdown;
    using InputField = TMPro.TMP_InputField;
    using Text = TMPro.TMP_Text;
#else
    using Dropdown = UnityEngine.UI.Dropdown;
    using InputField = UnityEngine.UI.InputField;
    using Text = UnityEngine.UI.Text;
#endif
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// Settings screen. All legacy graphics/network UI kept but hidden/locked.
    /// New: BGM & SFX sliders that only affect menu audio.
    /// </summary>
    public partial class QuantumMenuUISettings : QuantumMenuUIScreen
    {
        [InlineHelp, SerializeField] protected Dropdown _uiAppVersion;
        [InlineHelp, SerializeField] protected GameObject _goAppVersion;
        [InlineHelp, SerializeField] protected Toggle _uiFullscreen;
        [InlineHelp, SerializeField] protected GameObject _goFullscreenn;
        [InlineHelp, SerializeField] protected Dropdown _uiFramerate;
        [InlineHelp, SerializeField] protected Dropdown _uiGraphicsQuality;
        [InlineHelp, SerializeField] protected InputField _uiMaxPlayers;
        [InlineHelp, SerializeField] protected Dropdown _uiRegion;
        [InlineHelp, SerializeField] protected GameObject _goRegion;
        [InlineHelp, SerializeField] protected Dropdown _uiResolution;
        [InlineHelp, SerializeField] protected GameObject _goResolution;
        [InlineHelp, SerializeField] protected Toggle _uiVSyncCount;
        [InlineHelp, SerializeField] protected Button _backButton;
        [InlineHelp, SerializeField] protected Text _sdkLabel;

        [Header("Menu Audio (BGM/SFX)")]
        [SerializeField] private Slider _bgmSlider;
        [SerializeField] private Slider _sfxSlider;

        [Header("Locked Defaults (hidden from players)")]
        [SerializeField] private string _defaultRegion = "asia";
        [SerializeField] private bool _defaultVSync = false;
        [SerializeField] private int _defaultTargetFps = 60;
        [SerializeField] private string _defaultQualityName = "Low Fidelity";
        [SerializeField] private bool _lockAndHide = true;

        protected QuantumMenuSettingsEntry<string> _entryRegion;
        protected QuantumMenuSettingsEntry<string> _entryAppVersion;
        protected QuantumMenuSettingsEntry<int> _entryFramerate;
        protected QuantumMenuSettingsEntry<int> _entryResolution;
        protected QuantumMenuSettingsEntry<int> _entryGraphicsQuality;
        protected QuantumMenuGraphicsSettings _graphicsSettings;
        protected List<string> _appVersions;

        partial void AwakeUser();
        partial void InitUser();
        partial void ShowUser();
        partial void HideUser();
        partial void SaveChangesUser();

        public override void Awake()
        {
            base.Awake();

            _appVersions = new List<string>();
            if (Config.MachineId != null) _appVersions.Add(Config.MachineId);
            _appVersions.AddRange(Config.AvailableAppVersions);

            _entryRegion = new QuantumMenuSettingsEntry<string>(_uiRegion, SaveChanges);
            _entryAppVersion = new QuantumMenuSettingsEntry<string>(_uiAppVersion, SaveChanges);
            _entryFramerate = new QuantumMenuSettingsEntry<int>(_uiFramerate, SaveChanges);
            _entryResolution = new QuantumMenuSettingsEntry<int>(_uiResolution, SaveChanges);
            _entryGraphicsQuality = new QuantumMenuSettingsEntry<int>(_uiGraphicsQuality, SaveChanges);

            _uiMaxPlayers.onEndEdit.AddListener(s => {
                if (Int32.TryParse(s, out var maxPlayers) == false || maxPlayers <= 0 || maxPlayers > Config.MaxPlayerCount)
                {
                    maxPlayers = Math.Clamp(maxPlayers, 1, Config.MaxPlayerCount);
                    _uiMaxPlayers.text = maxPlayers.ToString();
                }
                SaveChanges();
            });

            _uiVSyncCount.onValueChanged.AddListener(_ => SaveChanges());
            _uiFullscreen.onValueChanged.AddListener(_ => SaveChanges());

            _graphicsSettings = new QuantumMenuGraphicsSettings();
#if UNITY_IOS || UNITY_ANDROID
            PlayerPrefs.SetInt("Photon.Menu.Framerate", 60);
            PlayerPrefs.SetInt("Photon.Menu.VSync", 0);
            PlayerPrefs.Save();
#endif

#if UNITY_IOS || UNITY_ANDROID
            _goResolution.SetActive(false);
            _goFullscreenn.SetActive(false);
#endif

            if (_lockAndHide)
            {
                if (_goRegion) _goRegion.SetActive(false);
                if (_goAppVersion) _goAppVersion.SetActive(false);
                if (_uiFramerate) _uiFramerate.gameObject.SetActive(false);
                if (_uiGraphicsQuality) _uiGraphicsQuality.gameObject.SetActive(false);
                if (_uiVSyncCount) _uiVSyncCount.gameObject.SetActive(false);
                ApplyLockedDefaults();
            }
            else
            {
                if (_goAppVersion) _goAppVersion.SetActive(Config.AvailableAppVersions.Count > 0);
                if (_goRegion) _goRegion.SetActive(Config.AvailableRegions.Count > 0);
            }

            AwakeUser();
        }

        public override void Init()
        {
            base.Init();
            InitUser();
        }

        public override void Show()
        {
            base.Show();

            if (!_lockAndHide)
            {
                _entryRegion.SetOptions(Config.AvailableRegions, ConnectionArgs.PreferredRegion, s => string.IsNullOrEmpty(s) ? "Best" : s);
                _entryAppVersion.SetOptions(_appVersions, ConnectionArgs.AppVersion, s => s.Equals(Config.MachineId) ? $"Build ({Config.MachineId})" : s);
                _entryFramerate.SetOptions(_graphicsSettings.CreateFramerateOptions, _graphicsSettings.Framerate, s => (s == -1 ? "Platform Default" : s.ToString()));
                _entryResolution.SetOptions(_graphicsSettings.CreateResolutionOptions, _graphicsSettings.Resolution, s =>
#if UNITY_2022_2_OR_NEWER
                  $"{Screen.resolutions[s].width} x {Screen.resolutions[s].height} @ {Mathf.RoundToInt((float)Screen.resolutions[s].refreshRateRatio.value)}");
#else
                  Screen.resolutions[s].ToString());
#endif
                _entryGraphicsQuality.SetOptions(_graphicsSettings.CreateGraphicsQualityOptions, _graphicsSettings.QualityLevel, s => QualitySettings.names[s]);
                _uiFullscreen.isOn = _graphicsSettings.Fullscreen;
                _uiVSyncCount.isOn = _graphicsSettings.VSync;
            }
            else
            {
                _uiFullscreen.isOn = _graphicsSettings.Fullscreen;
            }

            _uiMaxPlayers.SetTextWithoutNotify(Math.Clamp(ConnectionArgs.MaxPlayerCount, 1, Config.MaxPlayerCount).ToString());

            MenuAudioBus.EnsureInit();

            if (_bgmSlider)
            {
                _bgmSlider.minValue = 0f;
                _bgmSlider.maxValue = 1f;
                _bgmSlider.wholeNumbers = false;
                _bgmSlider.SetValueWithoutNotify(MenuAudioBus.BGMVolume);
                _bgmSlider.onValueChanged.RemoveListener(OnBGMChanged);
                _bgmSlider.onValueChanged.AddListener(OnBGMChanged);
            }

            if (_sfxSlider)
            {
                _sfxSlider.minValue = 0f;
                _sfxSlider.maxValue = 1f;
                _sfxSlider.wholeNumbers = false;
                _sfxSlider.SetValueWithoutNotify(MenuAudioBus.SFXVolume);
                _sfxSlider.onValueChanged.RemoveListener(OnSFXChanged);
                _sfxSlider.onValueChanged.AddListener(OnSFXChanged);
            }

            ShowUser();
        }

        public override void Hide()
        {
            base.Hide();
            if (_bgmSlider) _bgmSlider.onValueChanged.RemoveListener(OnBGMChanged);
            if (_sfxSlider) _sfxSlider.onValueChanged.RemoveListener(OnSFXChanged);
            HideUser();
        }

        protected virtual void SaveChanges()
        {
            if (IsShowing == false) return;

            if (Int32.TryParse(_uiMaxPlayers.text, out var maxPlayers))
            {
                ConnectionArgs.MaxPlayerCount = Math.Clamp(maxPlayers, 1, Config.MaxPlayerCount);
                _uiMaxPlayers.SetTextWithoutNotify(ConnectionArgs.MaxPlayerCount.ToString());
            }

            if (_lockAndHide)
            {
                ApplyLockedDefaults();
            }
            else
            {
                ConnectionArgs.PreferredRegion = _entryRegion.Value;
                ConnectionArgs.AppVersion = _entryAppVersion.Value;

                _graphicsSettings.Fullscreen = _uiFullscreen.isOn;
                _graphicsSettings.Framerate = _entryFramerate.Value;
                _graphicsSettings.Resolution = _entryResolution.Value;
                _graphicsSettings.QualityLevel = _entryGraphicsQuality.Value;
                _graphicsSettings.VSync = _uiVSyncCount.isOn;
                _graphicsSettings.Apply();

                ConnectionArgs.SaveToPlayerPrefs();
            }

            SaveChangesUser();
        }

        public virtual void OnBackButtonPressed()
        {
            Controller.Show<QuantumMenuUIMain>();
        }

        void ApplyLockedDefaults()
        {
            if (!string.IsNullOrEmpty(Config.MachineId))
                ConnectionArgs.AppVersion = Config.MachineId;
            else if (Config.AvailableAppVersions != null && Config.AvailableAppVersions.Count > 0)
                ConnectionArgs.AppVersion = Config.AvailableAppVersions[Config.AvailableAppVersions.Count - 1];
            else
                ConnectionArgs.AppVersion = Application.version;

            ConnectionArgs.PreferredRegion = string.IsNullOrEmpty(_defaultRegion) ? "asia" : _defaultRegion;

            QualitySettings.vSyncCount = _defaultVSync ? 1 : 0;
            Application.targetFrameRate = _defaultTargetFps;

            int qualityIndex = 0;
            var names = QualitySettings.names;
            if (names != null && names.Length > 0)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    if (string.Equals(names[i], _defaultQualityName, StringComparison.OrdinalIgnoreCase))
                    { qualityIndex = i; break; }
                }
            }
            _graphicsSettings = _graphicsSettings ?? new QuantumMenuGraphicsSettings();
            _graphicsSettings.Fullscreen = false;
            _graphicsSettings.Framerate = _defaultTargetFps;
            _graphicsSettings.Resolution = 0;
            _graphicsSettings.QualityLevel = qualityIndex;
            _graphicsSettings.VSync = _defaultVSync;
            _graphicsSettings.Apply();

            ConnectionArgs.SaveToPlayerPrefs();
        }

        void OnBGMChanged(float v) => MenuAudioBus.BGMVolume = v;
        void OnSFXChanged(float v) => MenuAudioBus.SFXVolume = v;
    }
}
