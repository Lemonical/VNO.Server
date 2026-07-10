import {EventEmitter} from 'node:events';
import {setTimeout as delay} from 'node:timers/promises';
import {describe, expect, it} from 'vitest';
import {
	AdminConnection,
	type WebSocketFactory,
	type WebSocketLike,
} from '../source/connection.js';

class FakeSocket extends EventEmitter implements WebSocketLike {
	readyState = 1;
	sent: string[] = [];
	closed = false;
	terminated = false;

	send(data: string): void {
		this.sent.push(data);
	}

	close(): void {
		this.closed = true;
	}

	terminate(): void {
		this.terminated = true;
	}
}

describe('AdminConnection', () => {
	it('sends the bearer token when connecting', () => {
		let recorded:
			| {
					url: string;
					headers?: Record<string, string>;
			  }
			| undefined;
		const factory: WebSocketFactory = (url, options) => {
			recorded = {
				url,
				headers: options?.headers as Record<string, string> | undefined,
			};
			return new FakeSocket();
		};

		const connection = new AdminConnection(factory);
		connection.connect('ws://127.0.0.1:6544/', 'secret');

		expect(recorded).toEqual({
			url: 'ws://127.0.0.1:6544/',
			headers: {Authorization: 'Bearer secret'},
		});
	});

	it('resolves completions from the matching response', async () => {
		const socket = new FakeSocket();
		const connection = new AdminConnection(() => socket);
		connection.connect('ws://127.0.0.1:6544/', 'secret');

		const pending = connection.complete('st');
		expect(socket.sent).toHaveLength(1);

		socket.emit(
			'message',
			Buffer.from(
				JSON.stringify({
					type: 'completions',
					requestId: 1,
					candidates: [{value: 'status', description: 'summary'}],
				}),
			),
		);

		await expect(pending).resolves.toEqual([{value: 'status', description: 'summary'}]);
	});

	it('flushes pending completions when the socket closes', async () => {
		const socket = new FakeSocket();
		const connection = new AdminConnection(() => socket);
		connection.connect('ws://127.0.0.1:6544/', 'secret');

		const pending = connection.complete('st');
		socket.emit('close', 1006, Buffer.from('bye'));

		await expect(pending).resolves.toEqual([]);
	});

	it('raises rejected when the server refuses the upgrade', async () => {
		const socket = new FakeSocket();
		const connection = new AdminConnection(() => socket);
		const statuses: number[] = [];
		connection.on('rejected', code => statuses.push(code));
		connection.connect('ws://127.0.0.1:6544/', 'secret');

		socket.emit('unexpected-response', {}, {statusCode: 401});
		await delay(0);

		expect(statuses).toEqual([401]);
		expect(socket.terminated).toBe(true);
	});
});
