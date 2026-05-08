# CI/CD Pipeline Guide for Dokanio

This guide explains the GitHub Actions CI/CD pipeline setup for the Dokanio POS system.

## Overview

The CI/CD pipeline automates:
- Building and testing the application
- Code quality checks and security scanning
- Docker image building and publishing
- Automated deployments
- Dependency updates
- Release management

## Workflows

### 1. CI Pipeline (`ci.yml`)

Runs on every push to `main` and `develop` branches, and on pull requests.

**Jobs:**
- **build-and-test**: Builds the solution and runs unit tests
  - Restores NuGet packages
  - Builds in Release mode
  - Runs Shared.Core tests
  - Runs Mobile tests
  - Uploads coverage reports to Codecov

- **code-quality**: Performs code analysis
  - Runs StyleCop analysis
  - Enforces code style rules

- **build-docker**: Builds Docker images
  - Builds Server image
  - Builds WebDashboard image
  - Builds Tests image
  - Pushes to GitHub Container Registry (on main branch)

- **security-scan**: Scans for vulnerabilities
  - Runs Trivy filesystem scan
  - Uploads results to GitHub Security tab

- **integration-tests**: Tests with Docker Compose
  - Starts PostgreSQL and Redis
  - Verifies service health
  - Cleans up resources

### 2. Code Coverage (`code-coverage.yml`)

Runs on push to `main` and `develop`, and daily at 2 AM UTC.

**Features:**
- Generates detailed coverage reports
- Uploads to Codecov
- Comments on PRs with coverage percentage
- Enforces minimum coverage threshold (70%)
- Generates HTML coverage report

### 3. Release (`release.yml`)

Triggered when a tag matching `v*` is pushed.

**Features:**
- Extracts version from git tag
- Generates changelog from commits
- Creates GitHub Release
- Marks as prerelease if version contains "alpha" or "beta"
- Includes Docker image references

### 4. Deploy (`deploy.yml`)

Triggered on push to `main` or when manually triggered.

**Features:**
- Supports staging and production environments
- Pulls Docker images
- Runs deployment logic
- Verifies deployment
- Sends notifications

### 5. Dependabot (`dependabot.yml`)

Automatically creates PRs for dependency updates.

**Configuration:**
- NuGet packages: Weekly updates
- Docker images: Weekly updates
- GitHub Actions: Weekly updates
- Auto-merge enabled for Dependabot PRs

## Setup Instructions

### 1. GitHub Repository Secrets

Add these secrets to your GitHub repository settings:

```
GITHUB_TOKEN          # Automatically provided by GitHub
CODECOV_TOKEN         # From codecov.io
DOCKER_REGISTRY_USER  # GitHub username
DOCKER_REGISTRY_PASS  # GitHub personal access token
```

### 2. Enable GitHub Container Registry

1. Go to repository Settings → Packages
2. Enable GitHub Container Registry
3. Create a personal access token with `write:packages` scope

### 3. Configure Branch Protection

1. Go to Settings → Branches
2. Add rule for `main` branch
3. Require status checks to pass:
   - build-and-test
   - code-quality
   - build-docker
   - security-scan
   - integration-tests

### 4. Setup Codecov Integration

1. Visit https://codecov.io
2. Connect your GitHub account
3. Enable repository
4. Copy token to GitHub Secrets

## Workflow Triggers

### Automatic Triggers

| Workflow | Trigger |
|----------|---------|
| CI | Push to main/develop, PR to main/develop |
| Code Coverage | Push to main/develop, Daily 2 AM UTC |
| Release | Push tag matching v* |
| Dependabot | Scheduled (weekly) |

### Manual Triggers

All workflows support manual triggering via GitHub Actions UI:

1. Go to Actions tab
2. Select workflow
3. Click "Run workflow"
4. Select branch/options
5. Click "Run workflow"

## Docker Image Publishing

### Image Naming Convention

```
ghcr.io/yourusername/dokanio-server:TAG
ghcr.io/yourusername/dokanio-webdashboard:TAG
ghcr.io/yourusername/dokanio-tests:TAG
```

### Available Tags

- `latest` - Latest main branch build
- `develop` - Latest develop branch build
- `v1.0.0` - Release version
- `main-abc123` - Commit SHA on main
- `develop-abc123` - Commit SHA on develop

### Pulling Images

```bash
# Login to GitHub Container Registry
docker login ghcr.io -u USERNAME -p TOKEN

# Pull images
docker pull ghcr.io/yourusername/dokanio-server:latest
docker pull ghcr.io/yourusername/dokanio-webdashboard:latest
```

## Testing

