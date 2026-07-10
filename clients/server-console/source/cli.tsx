#!/usr/bin/env node
import {readFileSync} from 'node:fs';
import process from 'node:process';
import {render} from 'ink';
import meow from 'meow';
import {App} from './app.js';

const cli = meow(
	`
  Usage
    $ vno-server-console [options]

  Options
    --host        Admin endpoint host (default 127.0.0.1)
    --port        Admin endpoint port (default 6542)
    --url         Full websocket url, overrides --host and --port
    --token       Admin token, overrides the env var and token file
    --token-file  Path to the server admin.token file
    --name        Label shown until the first status arrives

  Token resolution order
    --token, then SERVER_ADMIN_TOKEN, then --token-file

  Examples
    $ vno-server-console --token-file ../../src/VNO.Server/bin/Debug/net10.0/data/admin.token
    $ SERVER_ADMIN_TOKEN=secret vno-server-console --port 6542
`,
	{
		importMeta: import.meta,
		flags: {
			host: {type: 'string', default: '127.0.0.1'},
			port: {type: 'number', default: 6542},
			url: {type: 'string'},
			token: {type: 'string'},
			tokenFile: {type: 'string'},
			name: {type: 'string', default: 'VNO Server'},
		},
	},
);

const tokenFileWaitMilliseconds = 10_000;
const tokenFilePollMilliseconds = 100;

async function readTokenFileWhenReady(path: string): Promise<string | undefined> {
	const deadline = Date.now() + tokenFileWaitMilliseconds;
	do {
		try {
			const token = readFileSync(path, 'utf8').trim();
			if (token.length > 0) {
				return token;
			}
		} catch {
			// The daemon may still be creating its persistent token file.
		}

		if (Date.now() >= deadline) {
			return undefined;
		}

		await new Promise(resolve => setTimeout(resolve, tokenFilePollMilliseconds));
	} while (true);
}

async function resolveToken(): Promise<string | undefined> {
	if (cli.flags.token) {
		return cli.flags.token.trim();
	}

	const fromEnv = process.env['SERVER_ADMIN_TOKEN'];
	if (fromEnv) {
		return fromEnv.trim();
	}

	if (cli.flags.tokenFile) {
		return readTokenFileWhenReady(cli.flags.tokenFile);
	}

	return undefined;
}

const token = await resolveToken();
if (!token) {
	console.error(
		'No admin token found. Pass --token, set SERVER_ADMIN_TOKEN, or pass --token-file.',
	);
	process.exit(1);
}

const url = cli.flags.url ?? `ws://${cli.flags.host}:${cli.flags.port}/admin`;

const useAlternateScreen = process.stdout.isTTY === true;
if (useAlternateScreen) {
	process.stdout.write('\u001b[?1049h\u001b[2J\u001b[H');
}

const instance = render(<App url={url} token={token} name={cli.flags.name} />);

if (useAlternateScreen) {
	let restored = false;
	const restoreScreen = () => {
		if (restored) {
			return;
		}

		restored = true;
		process.stdout.write('\u001b[?1049l');
	};

	process.once('exit', restoreScreen);
	void instance.waitUntilExit().finally(restoreScreen);
}
