/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./**/*.razor",
    "./Layout/**/*.razor",
    "./Pages/**/*.razor",
    "./wwwroot/**/*.html",
    "./wwwroot/**/*.js",
    "./App.razor"
  ],
  theme: {
    extend: {
      colors: {
        // All colors now reference CSS variables from @theme layer
        // These are automatically available via Tailwind's theme system
        // No hardcoded values - single source of truth in input.css
      },
      fontFamily: {
        "display": ["Inter", "sans-serif"]
      },
      borderRadius: {
        "DEFAULT": "0.25rem",
        "lg": "0.5rem",
        "xl": "0.75rem",
        "full": "9999px"
      },
    },
  },
}
