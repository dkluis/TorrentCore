using System.Globalization;

namespace TorrentCore.WebUI2.Client.Services;

public static class OperatorDateTime
{
    public static string Local(DateTimeOffset? value) => value?.ToLocalTime().ToString("yyyy/MM/dd " + CultureInfo.CurrentCulture.DateTimeFormat.LongTimePattern, CultureInfo.CurrentCulture) ?? "";
    public static string Local(DateTime? value) => value is null ? "" : Local(new DateTimeOffset(value.Value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : value.Value));
}
