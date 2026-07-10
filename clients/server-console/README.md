# vno-server-console

A React Ink administration console for `VNO.Server`. The C# process owns all
server state and exposes a token-authenticated WebSocket endpoint; this Node
client is only the terminal UI, so detaching it does not stop the game server.

## Development

Requirements: Node.js 18 or newer and a running `VNO.Server --cli` process.

```sh
npm install
npm test
npm start -- --token-file ../../src/VNO.Server/bin/Debug/net10.0/data/admin.token
```

The default endpoint is `ws://127.0.0.1:6542/admin`. Token resolution order is
`--token`, `SERVER_ADMIN_TOKEN`, then `--token-file`. Use `--url` to override
the complete endpoint or `--host` and `--port` for its address.

The console supports live status and event updates plus player inspection,
kick/mute/ban/moderator actions, notices, listener start/stop, issue review,
and area, music, and character roster editing. `quit`, `exit`, or Ctrl+C
detaches the UI without terminating the C# server.

## Docker

From the VNO.Server repository root:

```sh
docker compose --profile console run --rm console
```

The console and server share the generated token through a read-only named
volume. Port 6542 is only reachable on Compose's private `admin` network and is
not published to the host.
