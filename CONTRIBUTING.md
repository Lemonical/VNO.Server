# Contributing to VNO.Server

`VNO.Server` is the Avalonia desktop game server and staff control surface for the Visual Novel Online stack. Contributions should keep the local host, moderation flows and Master Server connection behavior stable, testable and easy to maintain.

## Prerequisites

- .NET 10 SDK
- Git with submodule support

## Setup

```bash
git clone --recurse-submodules https://github.com/Lemonical/VNO.Server.git
cd VNO.Server
dotnet restore VNO.Server.slnx
```

If you already cloned without submodules:

```bash
git submodule update --init --recursive
```

## Development Notes

When making changes, please keep the following in mind:

- Keep server-side message handling aligned with `external/VNO.Core`.
- Preserve the behavior where the game server can continue operating when the Master Server is unavailable.
- Keep moderation, room, broadcast, timer and staff-control behaviour predictable and testable.
- Avoid mixing unrelated refactors with behaviour changes.
- Prefer small, focused changes that are easier to review.
- Add comments for non-obvious logic, especially around protocol handling, moderation rules and connection recovery.

## Testing

Run the project test suite before opening a pull request:

```bash
dotnet test VNO.Server.slnx
```

Add or update tests when changing:

- Server-side message handling
- Moderation flows
- Room locks
- Broadcast behavior
- Timers
- Staff lookup behavior
- Master Server connection and retry behavior
- Area or user-list synchronization

## Pull Requests

Before opening a pull request, make sure that:

- Submodules are initialized and up to date.
- The project builds successfully.
- The test suite passes.
- Any protocol or configuration changes are documented.
- The pull request explains the gameplay, moderation, hosting or protocol impact clearly.

Please mention any required configuration changes for local verification.

## Issues

Use GitHub issues to report bugs, moderation workflow problems, hosting issues or protocol-related concerns.

When reporting a bug, please include:

- What you expected to happen
- What actually happened
- Steps to reproduce the issue
- Relevant logs or screenshots, if available
- Your operating system and .NET SDK version

## License

By contributing to this repository, you agree that your contributions will be licensed under the MIT License that covers this project.
