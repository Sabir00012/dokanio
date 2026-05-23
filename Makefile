.PHONY: help build up down logs clean test lint format docker-build docker-push

# Variables
DOCKER_REGISTRY ?= ghcr.io
DOCKER_IMAGE_PREFIX ?= $(DOCKER_REGISTRY)/dokanio
DOCKER_TAG ?= latest
DOTNET_VERSION ?= 10.0.x

help:
	@echo "Dokanio Development Commands"
	@echo "============================"
	@echo ""
	@echo "Docker Commands:"
	@echo "  make docker-build          Build all Docker images"
	@echo "  make docker-push           Push Docker images to registry"
	@echo "  make up                    Start all services with Docker Compose"
	@echo "  make down                  Stop all services"
	@echo "  make logs                  View logs from all services"
	@echo "  make logs-server           View logs from server service"
	@echo "  make logs-dashboard        View logs from dashboard service"
	@echo ""
	@echo ".NET Commands:"
	@echo "  make build                 Build the solution"
	@echo "  make test                  Run all tests"
	@echo "  make test-core             Run Shared.Core tests"
	@echo "  make test-mobile           Run Mobile tests"
	@echo "  make clean                 Clean build artifacts"
	@echo "  make restore               Restore NuGet packages"
	@echo ""
	@echo "Code Quality:"
	@echo "  make lint                  Run code analysis"
	@echo "  make format                Format code"
	@echo ""
	@echo "Database:"
	@echo "  make db-migrate            Run EF Core migrations"
	@echo "  make db-backup             Backup PostgreSQL database"
	@echo "  make db-restore            Restore PostgreSQL database"
	@echo ""
	@echo "Development:"
	@echo "  make dev-server            Run API server locally"
	@echo "  make dev-dashboard         Run Web Dashboard locally"
	@echo ""

# Docker Commands
docker-build:
	@echo "Building Docker images..."
	docker-compose build

docker-push:
	@echo "Pushing Docker images to registry..."
	docker tag dokanio-server:latest $(DOCKER_IMAGE_PREFIX)-server:$(DOCKER_TAG)
	docker tag dokanio-webdashboard:latest $(DOCKER_IMAGE_PREFIX)-webdashboard:$(DOCKER_TAG)
	docker push $(DOCKER_IMAGE_PREFIX)-server:$(DOCKER_TAG)
	docker push $(DOCKER_IMAGE_PREFIX)-webdashboard:$(DOCKER_TAG)

up:
	@echo "Starting services..."
	docker-compose up -d
	@echo "Services started. Access:"
	@echo "  API Server: http://localhost:5000"
	@echo "  Dashboard: http://localhost:3000"
	@echo "  Grafana: http://localhost:3001"
	@echo "  Kibana: http://localhost:5601"

down:
	@echo "Stopping services..."
	docker-compose down

logs:
	docker-compose logs -f

logs-server:
	docker-compose logs -f server

logs-dashboard:
	docker-compose logs -f webdashboard

# .NET Commands
build:
	@echo "Building solution..."
	dotnet build OfflineFirstPOS.sln -c Release

test:
	@echo "Running all tests..."
	dotnet test OfflineFirstPOS.sln -c Release --logger "console;verbosity=detailed"

test-core:
	@echo "Running Shared.Core tests..."
	dotnet test src/Shared.Core/Shared.Core.csproj -c Release --logger "console;verbosity=detailed"

test-mobile:
	@echo "Running Mobile tests..."
	dotnet test src/Mobile/Tests/Mobile.Tests.csproj -c Release --logger "console;verbosity=detailed"

clean:
	@echo "Cleaning build artifacts..."
	dotnet clean OfflineFirstPOS.sln
	find . -type d -name "bin" -exec rm -rf {} + 2>/dev/null || true
	find . -type d -name "obj" -exec rm -rf {} + 2>/dev/null || true

restore:
	@echo "Restoring NuGet packages..."
	dotnet restore OfflineFirstPOS.sln

# Code Quality
lint:
	@echo "Running code analysis..."
	dotnet build OfflineFirstPOS.sln -c Release /p:EnforceCodeStyleInBuild=true

format:
	@echo "Formatting code..."
	dotnet format OfflineFirstPOS.sln

# Database Commands
db-migrate:
	@echo "Running EF Core migrations..."
	dotnet ef database update --project src/Shared.Core/Shared.Core.csproj

db-backup:
	@echo "Backing up database..."
	docker-compose exec -T postgres pg_dump -U pos_user pos_multi_business > backup_$(shell date +%Y%m%d_%H%M%S).sql
	@echo "Backup completed"

db-restore:
	@echo "Restoring database from backup..."
	@read -p "Enter backup file path: " backup_file; \
	docker-compose exec -T postgres psql -U pos_user pos_multi_business < $$backup_file
	@echo "Restore completed"

# Development Commands
dev-server:
	@echo "Running API server locally..."
	dotnet run --project src/Server/Server.csproj

dev-dashboard:
	@echo "Running Web Dashboard locally..."
	dotnet run --project src/WebDashboard/WebDashboard.csproj

# Utility Commands
health-check:
	@echo "Checking service health..."
	@docker-compose ps
	@echo ""
	@echo "Health checks:"
	@docker-compose exec -T postgres pg_isready -U pos_user -d pos_multi_business && echo "✓ PostgreSQL" || echo "✗ PostgreSQL"
	@docker-compose exec -T redis redis-cli ping > /dev/null && echo "✓ Redis" || echo "✗ Redis"
	@curl -s http://localhost:5000/health > /dev/null && echo "✓ API Server" || echo "✗ API Server"
	@curl -s http://localhost:3000/health > /dev/null && echo "✓ Dashboard" || echo "✗ Dashboard"

version:
	@echo "Dokanio Version Information"
	@echo "============================"
	@echo "Docker: $$(docker --version)"
	@echo "Docker Compose: $$(docker-compose --version)"
	@echo ".NET: $$(dotnet --version)"
	@echo "Git: $$(git --version)"

# CI/CD Commands
ci-build:
	@echo "Running CI build..."
	dotnet build OfflineFirstPOS.sln -c Release
	dotnet test OfflineFirstPOS.sln -c Release

ci-docker:
	@echo "Building Docker images for CI..."
	docker build -f Dockerfile.server -t dokanio-server:ci .
	docker build -f Dockerfile.webdashboard -t dokanio-webdashboard:ci .
	docker build -f Dockerfile.tests -t dokanio-tests:ci .

# Cleanup Commands
prune:
	@echo "Pruning Docker resources..."
	docker system prune -f
	docker volume prune -f

prune-all:
	@echo "Pruning all Docker resources (including unused images)..."
	docker system prune -af
	docker volume prune -f

# Default target
.DEFAULT_GOAL := help
