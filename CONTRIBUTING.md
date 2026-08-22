# Contributing to Carnot Cycle Circus

Thank you for your interest in contributing to **Carnot Cycle Circus**! We welcome contributions, whether it's fixing bugs, enhancing documentation, or implementing new orchestration features.

## Code of Conduct
By participating in this project, you agree to abide by our [Code of Conduct](CODE_OF_CONDUCT.md).

## Getting Started
1. Fork the repository on GitHub.
2. Clone your fork locally:
   ```bash
   git clone https://github.com/your-username/carnot-cycle-circus.git
   cd carnot-cycle-circus
   ```
3. Ensure you have the .NET 10 SDK installed:
   ```bash
   dotnet --info
   ```
4. Create a feature branch:
   ```bash
   git checkout -b feat/your-feature-name
   ```

## Development Workflow
- **Coding Standards**: Follow the guidelines outlined in `CLAUDE.md` and `skills/csharp-coding-standards/SKILL.md`.
- **Building**:
  ```bash
  dotnet build CarnotCycleCircus.slnx
  ```
- **Testing**:
  ```bash
  dotnet test CarnotCycleCircus.slnx
  ```
- **Commit Messages**: We follow [Conventional Commits](https://www.conventionalcommits.org/):
  - `feat:` New features
  - `fix:` Bug fixes
  - `docs:` Documentation changes
  - `refactor:` Code refactoring without behavioral changes
  - `test:` Adding or updating tests
  - `chore:` Build, CI, or dependency updates

## Pull Requests
1. Push your branch to GitHub.
2. Open a Pull Request against `main`.
3. Ensure CI checks pass.
4. Address review feedback constructively.
