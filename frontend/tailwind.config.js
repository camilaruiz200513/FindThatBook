/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      fontFamily: {
        sans: ['Inter', 'ui-sans-serif', 'system-ui', 'sans-serif'],
      },
      colors: {
        brand: {
          50: '#f4f7ff',
          100: '#e6edff',
          500: '#4c6ef5',
          600: '#4054c9',
          700: '#2f3f9e',
        },
      },
    },
  },
  plugins: [],
};