### Local Testing

Run tests locally before pushing:

```bash
# Run all tests
make test

# Run specific test project
make test-core
make test-mobile

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### CI Test Results

View test results in GitHub Actions:

1. Go to Actions tab
2. Select workflow run
3. Click "build-and-test" job
4. View test output

## Code Coverage

### Coverage Reports

Coverage reports are generated and uploaded to Codecov:

1. View on Codecov: https://codecov.io/gh/yourusername/dokanio
2. View in PR: Coverage badge and comment
3. Download artifact: GitHub Actions → Artifacts

### Coverage Threshold

Minimum coverage threshold is 70%. If coverage drops below this:
- CI will fail
- PR cannot be merged
- Review coverage report to identify gaps

### Improving Coverage

1. Write tests for uncovered code
2. Run coverage locally: `dotnet test --collect:"XPlat Code Coverage"`
3. View HTML report: `coverage-report/index.html`

## Security Scanning

### Trivy Vulnerability Scanning

Trivy scans for known vulnerabilities in:
- Docker images
- Dependencies
- Source code

Results are uploaded to GitHub Security tab:

1. Go to Security tab
2. Click "Code scanning alerts"
3. Review and fix vulnerabilities

### Dependency Scanning

Dependabot automatically scans for:
- Outdated NuGet packages
- Outdated Docker images
- Outdated GitHub Actions

Creates PRs for updates automatically.

## Deployment

### Staging Deployment

```bash
# Manual trigger
1. Go to Actions → Deploy
2. Click "Run workflow"
3. Select "staging" environment
4. Click "Run workflow"
```

### Production Deployment

```bash
# Automatic on main branch push
# Or manual trigger with "production" environment
```

### Deployment Verification

After deployment:

1. Check service health
2. Verify API endpoints
3. Check logs for errors
4. Run smoke tests

## Troubleshooting

### Workflow Failures

1. Check workflow logs:
   - Go to Actions tab
   - Click failed workflow
   - View job logs

2. Common issues:
   - **Build failure**: Check .NET version, dependencies
   - **Test failure**: Check test logs, database connection
   - **Docker build failure**: Check Dockerfile, build context
   - **Security scan failure**: Review vulnerabilities

### Debugging

Enable debug logging:

```yaml
- name: Enable debug logging
  run: |
    echo "ACTIONS_STEP_DEBUG=true" >> $GITHUB_ENV
```

### Rerunning Workflows

1. Go to Actions tab
2. Click workflow run
3. Click "Re-run all jobs" or "Re-run failed jobs"

## Best Practices

### Commit Messages

Use conventional commits for better changelog generation:

```
feat: Add new feature
fix: Fix bug
docs: Update documentation
chore: Update dependencies
ci: Update CI configuration
```

### Pull Requests

1. Create PR from feature branch to develop
2. Wait for CI to pass
3. Request review
4. Merge after approval
5. Delete feature branch

### Releases

1. Create release branch from develop
2. Update version numbers
3. Update CHANGELOG
4. Create PR to main
5. Merge after approval
6. Tag release: `git tag v1.0.0`
7. Push tag: `git push origin v1.0.0`

## Monitoring

### GitHub Actions Dashboard

Monitor workflow runs:
1. Go to Actions tab
2. View workflow runs
3. Check status and duration
4. Review logs

### Notifications

Configure notifications:
1. Go to Settings → Notifications
2. Enable workflow notifications
3. Choose notification method (email, web)

## Advanced Configuration

### Custom Workflows

Create custom workflows for specific needs:

```yaml
name: Custom Workflow
on:
  schedule:
    - cron: '0 0 * * *'  # Daily at midnight

jobs:
  custom-job:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - run: echo "Custom workflow"
```

### Matrix Builds

Test against multiple configurations:

```yaml
strategy:
  matrix:
    dotnet-version: ['8.0.x', '9.0.x', '10.0.x']
    os: [ubuntu-latest, windows-latest]
```

### Conditional Steps

Run steps conditionally:

```yaml
- name: Deploy to production
  if: github.ref == 'refs/heads/main'
  run: ./deploy.sh
```

## Resources

- [GitHub Actions Documentation](https://docs.github.com/en/actions)
- [GitHub Container Registry](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry)
- [Trivy Documentation](https://aquasecurity.github.io/trivy/)
- [Codecov Documentation](https://docs.codecov.io/)
- [Dependabot Documentation](https://docs.github.com/en/code-security/dependabot)

## Support

For issues or questions:
1. Check workflow logs
2. Review this guide
3. Open an issue on GitHub
4. Contact the development team
