# Docker Setup Guide for Dokanio

This guide explains how to build, run, and manage the Dokanio POS system using Docker and Docker Compose.

## Prerequisites

- Docker 20.10+
- Docker Compose 2.0+
- Git
- 4GB RAM minimum (8GB recommended)
- 10GB disk space

## Quick Start

### 1. Clone the repository

```bash
git clone https://github.com/yourusername/dokanio.git
cd dokanio
```

### 2. Setup environment variables

```bash
cp .env.example .env
```

Edit `.env` and update sensitive values:
- `POSTGRES_PASSWORD` - Strong database password
- `REDIS_PASSWORD` - Strong Redis password
- `JWT_SECRET_KEY` - Generate a secure key (min 32 characters)
- `GRAFANA_PASSWORD` - Grafana admin password

### 3. Build and start services

```bash
# Build all Docker images
docker-compose build

# Start all services
docker-compose up -d

# View logs
docker-compose logs -f
```

### 4. Access the application

- **API Server**: http://localhost:5000
- **Web Dashboard**: http://localhost:3000
- **Prometheus**: http://localhost:9090
- **Grafana**: http://localhost:3001 (admin/admin)
- **Kibana**: http://localhost:5601
- **PostgreSQL**: localhost:5432
- **Redis**: localhost:6379

## Services Overview

### Core Services

#### PostgreSQL (pos-postgres)
- **Image**: postgres:15-alpine
- **Port**: 5432
- **Volume**: postgres_data
- **Purpose**: Main database for server-side data

#### Redis (pos-redis)
- **Image**: redis:7-alpine
- **Port**: 6379
- **Volume**: redis_data
- **Purpose**: Caching and session management

#### API Server (pos-server)
- **Image**: Built from Dockerfile.server
- **Port**: 5000
- **Purpose**: ASP.NET Core REST API and sync hub
- **Dependencies**: PostgreSQL, Redis

#### Web Dashboard (pos-webdashboard)
- **Image**: Built from Dockerfile.webdashboard
- **Port**: 3000
- **Purpose**: Blazor Server admin/analytics dashboard
- **Dependencies**: API Server

### Monitoring Stack

#### Prometheus (pos-prometheus)
- **Image**: prom/prometheus:v2.48.0-alpine
- **Port**: 9090
- **Volume**: prometheus_data
- **Purpose**: Metrics collection and storage

#### Grafana (pos-grafana)
- **Image**: grafana/grafana:10.2.0-alpine
- **Port**: 3001
- **Volume**: grafana_data
- **Purpose**: Metrics visualization and dashboards

#### Elasticsearch (pos-elasticsearch)
- **Image**: docker.elastic.co/elasticsearch/elasticsearch:8.10.0
- **Port**: 9200
- **Volume**: elasticsearch_data
- **Purpose**: Log aggregation and search

#### Kibana (pos-kibana)
- **Image**: docker.elastic.co/kibana/kibana:8.10.0
- **Port**: 5601
- **Purpose**: Log visualization and analysis

## Common Commands

### Start/Stop Services

```bash
# Start all services
docker-compose up -d

# Stop all services
docker-compose down

# Stop and remove volumes (WARNING: deletes data)
docker-compose down -v

# Restart a specific service
docker-compose restart server

# View running services
docker-compose ps
```

### View Logs

```bash
# View logs from all services
docker-compose logs

# View logs from specific service
docker-compose logs server

# Follow logs in real-time
docker-compose logs -f server

# View last 100 lines
docker-compose logs --tail=100 server
```

### Database Management

```bash
# Connect to PostgreSQL
docker-compose exec postgres psql -U pos_user -d pos_multi_business

# Run migrations
docker-compose exec server dotnet ef database update

# Backup database
docker-compose exec postgres pg_dump -U pos_user pos_multi_business > backup.sql

# Restore database
docker-compose exec -T postgres psql -U pos_user pos_multi_business < backup.sql
```

### Redis Management

```bash
# Connect to Redis CLI
docker-compose exec redis redis-cli

# Check Redis info
docker-compose exec redis redis-cli info

# Clear all data
docker-compose exec redis redis-cli FLUSHALL
```

## Building Individual Docker Images

