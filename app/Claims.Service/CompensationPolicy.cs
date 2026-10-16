namespace Claims.Service;

public enum Rounding { Down, Up, Nearest }

public static class CompensationPolicy
{
    public static long Calculate(long fareRappen, int delayMinutes, Rounding rounding)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fareRappen);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fareRappen, 1_000_000_000);
        ArgumentOutOfRangeException.ThrowIfNegative(delayMinutes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(delayMinutes, 1_000_000);
        if (!Enum.IsDefined(rounding))
            throw new ArgumentOutOfRangeException(nameof(rounding));

        var percent = delayMinutes >= 120 ? 50 : delayMinutes >= 60 ? 25 : 0;
        var numerator = checked(fareRappen * percent);
        return rounding switch
        {
            Rounding.Down => numerator / 100,
            Rounding.Up => (numerator + 99) / 100,
            Rounding.Nearest => (numerator + 50) / 100,
            _ => throw new ArgumentOutOfRangeException(nameof(rounding))
        };
    }
}
