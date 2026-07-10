import {EventEmitter} from 'node:events';
import WebSocket, {type ClientOptions, type RawData} from 'ws';
import type {
	AdminRequest,
	CompletionDescriptor,
	ServerFrame,
} from './types.js';

/** How long to wait for a completion answer before giving up */
const completionTimeoutMs = 2000;

type WebSocketOptions = ClientOptions;

export interface WebSocketLike {
	readyState: number;
	on(event: 'open', listener: () => void): this;
	on(event: 'message', listener: (data: RawData) => void): this;
	on(event: 'close', listener: (code: number, reason: Buffer) => void): this;
	on(
		event: 'unexpected-response',
		listener: (
			request: unknown,
			response: {
				statusCode?: number;
			},
		) => void,
	): this;
	on(event: 'error', listener: (error: Error) => void): this;
	send(data: string): void;
	close(): void;
	terminate?(): void;
}

type PendingCompletion = {
	resolve: (candidates: CompletionDescriptor[]) => void;
	timer: NodeJS.Timeout;
};

export type WebSocketFactory = (url: string, options: WebSocketOptions) => WebSocketLike;

/**
 * Typed events the connection raises
 */
export interface ConnectionEvents {
	/** The socket opened */
	open: () => void;
	/** A frame arrived, completions are handled internally and not raised here */
	frame: (frame: Exclude<ServerFrame, {type: 'completions'}>) => void;
	/** The server refused the upgrade, usually a bad token */
	rejected: (statusCode: number) => void;
	/** The socket closed */
	close: (code: number, reason: string) => void;
	/** A transport error occurred */
	error: (error: Error) => void;
}

/**
 * Wraps the websocket and the admin json protocol
 *
 * Outgoing requests carry a correlation id. Completion answers resolve the
 * matching pending promise, every other frame is raised as a frame event for the
 * ui to fold into its state
 */
export class AdminConnection extends EventEmitter {
	private socket?: WebSocketLike;
	private nextId = 1;
	private readonly pendingCompletions = new Map<number, PendingCompletion>();

	constructor(private readonly createSocket: WebSocketFactory = (url, options) => new WebSocket(url, options)) {
		super();
	}

	override on<K extends keyof ConnectionEvents>(
		event: K,
		listener: ConnectionEvents[K],
	): this {
		return super.on(event, listener);
	}

	override emit<K extends keyof ConnectionEvents>(
		event: K,
		...args: Parameters<ConnectionEvents[K]>
	): boolean {
		return super.emit(event, ...args);
	}

	/** Opens the connection and presents the token as a bearer header */
	connect(url: string, token: string): void {
		const socket = this.createSocket(url, {
			headers: {Authorization: `Bearer ${token}`},
		});
		this.socket = socket;

		socket.on('open', () => this.emit('open'));
		socket.on('message', data => this.handle(data.toString()));
		socket.on('close', (code, reason) => {
			this.flushPendingCompletions();
			this.emit('close', code, reason.toString());
		});
		socket.on('unexpected-response', (_request, response) => {
			this.flushPendingCompletions();
			this.emit('rejected', response.statusCode ?? 0);
			socket.terminate?.();
		});
		socket.on('error', error => this.emit('error', error as Error));
	}

	/** Sends a command line for execution, returns its correlation id */
	sendCommand(line: string): number {
		const id = this.nextId++;
		this.send({type: 'command', id, line});
		return id;
	}

	/** Requests completions for a partial line, resolves with the candidates */
	async complete(line: string): Promise<CompletionDescriptor[]> {
		const id = this.nextId++;
		return new Promise(resolve => {
			const timer = setTimeout(() => {
				const pending = this.pendingCompletions.get(id);
				if (!pending) {
					return;
				}

				this.pendingCompletions.delete(id);
				pending.resolve([]);
			}, completionTimeoutMs);

			this.pendingCompletions.set(id, {resolve, timer});
			this.send({type: 'complete', id, line});
		});
	}

	/** Closes the connection */
	close(): void {
		this.flushPendingCompletions();
		this.socket?.close();
	}

	private handle(text: string): void {
		let frame: ServerFrame;
		try {
			frame = JSON.parse(text) as ServerFrame;
		} catch {
			return;
		}

		if (frame.type === 'completions') {
			const pending = this.pendingCompletions.get(frame.requestId);
			if (pending) {
				this.pendingCompletions.delete(frame.requestId);
				clearTimeout(pending.timer);
				pending.resolve(frame.candidates);
			}

			return;
		}

		this.emit('frame', frame);
	}

	private flushPendingCompletions(): void {
		for (const [id, pending] of this.pendingCompletions) {
			clearTimeout(pending.timer);
			pending.resolve([]);
			this.pendingCompletions.delete(id);
		}
	}

	private send(request: AdminRequest): void {
		if (this.socket?.readyState === WebSocket.OPEN) {
			this.socket.send(JSON.stringify(request));
		}
	}
}
