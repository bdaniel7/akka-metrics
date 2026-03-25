/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./src/**/*.{html,ts}",
  ],
  theme: {
    extend: {
      fontFamily: {
        'mono': ['JetBrains Mono', 'Fira Code', 'monospace'],
        'display': ['Space Grotesk', 'sans-serif'],
      },
      colors: {
        'void': '#0f1117',
        'panel': '#1a1f2e',
        'surface': '#222840',
        'border': '#2e3650',
        'accent': '#00ff88',
        'accent-dim': '#00cc6a',
        'warn': '#ff6b35',
        'danger': '#ff4444',
        'cpu': '#a78bfa',
        'ram': '#38bdf8',
        'muted': '#94a3b8',
        'text': '#f0f4ff',
      },
      animation: {
        'pulse-slow': 'pulse 3s cubic-bezier(0.4, 0, 0.6, 1) infinite',
        'scan': 'scan 2s linear infinite',
        'glow': 'glow 2s ease-in-out infinite alternate',
      },
      keyframes: {
        scan: {
          '0%': { transform: 'translateY(-100%)' },
          '100%': { transform: 'translateY(100vh)' },
        },
        glow: {
          '0%': { boxShadow: '0 0 5px #00ff8840' },
          '100%': { boxShadow: '0 0 20px #00ff8880, 0 0 40px #00ff8840' },
        }
      },
      backgroundImage: {
        'grid': "url(\"data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='32' height='32'%3E%3Cpath d='M0 0h32v32H0z' fill='none'/%3E%3Cpath d='M0 0v32M32 0v32M0 0h32M0 32h32' stroke='%2321262d' stroke-width='0.5' opacity='0.5'/%3E%3C/svg%3E\")",
      }
    },
  },
  plugins: [],
}
