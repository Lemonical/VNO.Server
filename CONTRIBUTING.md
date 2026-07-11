# Contributing to VNO.Server

Server changes must keep the game host, Master link, Avalonia dashboard, headless service, admin endpoint, and React Ink console consistent and testable.

## Setup

```bash
git clone --recurse-submodules https://github.com/Lemonical/VNO.Server.git
cd VNO.Server
dotnet restore VNO.Server.slnx
dotnet build VNO.Server.slnx
dotnet test VNO.Server.slnx
```

Requirements are the .NET 10 SDK and Git with submodule support. Node.js 18 or newer is required for console changes; Docker is required for container verification.

```bash
cd clients/server-console
npm ci
npm test
npm run build
```

## Change guidelines

- Keep gameplay and host policy in services/controllers, not views or console rendering code.
- Keep message handling aligned with `external/VNO.Core` and coordinate contract changes.
- Preserve explicit GUI, `--cli`, and `--headless` lifecycles.
- Keep Server available-state, Master reconnect/failure, and authentication behavior deliberate and tested.
- Treat credentials, handoff tokens, moderation, bans, admin authentication, and untrusted messages as security-sensitive.
- Use `VNO_AUTH_PASSWORD_FILE` for headless secrets and never log passwords or admin tokens.
- Update C# and TypeScript sides together when admin frames or commands change.
- Preserve legacy-compatible data files unless a documented migration is part of the change.
- Keep admin endpoints loopback/private by default and bound all remote input.

## Testing

Run `dotnet test VNO.Server.slnx` for every backend or desktop change. Add focused tests for:

- Player connection, version/handoff validation, reconnect, and cancellation
- Capacity/admission, public metrics, areas, character picks, music, items, chat, room state, timers, and relays
- Moderation, bans, locks, visibility, stats, notices, and staff lookup
- Master authentication, public registration, heartbeat, and failure recovery
- INI/list loading, environment overrides, persistence, and validation
- Admin authentication, frames, commands, limits, and detached-console behavior
- Avalonia view-model and headless startup behavior

Run `npm test` and `npm run build` whenever the server console changes. Manually test the affected GUI/CLI/headless path and state the Master/transport configuration used.

## Documentation

Keep the README concise. Put full Docker/headless, content, moderation, ports/TLS, environment, console, and troubleshooting tutorials in the VNO.Server GitHub wiki once it is enabled. Update docs alongside configuration or operator-flow changes.

## Pull requests and issues

Pull requests must explain gameplay and operational impact, include tests, pass applicable .NET/npm checks, document protocol/config/Docker changes, and identify required Core, Master, or Client updates. Keep unrelated cleanup separate and include no credentials or private player data.

Bug reports should include reproducible steps, expected/actual behavior, sanitized logs, OS, .NET/Node/Docker versions as applicable, run mode, transport, and repository revisions. Report exploitable security issues privately.

## License

By contributing, you agree that your contribution is licensed under this project's [MIT License](LICENSE).
