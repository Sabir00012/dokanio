# Contributing to Dokanio

Thank you for your interest in contributing to Dokanio! This document provides guidelines and instructions for contributing.

## Code of Conduct

Please be respectful and constructive in all interactions.

## Getting Started

1. Fork the repository
2. Clone your fork: `git clone https://github.com/yourusername/dokanio.git`
3. Create a feature branch: `git checkout -b feature/your-feature`
4. Make your changes
5. Commit with conventional commits: `git commit -m "feat: add new feature"`
6. Push to your fork: `git push origin feature/your-feature`
7. Create a Pull Request

## Development Setup

```bash
# Install dependencies
dotnet restore

# Build solution
dotnet build

# Run tests
dotnet test

# Start Docker services
docker-compose up -d

# Run locally
dotnet run --project src/Server/Server.csproj
```

## Code Style

- Follow C# conventions
- Use nullable reference types
- Use implicit usings
- Run code formatter: `make format`
- Run linter: `make lint`

## Testing

- Write tests for new features
- Ensure all tests pass: `make test`
- Maintain minimum 70% code coverage
- Use xUnit for unit tests
- Use FsCheck for property-based tests

## Commit Messages

Use conventional commits:

```
feat: Add new feature
fix: Fix bug
docs: Update documentation
style: Format code
refactor: Refactor code
test: Add tests
chore: Update dependencies
ci: Update CI configuration
```

## Pull Request Process

1. Update documentation
2. Add/update tests
3. Ensure CI passes
4. Request review
5. Address feedback
6. Merge after approval

## Reporting Issues

Include:
- Clear description
- Steps to reproduce
- Expected behavior
- Actual behavior
- Environment details
- Logs/screenshots

## Questions?

- Check existing issues
- Review documentation
- Ask in discussions
- Contact maintainers

Thank you for contributing!
