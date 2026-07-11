<p align="center">
  <img src="https://raw.githubusercontent.com/Lemonical/VNO.Core/refs/heads/main/docs/assets/vno-icon.png" width="96" alt="Visual Novel Online icon">
</p>

<h1 align="center">Visual Novel Online Server</h1>

<p align="center">A desktop-managed or headless game host for Visual Novel Online.</p>

<p align="center">
  <a href="https://dotnet.microsoft.com/"><img alt=".NET 10" src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white"></a>
  <a href="https://avaloniaui.net/"><img alt="Avalonia 12" src="https://img.shields.io/badge/Avalonia-12.0.4-8B44AC"></a>
  <a href="#docker"><img alt="Docker" src="https://img.shields.io/badge/Docker-supported-2496ED?logo=docker&logoColor=white"></a>
  <a href="LICENSE"><img alt="MIT License" src="https://img.shields.io/github/license/Lemonical/VNO.Server"></a>
  <a href="https://github.com/Lemonical/VNO.Server/commits/main"><img alt="Last commit" src="https://img.shields.io/github/last-commit/Lemonical/VNO.Server/main"></a>
  <a href="https://github.com/Lemonical/VNO.Server/issues"><img alt="Open issues" src="https://img.shields.io/github/issues/Lemonical/VNO.Server"></a>
</p>

`VNO.Server` hosts live VNO sessions. It can run with an Avalonia staff dashboard, an interactive React Ink console, or as a non-interactive service/container. It authenticates and registers with `VNO.Master`, validates short-lived Client handoffs, and owns gameplay, room, roster, moderation, and host policy.

> [!IMPORTANT]
> This project is under active development. Source, desktop, and Linux container paths exist, but no installer or published platform matrix is currently provided.

## Features

- TCP or WebSocket player listener with Master authentication and public registration
- Master-issued version gate and short-lived, single-use Client handoff validation
- Avalonia staff dashboard, foreground Ink console, and headless service modes
- Player/session tracking, areas, rosters, music, items, timers, and room policies
- Configurable player capacity with admission enforcement and public-directory metrics
- In-character/out-of-character traffic, broadcasts, notices, scene effects, and inventory relays
- Kick, mute, address/account ban, moderator, lock, hide, and stat controls
- Token-protected admin WebSocket with live status, events, issues, and configuration commands
- External legacy-compatible INI/list storage with environment overrides for headless deployments
- Docker service and separate full-screen console image
- Automated tests for gameplay, administration, networking, settings, and UI behavior

## Quick start

### Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Git with submodule support
- A reachable Master account for authenticated/public hosting
- Node.js 18 or newer only when developing the Ink console
- Docker with Compose for container deployment

### Install and build

```bash
git clone --recurse-submodules https://github.com/Lemonical/VNO.Server.git
cd VNO.Server
dotnet restore VNO.Server.slnx
dotnet build VNO.Server.slnx
```

### Run

Desktop staff UI:

```bash
dotnet run --project src/VNO.Server/VNO.Server.csproj
```

Interactive terminal UI or non-interactive service:

```bash
dotnet run --project src/VNO.Server/VNO.Server.csproj -- --cli
dotnet run --project src/VNO.Server/VNO.Server.csproj -- --headless
```

Headless environment authentication requires `VNO_AUTH_USERNAME`, `VNO_AUTH_PASSWORD_FILE`, and `VNO_AUTH_REMEMBER=true`; it exits if Master authentication fails. `--cli` launches the Ink frontend with the service; use `--headless` when attaching the detached console separately.

## Configuration summary

Configuration lives under `data/` next to the binary, unless `VNO_DATA_DIRECTORY` overrides it.

| File | Purpose |
| --- | --- |
| `init.ini` | Server name, port, player capacity, transport, visibility, heartbeat, moderator password, chat limits, and Master-account credentials |
| `areas.ini` | Area names, one INI section per area |
| `charlist.txt` | Authoritative character roster |
| `musiclist.txt` | Optional music list, one entry per line |
| `itemlist.txt` | Authoritative item definitions |

Important headless overrides include `VNO_SERVER_NAME`, `VNO_SERVER_PORT`, `VNO_SERVER_PLAYER_CAPACITY`, `VNO_SERVER_TRANSPORT`, `VNO_SERVER_PUBLIC`, `VNO_AUTH_USERNAME`, `VNO_AUTH_REMEMBER`, and `VNO_AUTH_PASSWORD_FILE`. The public Master endpoint and application version are shared constants in `VNO.Core`; they cannot be overridden by INI, environment, UI, or admin commands. Player capacity accepts `1` through `10000` and defaults to `100`. Use the password-file setting rather than placing a password in source or environment text.

The player listener defaults to TCP port `6541`. The bearer-protected admin endpoint defaults to `127.0.0.1:6542/admin` and stores a generated token in `data/admin.token`.

The complete hosting, configuration, ports/TLS, content, moderation, console, and troubleshooting guides belong in the VNO.Server GitHub wiki once it is enabled.

## Docker

Create the Master-account password secret and set its username:

```bash
mkdir -p secrets
printf '%s' '<master-account-password>' > secrets/vno_auth_password.txt
export VNO_AUTH_USERNAME="your-master-account"
docker compose up --build -d server
```

Attach the console separately:

```bash
docker compose --profile console run --rm console
```

Compose publishes player port `6541`, persists `/app/data`, and keeps admin port `6542` on a private network. Its defaults host players over WebSocket and connect to the shared TLS WebSocket Master endpoint.

## Build, test, and publish

```bash
dotnet build VNO.Server.slnx -c Release
dotnet test VNO.Server.slnx
dotnet publish src/VNO.Server/VNO.Server.csproj -c Release -o ./publish/server
```

For console changes:

```bash
cd clients/server-console
npm ci
npm test
npm run build
```

## Repository layout

```text
src/VNO.Server/             Game host, desktop UI, CLI, and admin endpoint
clients/server-console/     React Ink admin client
tests/VNO.Server.Tests/     Unit, behavior, and UI tests
external/VNO.Core/          Shared protocol submodule
```

## Ecosystem

- [VNO.Core](https://github.com/Lemonical/VNO.Core) - protocol, models, and transports
- [VNO.Master](https://github.com/Lemonical/VNO.Master) - authentication and public directory
- [VNO.Client](https://github.com/Lemonical/VNO.Client) - desktop player

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) before changing gameplay, authentication, administration, or configuration behavior. Use the [issue tracker](https://github.com/Lemonical/VNO.Server/issues) for sanitized reports; never include server credentials, admin tokens, or private player data.

## License

VNO.Server is licensed under the [MIT License](LICENSE).
