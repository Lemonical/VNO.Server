import type {ServerFrame, StatusFrame} from './types.js';
import type {TranscriptEntry} from './transcript.js';

export type WelcomeState = {
	name: string;
	version: string;
	url: string;
};

export type ConsoleViewState = {
	entries: TranscriptEntry[];
	status?: StatusFrame;
	connected: boolean;
	busy: boolean;
	welcome?: WelcomeState;
	fatal?: string;
};

type VisibleFrame = Exclude<ServerFrame, {type: 'completions'}>;

export function createInitialState(): ConsoleViewState {
	return {
		entries: [],
		connected: false,
		busy: false,
	};
}

export function applyWelcome(
	state: ConsoleViewState,
	name: string,
	version: string,
	url: string,
): ConsoleViewState {
	return {
		...state,
		welcome: {
			name,
			version,
			url,
		},
	};
}

export function applyFrame(
	state: ConsoleViewState,
	frame: VisibleFrame,
	url: string,
): ConsoleViewState {
	switch (frame.type) {
		case 'welcome':
			return applyWelcome(state, frame.name, frame.protocolVersion, url);
		case 'line':
			return append(state, {kind: 'line', text: frame.text});
		case 'table':
			return append(state, {
				kind: 'table',
				title: frame.title,
				headers: frame.headers,
				rows: frame.rows,
			});
		case 'clear':
			return {
				...state,
				entries: [],
			};
		case 'status':
			return {
				...state,
				status: frame,
			};
		case 'commandCompleted':
			return {
				...state,
				busy: false,
			};
		case 'error':
			return append(
				{
					...state,
					busy: false,
				},
				{kind: 'error', text: frame.message},
			);
	}
}

export function markConnected(state: ConsoleViewState): ConsoleViewState {
	return {
		...state,
		connected: true,
	};
}

export function markSubmitted(state: ConsoleViewState, line: string): ConsoleViewState {
	return append(
		{
			...state,
			busy: true,
		},
		{kind: 'echo', text: `> ${line}`},
	);
}

export function markClosed(
	state: ConsoleViewState,
	intentional: boolean,
): ConsoleViewState {
	const disconnected = {
		...state,
		connected: false,
		busy: false,
	};

	return intentional
		? disconnected
		: append(disconnected, {kind: 'system', text: 'Disconnected from the server'});
}

export function markRejected(
	state: ConsoleViewState,
	statusCode: number,
): ConsoleViewState {
	return {
		...state,
		busy: false,
		fatal:
			statusCode === 401
				? 'Authentication failed, check the admin token'
				: `The server refused the connection (status ${statusCode})`,
	};
}

export function markTransportError(
	state: ConsoleViewState,
	url: string,
	message: string,
): ConsoleViewState {
	if (!state.connected) {
		return {
			...state,
			busy: false,
			fatal: `Could not reach the server at ${url}: ${message}`,
		};
	}

	return append(
		{
			...state,
			busy: false,
		},
		{kind: 'error', text: `Connection error: ${message}`},
	);
}

function append(state: ConsoleViewState, entry: TranscriptEntry): ConsoleViewState {
	return {
		...state,
		entries: [...state.entries, entry],
	};
}
