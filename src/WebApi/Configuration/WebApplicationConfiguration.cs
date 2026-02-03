using System.Diagnostics.CodeAnalysis;
using Scalar.AspNetCore;

namespace WebApi.Configuration;

public static class WebApplicationConfiguration
{
    [StringSyntax("css")]
    private const string ScalarCustomCss =
"""
@import url('https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap');

:root {
    --scalar-font: 'Inter', 'Helvetica Neue', Helvetica, Arial, sans-serif;
    --scalar-font-code: 'Inter', 'Courier New', monospace;

    /* Text colors - matching frontend foreground (oklch(0.985 0 0)) */
    --scalar-color-1: #fafafa;
    --scalar-color-2: #d4d4d4;
    --scalar-color-3: #a3a3a3;

    /* Accent color - matching frontend blue (#5288ed) */
    --scalar-color-accent: #5288ed;

    /* Backgrounds - matching frontend dark theme */
    /* Background: oklch(0.141 0.005 285.823) ≈ #1a1a1a */
    --scalar-background-1: #0f0f0f;
    --scalar-background-2: #1a1a1a;
    /* Card: oklch(0.21 0.006 285.885) ≈ #2a2a2a */
    --scalar-background-3: #2a2a2a;

    /* Borders - matching frontend border (rgba white 0.1) */
    --scalar-border-color: rgba(255, 255, 255, 0.1);

    /* Button/input backgrounds */
    --scalar-button-1: #2a2a2a;
    --scalar-button-1-hover: #353535;
    --scalar-button-1-color: #fafafa;

    /* Code blocks */
    --scalar-code-background: #1a1a1a;
    --scalar-code-color: #fafafa;

    /* Muted/secondary colors */
    --scalar-color-muted: #404040;
    --scalar-color-muted-foreground: #a3a3a3;
}
""";

    public static WebApplication ConfigureScalarDocs(this WebApplication app)
    {
        app.MapScalarApiReference(options =>
        {
            options.WithTheme(ScalarTheme.None);
            options.WithOpenApiRoutePattern("/swagger/v1/swagger.json");
            options.WithTitle("API Documentation");
            options.DisableDefaultFonts();
            options.WithCustomCss(ScalarCustomCss);

            options.DotNetFlag = true;
            options.HideClientButton = true;
        });

        app.MapGet("/", () => Results.Redirect("/scalar", permanent: false))  // root redirect to Scalar UI
            .ExcludeFromDescription()
            .WithName("RootRedirect")
            .WithTags("Redirect");

        return app;
    }
}
