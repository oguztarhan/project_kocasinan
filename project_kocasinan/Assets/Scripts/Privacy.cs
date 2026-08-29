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
        public const string PolicyUrl = "https://intakeentertainment.com/privacy";

        /// <summary>Open the privacy policy in the device browser.</summary>
        public static void OpenPolicy() => Application.OpenURL(PolicyUrl);
    }
}
