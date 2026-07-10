import {describe, expect, it} from 'vitest';
import {
	applyFrame,
	createInitialState,
	markClosed,
	markConnected,
	markRejected,
	markSubmitted,
	markTransportError,
} from '../source/session-state.js';
import type {CommandCompletedFrame, ErrorFrame, WelcomeFrame} from '../source/types.js';

describe('session state', () => {
	it('treats a transport error before open as fatal', () => {
		const state = markTransportError(createInitialState(), 'ws://127.0.0.1:6544/', 'ECONNREFUSED');

		expect(state.fatal).toContain('Could not reach the server');
		expect(state.busy).toBe(false);
	});

	it('treats a transport error after connect as transcript output', () => {
		const connected = markConnected(createInitialState());
		const state = markTransportError(connected, 'ws://127.0.0.1:6544/', 'socket hang up');

		expect(state.fatal).toBeUndefined();
		expect(state.entries).toEqual([{kind: 'error', text: 'Connection error: socket hang up'}]);
	});

	it('suppresses the disconnect notice for intentional closes', () => {
		const state = markClosed(markConnected(createInitialState()), true);

		expect(state.connected).toBe(false);
		expect(state.entries).toEqual([]);
	});

	it('appends a disconnect notice for unexpected closes', () => {
		const state = markClosed(markConnected(createInitialState()), false);

		expect(state.entries).toEqual([{kind: 'system', text: 'Disconnected from the server'}]);
		expect(state.busy).toBe(false);
	});

	it('records welcome frames and command completion', () => {
		const welcome: WelcomeFrame = {
			type: 'welcome',
			name: 'VNO Server',
			protocolVersion: '1.0',
			commands: [{name: 'status', aliases: [], summary: 'show', usage: 'status'}],
		};
		const started = markSubmitted(createInitialState(), 'status');
		const afterWelcome = applyFrame(started, welcome, 'ws://127.0.0.1:6544/');
		const completed: CommandCompletedFrame = {
			type: 'commandCompleted',
			requestId: 1,
			sessionEnded: false,
		};

		const state = applyFrame(afterWelcome, completed, 'ws://127.0.0.1:6544/');

		expect(state.entries).toEqual([{kind: 'echo', text: '> status'}]);
		expect(state.welcome).toEqual({
			name: 'VNO Server',
			version: '1.0',
			url: 'ws://127.0.0.1:6544/',
		});
		expect(state.busy).toBe(false);
	});

	it('converts protocol errors into transcript entries', () => {
		const frame: ErrorFrame = {
			type: 'error',
			message: 'Bad command',
		};

		const state = applyFrame(markSubmitted(createInitialState(), 'bogus'), frame, 'ws://127.0.0.1:6544/');

		expect(state.entries.at(-1)).toEqual({kind: 'error', text: 'Bad command'});
		expect(state.busy).toBe(false);
	});

	it('maps rejected upgrades to a fatal message', () => {
		const unauthorized = markRejected(createInitialState(), 401);
		const other = markRejected(createInitialState(), 503);

		expect(unauthorized.fatal).toBe('Authentication failed, check the admin token');
		expect(other.fatal).toBe('The server refused the connection (status 503)');
	});
});
