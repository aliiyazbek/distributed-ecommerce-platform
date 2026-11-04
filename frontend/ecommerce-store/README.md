# Store frontend

React and Vite app for the e-commerce platform. It talks to the backend through the Vite proxy, so the browser only sees one origin.

## Run it

From the repository root, start the database and apply the migrations once:

```sh
docker compose up -d
dotnet ef database update --project src/Modules/Users --startup-project src/Host --context UsersDbContext
dotnet ef database update --project src/Modules/Products --startup-project src/Host --context ProductsDbContext
dotnet ef database update --project src/Modules/Orders --startup-project src/Host --context OrdersDbContext
```

Start the API:

```sh
dotnet run --project src/Host --launch-profile http
```

Then start the frontend:

```sh
cd frontend/ecommerce-store
npm install
npm run dev
```

The site opens on http://localhost:5173 and calls the API on http://localhost:5169.

New accounts are customers. To make an admin, set `RoleId` to `1` for that user in `users."Users"`.

## Scripts

| Command | What it does |
| --- | --- |
| `npm run dev` | Starts the dev server |
| `npm run build` | Builds for production into `dist` |
| `npm run lint` | Runs ESLint |
| `npm test` | Runs the tests in `tests` |

## Structure

| Folder | Contents |
| --- | --- |
| `src/pages/store` | Sign in, create account, products, product details, cart, my orders |
| `src/pages/admin` | Add product, all orders |
| `src/pages/shared` | Order details, access denied and not found |
| `src/components` | Shared UI |
| `src/context` | Session and cart state |
| `src/services` | API calls and the logic around them |
