# Store frontend

React and Vite app for the e-commerce platform. It talks to the backend through the Vite proxy, so the browser only sees one origin.

## Run it

From the repository root, start the database, create the inventory database and apply the migrations once:

```sh
docker compose up -d
docker compose exec postgres psql -U ecommerce_user -d postgres -c "CREATE DATABASE inventory_db"
dotnet ef database update --project src/Modules/Users --startup-project src/Host --context UsersDbContext
dotnet ef database update --project src/Modules/Products --startup-project src/Host --context ProductsDbContext
dotnet ef database update --project src/Modules/Orders --startup-project src/Host --context OrdersDbContext
```

Create `inventory-service/.env`:

```sh
DB_USER=ecommerce_user
DB_PASSWORD=ecommerce_password
DB_HOST=127.0.0.1
DB_PORT=5432
DB_NAME=inventory_db
GRPC_PORT=50051
```

Start the inventory service:

```sh
cd inventory-service
npm install
node server.js
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

The site opens on http://localhost:5173 and calls the API on http://localhost:5169. The API reaches the inventory service on port 50051.

New accounts are customers. To make an admin, set `RoleId` to `1` for that user in `users."Users"`.

## Scripts

| Command | What it does |
| --- | --- |
| `npm run dev` | Starts the dev server |
| `npm run build` | Builds for production into `dist` |
| `npm run lint` | Runs ESLint |
| `npm test` | Runs the tests in `tests` |
| `npm run test:last-piece` | Sends many orders at once for the last piece and checks that one succeeds |

## Structure

| Folder | Contents |
| --- | --- |
| `src/pages/store` | Sign in, create account, products, product details, cart, my orders |
| `src/pages/admin` | Add product, all orders |
| `src/pages/shared` | Order details, access denied and not found |
| `src/components` | Shared UI |
| `src/context` | Session and cart state |
| `src/services` | API calls and the logic around them |
