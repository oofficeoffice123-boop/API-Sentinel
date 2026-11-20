# API Sentinel frontend

The frontend is a React and TypeScript single-page app built with Vite. Its reusable workspace panels cover API requests, environments, tests, collections, monitoring, reports, AI assistance, accounts, and administration.

## Run locally

From the repository root, start the API first, then run:

```powershell
npm.cmd install --prefix frontend
npm.cmd run dev --prefix frontend
```

Open `http://localhost:5173`. The API defaults to `http://localhost:5080/api`. To use a different API address, set `VITE_API_URL` before building or running Vite. Keep backend credentials out of all `VITE_*` variables because frontend values are bundled into public assets.

## Build and lint

```powershell
npm.cmd run build --prefix frontend
npm.cmd run lint --prefix frontend
```

The production files are generated in `frontend/dist`. The Docker build serves those files with Nginx and routes API traffic through Caddy.
