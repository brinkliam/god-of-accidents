using UnityEngine;

namespace GOA.Game
{
    public static class GameSettings
    {
        static float F(string k, float d) => PlayerPrefs.GetFloat("goa_" + k, d);
        static void SetF(string k, float v) { PlayerPrefs.SetFloat("goa_" + k, v); PlayerPrefs.Save(); }
        static bool B(string k, bool d) => PlayerPrefs.GetInt("goa_" + k, d ? 1 : 0) == 1;
        static void SetB(string k, bool v) { PlayerPrefs.SetInt("goa_" + k, v ? 1 : 0); PlayerPrefs.Save(); }

        public static float MusicVolume { get => F("music", 0.55f); set => SetF("music", value); }
        public static float SfxVolume { get => F("sfx", 0.8f); set => SetF("sfx", value); }
        public static float AmbienceVolume { get => F("amb", 0.7f); set => SetF("amb", value); }
        /// <summary>0 = low (mobile), 1 = medium, 2 = high.</summary>
        public static int Quality { get => PlayerPrefs.GetInt("goa_quality", Application.isMobilePlatform ? 0 : 2); set { PlayerPrefs.SetInt("goa_quality", value); PlayerPrefs.Save(); } }
        public static bool Cinematics { get => B("cine", true); set => SetB("cine", value); }
        public static bool ReduceMotion { get => B("reducemotion", false); set => SetB("reducemotion", value); }
        public static bool LargeText { get => B("largetext", false); set => SetB("largetext", value); }
        public static bool AnalyticsConsent { get => B("analytics", false); set => SetB("analytics", value); }
        public static bool PersonalizedAds { get => B("personalads", false); set => SetB("personalads", value); }
        public static bool ConsentAsked { get => B("consentasked", false); set => SetB("consentasked", value); }
        public static bool TutorialDone { get => B("tutorial", false); set => SetB("tutorial", value); }

        public static float QualityScale => Quality switch { 0 => 0.45f, 1 => 0.75f, _ => 1f };
    }
}
