# VNO Server Console

React Ink terminal administration client for a headless `VNO.Server`. The C# process owns server state and exposes a token-protected WebSocket endpoint; attaching or detaching this UI does not stop the game host.

## Requirements and setup

- Node.js 18 or newer
- A successfully authenticated Server running with `--headless`
- Its generated/configured admin token

```bash
npm ci
npm test
npm run build
npm start -- --token-file ../../src/VNO.Server/bin/Debug/net10.0/data/admin.token
```

The default endpoint is `ws://127.0.0.1:6542/admin`. Token resolution order is `--token`, `SERVER_ADMIN_TOKEN`, then `--token-file`. Prefer a protected token file or environment secret; command-line tokens may be exposed through shell history or process inspection.

The console exposes live status and events, player inspection, moderation, bans, notices, listener control, issue review, and area/music/roster editing. Use `help` for commands; `quit`, `exit`, or Ctrl+C detaches without terminating Server.

From the repository root, the Docker console profile is:

```bash
docker compose --profile console run --rm console
```

See the repository [README](../../README.md) for the service quick start. The full command, option, protocol, and troubleshooting reference belongs in the VNO.Server GitHub wiki once it is enabled.
