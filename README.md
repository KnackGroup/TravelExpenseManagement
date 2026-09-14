# Travel & Expense — Frontend

React + TypeScript + Vite single-page app. See the top-level `README.md` (one directory up) for
the full system overview, how to run everything together with Docker Compose, and demo logins.

## Local dev (against an API running elsewhere)

```bash
cp .env.example .env   # set VITE_API_BASE_URL to wherever the API is running
npm install
npm run dev
```

## Build

```bash
npm run build   # tsc -b && vite build -> dist/
```
