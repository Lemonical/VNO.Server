import {useCallback, useEffect, useMemo, useRef, useState} from 'react';
import {Box, Text, useInput} from 'ink';
import type {CompletionDescriptor} from '../types.js';
import {pad, splitLastToken} from '../text.js';
import {glyph, theme} from '../theme.js';

type Props = {
	disabled: boolean;
	onSubmit: (line: string) => void;
	onExit: () => void;
	requestCompletions: (line: string) => Promise<CompletionDescriptor[]>;
};

const suggestionLimit = 6;

/**
 * The command composer, with live remote suggestions, lightweight history, and
 * keyboard-first navigation
 */
export function Prompt({disabled, onSubmit, onExit, requestCompletions}: Props) {
	const [value, setValue] = useState('');
	const [history, setHistory] = useState<string[]>([]);
	const [historyIndex, setHistoryIndex] = useState<number | null>(null);
	const [suggestions, setSuggestions] = useState<CompletionDescriptor[]>([]);
	const [suggestIndex, setSuggestIndex] = useState(0);
	const [hiddenSuggestionCount, setHiddenSuggestionCount] = useState(0);
	const [anchorBase, setAnchorBase] = useState('');
	const requestSequenceRef = useRef(0);
	const suppressAutoSuggestRef = useRef(false);

	const clearSuggestions = useCallback(() => {
		requestSequenceRef.current += 1;
		setSuggestions([]);
		setSuggestIndex(0);
		setHiddenSuggestionCount(0);
		setAnchorBase('');
	}, []);

	const publishSuggestions = useCallback((base: string, matches: CompletionDescriptor[]) => {
		setAnchorBase(base);
		setSuggestions(matches.slice(0, suggestionLimit));
		setSuggestIndex(0);
		setHiddenSuggestionCount(Math.max(0, matches.length - suggestionLimit));
	}, []);

	const fetchMatches = useCallback(
		async (line: string) => {
			const candidates = await requestCompletions(line);
			const {base, partial} = splitLastToken(line);
			const matches = candidates.filter(candidate =>
				candidate.value.toLowerCase().startsWith(partial.toLowerCase()),
			);
			return {base, matches};
		},
		[requestCompletions],
	);

	const replaceValue = useCallback((next: string) => {
		suppressAutoSuggestRef.current = true;
		setValue(next);
	}, []);

	const applySuggestion = useCallback(
		(base: string, candidate: CompletionDescriptor) => {
			replaceValue(base + candidate.value + ' ');
			setHistoryIndex(null);
			clearSuggestions();
		},
		[clearSuggestions, replaceValue],
	);

	const openSuggestions = useCallback(async () => {
		const sequence = ++requestSequenceRef.current;
		const {base, matches} = await fetchMatches(value);
		if (sequence !== requestSequenceRef.current) {
			return false;
		}

		if (matches.length === 0) {
			clearSuggestions();
			return false;
		}

		if (matches.length === 1) {
			applySuggestion(base, matches[0]!);
			return true;
		}

		publishSuggestions(base, matches);
		return true;
	}, [applySuggestion, clearSuggestions, fetchMatches, publishSuggestions, value]);

	useEffect(() => {
		if (disabled) {
			clearSuggestions();
			return;
		}

		if (suppressAutoSuggestRef.current) {
			suppressAutoSuggestRef.current = false;
			return;
		}

		if (value.trim().length === 0) {
			clearSuggestions();
			return;
		}

		const sequence = ++requestSequenceRef.current;
		const timer = setTimeout(() => {
			void fetchMatches(value)
				.then(({base, matches}) => {
					if (sequence !== requestSequenceRef.current) {
						return;
					}

					if (matches.length === 0) {
						clearSuggestions();
						return;
					}

					publishSuggestions(base, matches);
				})
				.catch(() => {
					if (sequence === requestSequenceRef.current) {
						clearSuggestions();
					}
				});
		}, 80);

		return () => clearTimeout(timer);
	}, [clearSuggestions, disabled, fetchMatches, publishSuggestions, value]);

	const submit = () => {
		const line = value.trim();
		setValue('');
		clearSuggestions();
		setHistoryIndex(null);
		if (line.length > 0) {
			setHistory(previous => [...previous, line]);
			onSubmit(line);
		}
	};

	const historyPrevious = () => {
		if (history.length === 0) {
			return;
		}

		const index = historyIndex === null ? history.length - 1 : Math.max(0, historyIndex - 1);
		setHistoryIndex(index);
		replaceValue(history[index] ?? '');
		clearSuggestions();
	};

	const historyNext = () => {
		if (historyIndex === null) {
			return;
		}

		const index = historyIndex + 1;
		if (index >= history.length) {
			setHistoryIndex(null);
			replaceValue('');
		} else {
			setHistoryIndex(index);
			replaceValue(history[index] ?? '');
		}

		clearSuggestions();
	};

	const valueColumnWidth = useMemo(() => {
		const widest = Math.max(12, ...suggestions.map(suggestion => suggestion.value.length));
		return Math.min(20, widest);
	}, [suggestions]);

	useInput((input, key) => {
		if (key.ctrl && input === 'c') {
			onExit();
			return;
		}

		if (disabled) {
			return;
		}

		if (key.ctrl && input === 'p') {
			historyPrevious();
			return;
		}

		if (key.ctrl && input === 'n') {
			historyNext();
			return;
		}

		if (key.return) {
			submit();
			return;
		}

		if (key.tab) {
			if (suggestions.length > 0) {
				const candidate = suggestions[suggestIndex];
				if (candidate) {
					applySuggestion(anchorBase, candidate);
				}
				return;
			}

			void openSuggestions();
			return;
		}

		if (key.upArrow) {
			if (suggestions.length > 0) {
				setSuggestIndex(previous =>
					previous === 0 ? suggestions.length - 1 : previous - 1,
				);
				return;
			}

			historyPrevious();
			return;
		}

		if (key.downArrow) {
			if (suggestions.length > 0) {
				setSuggestIndex(previous => (previous + 1) % suggestions.length);
				return;
			}

			if (value.trim().length > 0) {
				void openSuggestions();
				return;
			}

			historyNext();
			return;
		}

		if (key.backspace || key.delete) {
			setHistoryIndex(null);
			setValue(previous => previous.slice(0, -1));
			return;
		}

		if (key.escape) {
			if (suggestions.length > 0) {
				clearSuggestions();
				return;
			}

			setValue('');
			return;
		}

		// Ignore control chords, append anything printable
		if (input && !key.ctrl && !key.meta) {
			setHistoryIndex(null);
			setValue(previous => previous + input);
		}
	});

	return (
		<Box flexDirection="column">
			{suggestions.length > 0 && (
				<Box
					width="100%"
					flexDirection="column"
					borderStyle="round"
					borderColor={theme.edge}
					paddingX={1}
					marginBottom={1}
				>
					<Text color={theme.subtle}>
						{`Suggestions  ${glyph.bullet}  Tab accepts  ${glyph.bullet}  Esc dismisses`}
					</Text>
					{suggestions.map((suggestion, index) => {
						const active = index === suggestIndex;
						const background = active ? theme.selection : undefined;
						const valueColor = active ? theme.selectionText : theme.accent;
						const descriptionColor = active ? theme.selectionText : theme.dim;

						return (
							<Box key={`${suggestion.value}:${index}`} marginTop={1}>
								<Text color={valueColor} backgroundColor={background}>
									{pad(suggestion.value, valueColumnWidth)}
								</Text>
								<Text
									color={descriptionColor}
									backgroundColor={background}
								>{`  ${suggestion.description}`}</Text>
							</Box>
						);
					})}
					{hiddenSuggestionCount > 0 && (
						<Box marginTop={1}>
							<Text color={theme.subtle}>{`+${hiddenSuggestionCount} more matches`}</Text>
						</Box>
					)}
				</Box>
			)}
			<Box
				width="100%"
				borderStyle="single"
				borderColor={disabled ? theme.edge : theme.accent}
				paddingX={1}
			>
				<Text color={disabled ? theme.dim : theme.accent}>{`${glyph.caret} `}</Text>
				{value.length === 0 ? (
					<Text color={theme.dim}>Type a command</Text>
				) : (
					<>
						<Text>{value}</Text>
						<Text inverse> </Text>
					</>
				)}
			</Box>
			<Box paddingX={1} marginTop={1}>
				<Text color={theme.subtle}>
					{disabled
						? 'Waiting for the server handshake…'
						: suggestions.length > 0
							? `↑↓ move  ${glyph.bullet}  Tab accept  ${glyph.bullet}  Esc dismiss  ${glyph.bullet}  Ctrl+P/N history`
							: `Type to search commands  ${glyph.bullet}  ↓ suggestions  ${glyph.bullet}  Tab complete  ${glyph.bullet}  Ctrl+P/N history`}
				</Text>
			</Box>
		</Box>
	);
}
