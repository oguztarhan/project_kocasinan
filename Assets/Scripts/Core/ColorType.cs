namespace BusJam.Core
{
    /// <summary>
    /// The set of passenger / bus colors used for matching.
    /// <para><b>None</b> is intentionally value 0 so a default-initialized field is an
    /// explicit "unset" rather than an accidental real color.</para>
    /// Add new colors at the end to keep serialized inspector values stable.
    /// </summary>
    public enum ColorType
    {
        None = 0,
        Red,
        Blue,
        Green,
        Yellow,
        Purple,
        Orange,
    }
}
