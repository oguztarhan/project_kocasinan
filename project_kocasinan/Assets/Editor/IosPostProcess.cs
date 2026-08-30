#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

/// <summary>
/// Writes the App Store export-compliance answer into the generated Xcode project's Info.plist, so it is baked into
/// every build instead of being hand-added after each one (which is how it went missing before).
///
/// <b>ITSAppUsesNonExemptEncryption = false.</b> Without this key App Store Connect stops each upload to ask the
/// export-compliance question by hand; with it the answer travels with the binary. False is the correct answer here
/// because the app uses only the encryption Apple already exempts: HTTPS/TLS via the OS (Firebase, AdMob/UMP and
/// StoreKit all talk over standard TLS). There is no bundled crypto library and nothing implements its own cipher.
/// <b>If that ever changes — a custom cipher, or encrypting user data at rest with your own scheme — this key must be
/// re-evaluated, not just left at false.</b>
///
/// Runs at callbackOrder 999 so it lands after Unity's own plist pass and after GoogleMobileAds' PListProcessor
/// (which uses the default order and writes the AdMob app id / SKAdNetwork items). It only ever adds this one key,
/// so it cannot clobber theirs.
///
/// The whole file is behind #if UNITY_IOS: with any other active build target it compiles out, so UnityEditor.iOS.Xcode
/// is never required on a machine building only for Android.
/// </summary>
public static class IosPostProcess
{
    [PostProcessBuild(999)]
    public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
    {
        if (target != BuildTarget.iOS) return;

        string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
        if (!File.Exists(plistPath))
        {
            Debug.LogWarning("[IosPostProcess] No Info.plist at " + plistPath +
                             " — ITSAppUsesNonExemptEncryption NOT set. App Store Connect will ask for export " +
                             "compliance by hand on this upload.");
            return;
        }

        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);
        plist.root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
        plist.WriteToFile(plistPath);

        Debug.Log("[IosPostProcess] Info.plist: ITSAppUsesNonExemptEncryption = false");
    }
}
#endif
