// MenuAudioBus.cs  (Runtime, NOT under an Editor folder)
using UnityEngine;
using System;

namespace Quantum.Menu
{
    public static class MenuAudioBus
    {
        const string PP_BGM = "Menu.BGM.Volume";
        const string PP_SFX = "Menu.SFX.Volume";

        static bool _inited;
        static float _bgm = 1f;
        static float _sfx = 1f;

        public static event Action<float> OnBGMVolumeChanged;
        public static event Action<float> OnSFXVolumeChanged;

        public static void EnsureInit()
        {
            if (_inited) return;
            _bgm = Mathf.Clamp01(PlayerPrefs.GetFloat(PP_BGM, 0.3f));
            _sfx = Mathf.Clamp01(PlayerPrefs.GetFloat(PP_SFX, 1f));
            _inited = true;
        }

        public static float BGMVolume
        {
            get { EnsureInit(); return _bgm; }
            set
            {
                EnsureInit();
                value = Mathf.Clamp01(value);
                if (!Mathf.Approximately(value, _bgm))
                {
                    _bgm = value;
                    PlayerPrefs.SetFloat(PP_BGM, _bgm);
                    PlayerPrefs.Save();
                    OnBGMVolumeChanged?.Invoke(_bgm);
                }
            }
        }

        public static float SFXVolume
        {
            get { EnsureInit(); return _sfx; }
            set
            {
                EnsureInit();
                value = Mathf.Clamp01(value);
                if (!Mathf.Approximately(value, _sfx))
                {
                    _sfx = value;
                    PlayerPrefs.SetFloat(PP_SFX, _sfx);
                    PlayerPrefs.Save();
                    OnSFXVolumeChanged?.Invoke(_sfx);
                }
            }
        }
    }
}
