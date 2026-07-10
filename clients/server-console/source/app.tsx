import {useCallback, useEffect, useRef, useState} from 'react';
import {Box, Text, useApp, useStdout} from 'ink';
import {AdminConnection} from './connection.js';
import type {ServerFrame} from './types.js';
import {ShellHeader} from './components/ShellHeader.js';
import {StatusBar} from './components/StatusBar.js';
import {TranscriptView} from './components/TranscriptView.js';
import {Prompt} from './components/Prompt.js';
import {
	applyFrame,
	createInitialState,
	markClosed,
	markConnected,
	markRejected,
	markSubmitted,
	markTransportError,
	type ConsoleViewState,
} from './session-state.js';

type Props = {
	url: string;
	token: string;
	name: string;
};

// quitting the remote console detaches it, the server keeps serving, so these are
// handled locally and never reach the server
const localQuitWords = new Set(['quit', 'exit', 'q']);

/**
 * The console application, owns the connection and folds frames into ui state
 */
export function App({url, token, name}: Props) {
	const {exit} = useApp();
	const {stdout} = useStdout();
	const connectionRef = useRef<AdminConnection | null>(null);
	const intentionalCloseRef = useRef(false);
	const [state, setState] = useState<ConsoleViewState>(createInitialState);
	const [viewportHeight, setViewportHeight] = useState<number | undefined>(() =>
		stdout.isTTY ? (stdout.rows ?? 24) : undefined,
	);

	useEffect(() => {
		if (!stdout.isTTY) {
			return;
		}

		const handleResize = () => setViewportHeight(stdout.rows ?? 24);
		handleResize();
		stdout.on('resize', handleResize);
		return () => {
			stdout.off('resize', handleResize);
		};
	}, [stdout]);

	useEffect(() => {
		const connection = new AdminConnection();
		connectionRef.current = connection;

		const handleFrame = (frame: Exclude<ServerFrame, {type: 'completions'}>) => {
			if (frame.type === 'clear') {
				// Keep the active console surface clean, including remote clear
				// requests that arrive after the alternate screen is mounted
				stdout.write(stdout.isTTY ? '\u001b[2J\u001b[H' : '\u001b[2J\u001b[3J\u001b[H');
			}

			setState(previous => applyFrame(previous, frame, url));
			switch (frame.type) {
				case 'commandCompleted':
					if (frame.sessionEnded) {
						intentionalCloseRef.current = true;
						connection.close();
						exit();
					}
					break;
			}
		};

		connection.on('open', () => {
			intentionalCloseRef.current = false;
			setState(markConnected);
		});
		connection.on('frame', handleFrame);
		connection.on('rejected', code => setState(previous => markRejected(previous, code)));
		connection.on('close', () =>
			setState(previous => markClosed(previous, intentionalCloseRef.current)),
		);
		connection.on('error', error =>
			setState(previous => markTransportError(previous, url, error.message)),
		);

		connection.connect(url, token);
		return () => connection.close();
		// connect once for the life of the app
		// eslint-disable-next-line react-hooks/exhaustive-deps
	}, []);

	// leave the alternate state cleanly once a fatal error is shown
	useEffect(() => {
		if (!state.fatal) {
			return;
		}

		const timer = setTimeout(exit, 50);
		return () => clearTimeout(timer);
	}, [state.fatal, exit]);

	const submit = useCallback(
		(line: string) => {
			setState(previous => markSubmitted(previous, line));
			if (localQuitWords.has(line.trim().toLowerCase())) {
				intentionalCloseRef.current = true;
				connectionRef.current?.close();
				exit();
				return;
			}

			connectionRef.current?.sendCommand(line);
		},
		[exit],
	);

	const requestCompletions = useCallback(
		(line: string) => connectionRef.current?.complete(line) ?? Promise.resolve([]),
		[],
	);

	const onExit = useCallback(() => {
		intentionalCloseRef.current = true;
		connectionRef.current?.close();
		exit();
	}, [exit]);

	if (state.fatal) {
		return (
			<Box>
				<Text color="red">{state.fatal}</Text>
			</Box>
		);
	}

	return (
		<Box
			flexDirection="column"
			paddingX={2}
			paddingY={1}
			height={viewportHeight}
			justifyContent="space-between"
		>
			<Box flexDirection="column" flexGrow={1}>
				<ShellHeader connected={state.connected} name={name} url={url} welcome={state.welcome} />
				<TranscriptView entries={state.entries} />
			</Box>
			<Box marginTop={1} flexDirection="column">
				<StatusBar
					status={state.status}
					name={name}
					connected={state.connected}
					busy={state.busy}
				/>
				<Prompt
					disabled={!state.connected}
					onSubmit={submit}
					onExit={onExit}
					requestCompletions={requestCompletions}
				/>
			</Box>
		</Box>
	);
}
