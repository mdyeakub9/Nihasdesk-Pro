# infra

docker-compose for local dev, Coolify deployment notes (later).

## Local development

Requirements: Docker, .NET SDK (version in `backend/global.json`).

1. Create your env file and set your own passwords:
   ```bash
   cp infra/.env.example infra/.env
   ```
2. Start PostgreSQL (with pgvector), Redis and RabbitMQ:
   ```bash
   docker compose -f infra/docker-compose.yml --env-file infra/.env up -d
   ```
   Ports are bound to `127.0.0.1` only. RabbitMQ management UI: http://localhost:15672. Mailpit catches every email the API sends: http://localhost:8025.
   On the first start, `postgres/init` creates the app role `nihasdesk_app` (not table owner, cannot bypass RLS — `docs/standards/database.md`). If your database volume existed before that script, run it once by hand:
   ```bash
   docker compose -f infra/docker-compose.yml --env-file infra/.env exec postgres sh /docker-entrypoint-initdb.d/01-app-role.sh
   ```
   (In Git Bash prefix the command with `MSYS_NO_PATHCONV=1`.)
3. Give the API its connection strings with user secrets (kept outside the repo), using the same passwords as in `.env`. The app connects as `nihasdesk_app`; the owner connection is only for migrations:
   ```bash
   cd backend/src/Nihasdesk.Api
   dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5432;Database=nihasdesk;Username=nihasdesk_app;Password=<APP_DB_PASSWORD>"
   dotnet user-secrets set "ConnectionStrings:PostgresMigrations" "Host=localhost;Port=5432;Database=nihasdesk;Username=nihasdesk;Password=<POSTGRES_PASSWORD>"
   dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379,password=<REDIS_PASSWORD>"
   dotnet user-secrets set "ConnectionStrings:RabbitMq" "amqp://nihasdesk:<RABBITMQ_PASSWORD>@localhost:5672/"
   ```
4. Apply database migrations as the owner (the app never migrates itself):
   ```bash
   cd backend
   dotnet ef database update --project src/Modules/Identity --startup-project src/Modules/Identity --connection "Host=localhost;Port=5432;Database=nihasdesk;Username=nihasdesk;Password=<POSTGRES_PASSWORD>"
   dotnet ef database update --project src/Modules/Contacts --startup-project src/Modules/Contacts --connection "Host=localhost;Port=5432;Database=nihasdesk;Username=nihasdesk;Password=<POSTGRES_PASSWORD>"
   dotnet ef database update --project src/Modules/Inbox --startup-project src/Modules/Inbox --connection "Host=localhost;Port=5432;Database=nihasdesk;Username=nihasdesk;Password=<POSTGRES_PASSWORD>"
   dotnet ef database update --project src/Modules/Channels --startup-project src/Modules/Channels --connection "Host=localhost;Port=5432;Database=nihasdesk;Username=nihasdesk;Password=<POSTGRES_PASSWORD>"
   dotnet ef database update --project src/Modules/Commerce --startup-project src/Modules/Commerce --connection "Host=localhost;Port=5432;Database=nihasdesk;Username=nihasdesk;Password=<POSTGRES_PASSWORD>"
   dotnet ef database update --project src/Modules/AiAgent --startup-project src/Modules/AiAgent --connection "Host=localhost;Port=5432;Database=nihasdesk;Username=nihasdesk;Password=<POSTGRES_PASSWORD>"
   dotnet ef database update --project src/Modules/Shipping --startup-project src/Modules/Shipping --connection "Host=localhost;Port=5432;Database=nihasdesk;Username=nihasdesk;Password=<POSTGRES_PASSWORD>"
   ```
   Each module has its own migrations; run all of them after pulling new ones.
5. Run the API (HTTPS only: session and CSRF cookies are `Secure`). Trust the .NET development certificate once:
   ```bash
   dotnet dev-certs https --trust
   ```
   Then run it and check health:
   ```bash
   dotnet run --project backend/src/Nihasdesk.Api
   ```
   - https://localhost:5080/health — liveness (process is up)
   - https://localhost:5080/health/ready — readiness (PostgreSQL, Redis, RabbitMQ reachable)
6. Run the Workers host as well — it delivers queued email (ADR 0013); without it nothing reaches Mailpit. Give it the same app connection string once:
   ```bash
   cd backend/src/Nihasdesk.Workers
   dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5432;Database=nihasdesk;Username=nihasdesk_app;Password=<APP_DB_PASSWORD>"
   ```
   ```bash
   dotnet run --project backend/src/Nihasdesk.Workers
   ```
   Its launch profile sets the Development environment, which is what loads the user secrets above.
   In Visual Studio: solution → **Configure Startup Projects** → **Multiple startup projects** → start `Nihasdesk.Api` and `Nihasdesk.Workers`.

Stop with `docker compose -f infra/docker-compose.yml down` (add `-v` to delete local data).
