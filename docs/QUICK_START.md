# Quick Start Guide

Get Dokanio running in minutes.

## Prerequisites

- Docker & Docker Compose
- Git
- 4GB RAM minimum

## 5-Minute Setup

### 1. Clone & Setup

```bash
git clone https://github.com/yourusername/dokanio.git
cd dokanio
cp .env.example .env
```

### 2. Start Services

```bash
docker-compose up -d
```

### 3. Access Application

- **API**: http://localhost:5000
- **Dashboard**: http://localhost:3000
- **Grafana**: http://localhost:3001 (admin/admin)

## Common Commands

```bash
# View logs
docker-compose logs -f

# Stop services
docker-compose down

# Rebuild images
docker-compose build

# Run tests
make test

# Build locally
make build
```

## Using Make

```bash
# View all commands
make help

# Start services
make up

# View logs
make logs

# Run tests
make test

# Check health
make health-check
```

## Troubleshooting

### Services won't start
```bash
docker-compose logs
docker-compose down -v
docker-compose up -d
```

### Port already in use
```bash
# Change ports in docker-compose.yml or .env
# Or kill process using port:
lsof -i :5000
kill -9 <PID>
```

### Database connection error
```bash
# Wait for PostgreSQL to be ready
docker-compose exec postgres pg_isready -U pos_user
```

## Next Steps

- Read [DOCKER_SETUP.md](DOCKER_SETUP.md) for detailed Docker guide
- Read [CI_CD_GUIDE.md](CI_CD_GUIDE.md) for CI/CD setup
- Check [README.md](README.md) for full documentation

## Need Help?

1. Check logs: `docker-compose logs`
2. Review guides above
3. Open GitHub issue
4. Contact team
