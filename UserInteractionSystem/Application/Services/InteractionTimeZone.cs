namespace UserInteractionSystem.Application.Services;

public static class InteractionTimeZone
{
    private static readonly TimeZoneInfo MoscowTimeZone = FindMoscowTimeZone();

    private static TimeZoneInfo FindMoscowTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        }
        catch (TimeZoneNotFoundException) when (OperatingSystem.IsWindows())
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
        }
    }

    public static DateTime ToUtc(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
        {
            return value;
        }

        var moscowLocalTime = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(moscowLocalTime, MoscowTimeZone);
    }

    public static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public static DateTime ToMoscowTime(DateTime utcValue) =>
        TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utcValue), MoscowTimeZone);

    public static DateTime GetMoscowNow() => ToMoscowTime(DateTime.UtcNow);
}
