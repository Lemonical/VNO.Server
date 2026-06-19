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
- Timer handling
- Staff lookup behaviour
- Configurable server name, port, visibility, areas, music and characters

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

The window gives you start/stop controls for the local game host, auth-server connection status, a live event log and the current connected-user list.

## Configuration

The active defaults live in [`src/VNO.Server/appsettings.json`](src/VNO.Server/appsettings.json) under the `Server` section.

Key settings:

- `Server:Name`: display name sent to `Master`
- `Server:ListenPort`: TCP port for player connections
- `Server:IsPublic`: whether the server asks the master to list it publicly
- `Server:AuthServerHost` / `Server:AuthServerPort`: master/auth target
- `Server:HeartbeatSeconds`: retry and heartbeat interval for the master link
- `Server:Areas`: list of area names sent to players on join
- `Server:Music`: available music tracks
- `Server:Characters`: optional roster override; empty means clients use their local roster
- `Server:ModeratorPassword`: in-game moderator password

This app currently reads configuration from `appsettings.json` only. Environment-variable overrides and legacy `.ini` file imports are not implemented yet.

## Testing

```bash
dotnet test VNO.Server.slnx
```

The test suite covers animator commands, area user lists, auth-link resilience, moderation flows, room locks, broadcast behavior, timers and staff lookup behavior.

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