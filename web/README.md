# Meridian web app

Angular app for the Meridian Vote Tracker. See the [main README](../README.md) for how to run the whole system.

```
npm install
npm run generate:api   # regenerates src/app/core/api from contracts/openapi.json; never edit that folder
npx ng serve           # http://localhost:4200, proxies /api to the API on port 5284
npm test               # unit tests for the shared helpers
npm run build
```
