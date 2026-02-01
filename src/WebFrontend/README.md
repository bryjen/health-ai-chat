## Tailwind CSS integration

This frontend uses **Tailwind CSS v4** with the `tailwind-dotnet` integration (`Tailwind.Hosting` and `Tailwind.Hosting.Build`).

- **Input CSS**: `wwwroot/css/input.css` (contains `@import "tailwindcss";`, `@theme`, and `@layer` definitions).
- **Output CSS**: `wwwroot/css/app.css` (referenced from `wwwroot/index.html`).
- **Configuration**: `tailwind.config.js`.

Tailwind is compiled automatically:

- On **build/publish** via `Tailwind.Hosting.Build`.
- During **`dotnet watch` / Hot Reload**, using `Tailwind.Hosting` enabled in `Properties/launchSettings.json` (`ASPNETCORE_HOSTINGSTARTUPASSEMBLIES=Tailwind.Hosting`).

When extracting shared UI into a Razor Class Library (RCL), keep Tailwind compilation centralized in this project and ensure the Tailwind config (or CSS `@source` directives) includes the RCL's Razor files so that shared components are styled correctly.

