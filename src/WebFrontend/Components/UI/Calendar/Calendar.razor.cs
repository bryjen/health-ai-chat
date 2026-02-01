using Microsoft.AspNetCore.Components;
using WebFrontend.Utils;

namespace WebFrontend.Components.UI.Calendar;

public enum CalendarSelectableRange
{
    AllDates,
    PreviousDates,
    PreviousDatesToday,
    FutureDates,
    FutureDatesToday
}

[ComponentMetadata(
    Description = "Custom dropdown-style date picker with calendar grid.",
    IsEntry = true,
    Group = nameof(Calendar))]
public partial class Calendar
{
    [Parameter] public CalendarSelectableRange SelectableRange { get; set; } = CalendarSelectableRange.AllDates;
}

