# Security Policy

## Reporting Security Vulnerabilities

**Do not** open public issues for security vulnerabilities.

Instead, please email security@dokanio.dev with:
- Description of vulnerability
- Steps to reproduce
- Potential impact
- Suggested fix (if any)

We will acknowledge receipt within 48 hours and provide updates regularly.

## Security Practices

### Code Security
- All code is reviewed before merging
- Security scanning on every commit
- Dependency vulnerability scanning
- Static code analysis

### Infrastructure Security
- Secrets managed via GitHub Secrets
- No credentials in code or images
- HTTPS enforced in production
- Regular security updates

### Data Security
- Encrypted connections (TLS/SSL)
- Secure password hashing (BCrypt)
- SQL injection prevention (parameterized queries)
- CSRF protection enabled

## Supported Versions

| Version | Status | Support Until |
|---------|--------|---------------|
| 1.x | Active | Current + 1 year |
| 0.x | Deprecated | 2024-12-31 |

## Security Updates

Security updates are released as soon as possible after discovery and verification.

## Dependencies

We use:
- Dependabot for automated dependency updates
- Trivy for vulnerability scanning
- GitHub Security scanning

## Best Practices

### For Users
1. Keep Docker images updated
2. Use strong passwords
3. Enable HTTPS in production
4. Regularly backup data
5. Monitor logs for suspicious activity

### For Developers
1. Never commit secrets
2. Use environment variables for sensitive data
3. Validate all inputs
4. Use parameterized queries
5. Keep dependencies updated
6. Run security scans locally

## Compliance

Dokanio follows:
- OWASP Top 10 guidelines
- CWE/SANS Top 25
- .NET security best practices
- Docker security best practices

## Contact

- Security Issues: security@dokanio.dev
- General Questions: support@dokanio.dev
- GitHub Issues: https://github.com/yourusername/dokanio/issues