### Build Server Image

```bash
docker build -f Dockerfile.server -t dokanio-server:latest .
```

### Build WebDashboard Image

```bash
docker build -f Dockerfile.webdashboard -t dokanio-webdashboard:latest .
```

### Build Tests Image

```bash
docker build -f Dockerfile.tests -t dokanio-tests:latest .
```

### Build Desktop Image (for CI verification)

```bash
docker build -f Dockerfile.desktop -t dokanio-desktop:latest .
```

### Build Mobile Image (for CI verification)

```bash
docker build -f Dockerfile.mobile -t dokanio-mobile:latest .
```

## Running Tests in Docker

```bash
# Build test image
docker build -f Dockerfile.tests -t dokanio-tests:latest .

# Run tests
docker run --rm dokanio-tests:latest
```

## Production Deployment

### Using Docker Compose

```bash
# Create production environment file
cp .env.example .env.production
# Edit .env.production with production values

# Start services with production config
docker-compose -f docker-compose.yml -f docker-compose.prod.yml up -d
```

### Using Docker Swarm

```bash
# Initialize swarm
docker swarm init

# Deploy stack
docker stack deploy -c docker-compose.yml dokanio
```

### Using Kubernetes

See `k8s/` directory for Kubernetes manifests.

## Troubleshooting

### Services won't start

```bash
# Check service logs
docker-compose logs server

# Verify network connectivity
docker-compose exec server ping postgres

# Check port availability
netstat -an | grep 5000
```

### Database connection errors

```bash
# Verify PostgreSQL is running
docker-compose exec postgres pg_isready

# Check connection string in logs
docker-compose logs server | grep -i connection
```

### Out of disk space

```bash
# Clean up unused images
docker image prune -a

# Clean up unused volumes
docker volume prune

# Check disk usage
docker system df
```

### Memory issues

```bash
# Check resource usage
docker stats

# Limit service memory in docker-compose.yml
# Add under service:
# deploy:
#   resources:
#     limits:
#       memory: 512M
```

## Performance Optimization

### Enable BuildKit

```bash
export DOCKER_BUILDKIT=1
docker-compose build
```

### Use .dockerignore

The project includes `.dockerignore` to exclude unnecessary files from build context.

### Multi-stage builds

All Dockerfiles use multi-stage builds to minimize final image size.

## Security Best Practices

1. **Change default passwords** in `.env` file
2. **Use strong JWT secret** (minimum 32 characters)
3. **Enable Redis authentication** (already configured)
4. **Use HTTPS in production** (configure reverse proxy)
5. **Restrict network access** using firewall rules
6. **Keep images updated** regularly
7. **Scan images for vulnerabilities**:
   ```bash
   docker scan dokanio-server:latest
   ```

## Monitoring and Logging

### Access Grafana Dashboards

1. Navigate to http://localhost:3001
2. Login with credentials from `.env`
3. Import dashboards from `monitoring/grafana/provisioning/dashboards/`

### View Application Logs in Kibana

1. Navigate to http://localhost:5601
2. Create index pattern for application logs
3. View logs in Discover tab

### Prometheus Queries

Common queries for monitoring:

```promql
# CPU usage
rate(container_cpu_usage_seconds_total[5m])

# Memory usage
container_memory_usage_bytes

# Request rate
rate(http_requests_total[5m])

# Error rate
rate(http_requests_total{status=~"5.."}[5m])
```

## CI/CD Integration

GitHub Actions workflows are configured in `.github/workflows/`:

- `ci.yml` - Build, test, and security scanning
- `release.yml` - Create releases and tag images
- `deploy.yml` - Deploy to staging/production

See GitHub Actions documentation for setup.

## Additional Resources

- [Docker Documentation](https://docs.docker.com/)
- [Docker Compose Documentation](https://docs.docker.com/compose/)
- [.NET Docker Images](https://hub.docker.com/_/microsoft-dotnet)
- [PostgreSQL Docker Image](https://hub.docker.com/_/postgres)
- [Redis Docker Image](https://hub.docker.com/_/redis)

## Support

For issues or questions:
1. Check logs: `docker-compose logs`
2. Review this guide
3. Open an issue on GitHub
4. Contact the development team
