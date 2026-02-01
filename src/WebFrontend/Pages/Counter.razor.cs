using Microsoft.AspNetCore.Components;

namespace WebFrontend.Pages;

public partial class Counter : ComponentBase
{
    protected int CurrentCount { get; set; } = 0;

    protected void IncrementCount()
    {
        CurrentCount++;
    }
}
