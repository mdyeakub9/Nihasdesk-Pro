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
   Ports are bound to `127.0.0.1` only. RabbitMQ management UI: http://localhost:15672.
3. Give the API its connection strings with user secrets (kept outside the repo), using the same passwords as in `.env`:
   ```bash
   cd backend/src/Nihasdesk.Api
   dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5432;Database=nihasdesk;Username=nihasdesk;Password=<POSTGRES_PASSWORD>"
   dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379,password=<REDIS_PASSWORD>"
   dotnet user-secrets set "ConnectionStrings:RabbitMq" "amqp://nihasdesk:<RABBITMQ_PASSWORD>@localhost:5672/"
   ```
4. Run the API and check health:
   ```bash
   dotnet run --project backend/src/Nihasdesk.Api
   ```
   - http://localhost:5080/health — liveness (process is up)
   - http://localhost:5080/health/ready — readiness (PostgreSQL, Redis, RabbitMQ reachable)

Stop with `docker compose -f infra/docker-compose.yml down` (add `-v` to delete local data).
