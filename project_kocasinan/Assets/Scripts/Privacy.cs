using UnityEngine;

namespace Ridebury
{
    /// <summary>
    /// The privacy entry points the stores require. There are two, and they are NOT the same thing:
    ///
    /// <list type="bullet">
    /// <item><b>Privacy policy</b> (<see cref="OpenPolicy"/>) — the published document. Google Play and the App Store
    /// both require a link to it from inside the app. Always available, in every region.</item>
    /// <item><b>Ad privacy options</b> (AdManager.ShowPrivacyOptions) — Google's UMP consent form. Required only for
    /// users who were shown the consent form in the first place (EEA/UK), which is why its button hides itself
    /// everywhere else.</item>
    /// </list>
    /// </summary>
    public static class Privacy
    {
        /// <summary>The Ridebury-specific policy (the old /privacy page covers every Intake title, so it is no
        /// longer the right link for this app's store listing or its in-game button).</summary>
        public const string PolicyBaseUrl = "https://intakeentertainment.com/privacy/ridebury/";

        /// <summary>
        /// The URL to open, with the language fragment. The page ships English and Turkish in ONE document and picks
        /// between them from the hash — its script is literally <c>location.hash === '#en' ? 'en' : 'tr'</c>, so
        /// anything that is NOT <c>#en</c> renders Turkish. The fragment is therefore not optional: linking to the
        /// bare URL would show Turkish to every player in the world, App Review included.
        ///
        /// The game has nine languages and the page has two, so Turkish players get <c>#tr</c> and everyone else
        /// falls back to English — the same English-fallback rule <see cref="Loc.T"/> uses for missing strings.
        /// </summary>
        public static string PolicyUrl => PolicyBaseUrl + (Loc.Lang == 0 ? "#tr" : "#en");

        /// <summary>Open the privacy policy in the device browser, in the player's language where we have it.</summary>
        public static void OpenPolicy() => Application.OpenURL(PolicyUrl);
    }
}
