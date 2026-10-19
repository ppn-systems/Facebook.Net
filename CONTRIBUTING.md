# Contributing to Facebook.Net

Thank you for considering contributing to Facebook.Net!

---

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Development Environment](#development-environment)
- [How to Contribute](#how-to-contribute)
- [Commit Convention](#commit-convention)
- [Coding Standards](#coding-standards)
- [Pull Request Process](#pull-request-process)
- [CI Quality Gates](#ci-quality-gates)
- [Release Automation](#release-automation)
- [Issue Reporting](#issue-reporting)
- [Community](#community)

---

## Code of Conduct

This project and everyone participating in it is governed by the [Facebook.Net Code of Conduct](CODE_OF_CONDUCT.md). By participating, you are expected to uphold this code. Please report unacceptable behavior via a [private security advisory](https://github.com/ppn-systems/Facebook.Net/security/advisories/new).

---

## Development Environment

### Prerequisites

| Tool | Version |
| :--- | :--- |
| [.NET SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | 10.0+ |
| IDE | [Visual Studio 2026](https://visualstudio.microsoft.com/vs/) / [VS Code](https://code.visualstudio.com/) / [Rider](https://www.jetbrains.com/rider/) |
| [Git](https://git-scm.com/) | Latest |

### Getting Started

1. **Fork** the repository on GitHub.

2. **Clone** your fork locally:

   ```bash
   git clone https://github.com/<your-username>/Facebook.Net.git
   cd Facebook.Net
   ```

3. **Add upstream** remote:

   ```bash
   git remote add upstream https://github.com/ppn-systems/Facebook.Net.git
   ```

4. **Create a branch** for your work:

   ```bash
   git checkout -b feature/your-feature-name
   ```

5. **Build** the solution to verify everything compiles:

   ```bash
   dotnet build Facebook.Net.slnx --configuration Release
   ```

---

## How to Contribute

### Workflow

1. Sync with upstream: `git pull upstream master`
2. Create a feature branch.
3. Make your changes.
4. Write or update tests as needed.
5. Run the test suite: `dotnet test Facebook.Net.slnx --configuration Release`
6. Commit with a [conventional message](#commit-convention).
7. Push to your fork and open a Pull Request.

---

## Commit Convention

We follow the [Conventional Commits](https://www.conventionalcommits.org/) specification for consistent commit history and automated versioning.

### Format

```text
<type>(optional-scope): <short description>

[optional body]

[optional footer]
```

### Commit Types

| Type | Purpose |
| :--- | :--- |
| `feat` | A new feature |
| `fix` | A bug fix |
| `refactor` | Code change (no feature/fix) |
| `perf` | Performance improvement |
| `docs` | Documentation only |
| `test` | Adding or updating tests |
| `build` | Build system or dependencies |
| `ci` | CI/CD pipeline changes |
| `chore` | Maintenance (no production code) |
| `style` | Formatting or non-functional cleanup |
| `revert` | Revert a previous commit |

---

## Coding Standards

### C# Code Style

We follow the [Microsoft C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions) with additional guidelines:

- Use **4 spaces** for indentation (no tabs).
- Use `var` when the type is obvious; explicit type declarations otherwise.
- Use expression-bodied members when appropriate.
- Prefer pattern matching (`is`) over type checking and casting.
- Keep methods short and focused on a single responsibility.
- Write self-documenting code; avoid excessive comments.
- Full formatting rules are codified in [`.editorconfig`](.editorconfig).

---

## Pull Request Process

1. Ensure your code adheres to the [coding standards](#coding-standards).
2. Update documentation if your changes affect public APIs or behavior.
3. Include relevant tests for your changes.
4. Verify the PR passes all [CI quality gates](#ci-quality-gates).
5. A maintainer will review your PR and may request changes.
6. Once approved, your PR will be merged into the `master` branch.

---

## CI Quality Gates

Pull requests and pushes to `master` must pass the shared `_build.yml` workflow:

| Check | Description |
| :--- | :--- |
| `dotnet format --verify-no-changes` | Code formatting compliance |
| Release build | Full solution compilation |
| Test execution | TRX results with XPlat Code Coverage |
| Coverage artifacts | Cobertura format generation |

---

## Release Automation

Releases follow Conventional Commits and are driven from the `master` branch:

- `fix` commits → next **patch** release.
- `feat` commits → next **minor** release.
- `BREAKING CHANGE:` footers or `!` markers → next **major** release.

---

## Issue Reporting

When reporting issues, please use the provided templates and include:

1. A clear, descriptive title.
2. Steps to reproduce the issue.
3. Expected behavior.
4. Actual behavior.
5. Environment details (OS, .NET version, etc.).
6. Any relevant logs or screenshots.

---

## Community

Join the conversation:

- [GitHub Discussions](https://github.com/ppn-systems/Facebook.Net/discussions)
- [GitHub Issues](https://github.com/ppn-systems/Facebook.Net/issues)

---

<p align="center">
  ❤️ Thank you for contributing to Facebook.Net!
</p>
