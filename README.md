# Visual Novel Online Server

Desktop game server and staff control surface for Visual Novel Online, built with .NET 10 and Avalonia 12.

## Overview

Visual Novel Online Server is the desktop server application for hosting and managing VNO game sessions. It is a modern .NET port of the original Delphi-based VNO Server.

The application provides a local server host, staff-facing controls, player connection management, moderation tools, live server logging and communication with the VNO Master Server.

## Project Status

This project is currently under active development. Core server behaviour, moderation flows and staff tools are being rebuilt and tested as part of the migration from the legacy Delphi server.

## Features

Current features include:

- Local game server start and stop controls
- Master Server connection and heartbeat handling
- Player connection tracking
- Live event logging
- Area and user list management
- Server-side moderation flows
- Room lock support
- Broadcast behaviour
- Live out of character chat monitor with server-side broadcast
- Room policies for player hiding, room-count hiding and self stat edits
- Scene effect and inventory-check relaying
- Timer handling
- Staff lookup behaviour
- Configurable server name, port, visibility, areas, music and characters
- Master-issued, short-lived, single-use client authentication
- Token-authenticated React Ink administration console
- Headless Docker service and separate console image

More features will be added as the port develops.

## Requirements

- .NET 10 SDK
- Git with submodule support

## Installation

```bash
git clone --recurse-submodules https://github.com/Lemonical/VNO.Server.git
cd VNO.Server
dotnet restore VNO.Server.slnx
```

If you already cloned without submodules:

```bash
git submodule update --init --recursive
```

## Running

```bash
dotnet run --project src/VNO.Server/VNO.Server.csproj
```

The window gives you start/stop controls for the local game host, auth-server connection status, a live event log, an out of character chat monitor you can broadcast into, and the current connected-user list.

For the React Ink CLI, use `--cli`. For a non-interactive service/container, use
`--headless`; this prevents a TTY from accidentally selecting the Ink launcher.

## Configuration

Like the legacy server (the files Form3 let an operator edit), settings are read
from external files in a `data` folder next to the executable by
[`ServerSettingsLoader`](src/VNO.Server/Services/ServerSettingsLoader.cs), not from
an `appsettings.json`. Any key that is missing falls back to a built in default.

`data/init.ini`:

- `[Server] name`: display name sent to `Master`
- `[Server] port`: TCP port for player connections
- `[Server] public`: whether the server asks the master to list it publicly (`1`/`0`)
- `[Server] heartbeat`: retry and heartbeat interval for the master link
- `[Server] moderatorpassword`: in-game moderator password, blank disables it
- `[AS] host` / `[AS] port`: master/auth target

Other data files:

- `data/areas.ini`: one area per `[Section]`, the section name is the area shown to players
- `data/musiclist.txt`: available music tracks, one per line
- `data/charlist.txt`: authoritative roster, one character per line; a standard roster is created when absent
- `data/itemlist.txt`: authoritative item definitions, one per line

Headless deployments may override the data directory, server/auth host and port,
transport, TLS, public flag, credentials, and admin binding with the documented
`VNO_*` variables in `docker-compose.yml`. Passwords should use
`VNO_AUTH_PASSWORD_FILE`; the compatibility wire always carries canonical uppercase MD5.

## Ink Console

`clients/server-console` is a full React Ink frontend over the C# admin controller.
It provides live status/events, rich player inspection, kick/mute/ban/moderator
actions, notices, listener control, issue review, and roster/area/music editing.
The generated token is stored owner-only at `data/admin.token`.

```bash
cd clients/server-console
npm install
npm test
npm start -- --token-file ../../src/VNO.Server/bin/Debug/net10.0/data/admin.token
```

## Docker

Create `secrets/vno_auth_password.txt`, set `VNO_AUTH_USERNAME`, then start the
headless server. Run the full-screen console separately so Compose does not mix
daemon logs into Ink's alternate screen:

```bash
docker compose up --build -d server
docker compose --profile console run --rm console
```

Only the player port is published. The bearer-protected admin endpoint is reachable
only on the private Compose `admin` network, and its token volume is mounted read-only
into the console container.

## Testing

```bash
dotnet test VNO.Server.slnx
```

The test suite covers animator commands, area user lists, auth-link resilience, moderation flows, room locks, broadcast behavior, out of character monitoring, room policy and hide behavior, timers and staff lookup behavior.

## Related Repositories

- [`VNO.Core`](https://github.com/Lemonical/VNO.Core): shared protocol and transport layer
- [`VNO.Client`](https://github.com/Lemonical/VNO.Client): desktop player client

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for setup and pull request expectations.

Before opening a pull request:

```bash
git submodule update --init --recursive
dotnet test VNO.Server.slnx
```

If you change server-side message handling, add or update the matching tests.

## Support

Use the [GitHub issue tracker](https://github.com/Lemonical/VNO.Server/issues) for bugs, moderation workflow feedback and server-hosting questions.