namespace BusJam
{
    /// <summary>
    /// This app's Google Play LICENSE (public) key, used by <see cref="IAPManager"/>'s receipt validator to confirm a
    /// purchase was really signed by Google (anti-fraud). It is a PUBLIC key — safe to ship inside the app; it can only
    /// VERIFY receipts, never sign them. Pasted from Play Console ▸ Monetize ▸ Monetization setup ▸ Licensing.
    ///
    /// (For stronger tamper-resistance you can later regenerate this via Unity's Receipt Validation Obfuscator — the
    /// validator only needs <see cref="Data"/> to return these raw key bytes, however they're produced.)
    /// </summary>
    public static class GooglePlayKey
    {
        const string Base64 =
            "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAtYRN41/cch6Kv+joGh5a0T8jj0W+sfwW1BX56h4uxudhCnl6CfoYbdC8INruWF0l79+/QPFVxWcpFUrmxY65hYHgY+//Cm7MXCaPI/WJWTO9hukHnCAHfhT+pTriwXk1+tkzKYuwoZ01DefbmW1jKQ1S4xITHQeeWUmpKiLpu1EWUmfEaWPJgAOVuJx39ycxEFXG0g6q8Sb9DixlXc7aRIyR6Wwb3T8kJ0iAm21k3ECEB0intuUQVgvQjk1r+m+2APSK6bAOnmmNOp1ZhbMpXjzza+jcaMLB2EYEAdmE7Vf0845RhrZloJwQJEfHM4KgLAF2yltbgxw7+hj1jpJ3ewIDAQAB";

        /// <summary>Raw DER bytes of the public key, for CrossPlatformValidator's Google Play check.</summary>
        public static byte[] Data() => System.Convert.FromBase64String(Base64);
    }
}
