import {Box, Static, Text} from 'ink';
import type {TranscriptEntry} from '../transcript.js';
import {pad} from '../text.js';
import {glyph, theme} from '../theme.js';

type TableProps = {
	title: string;
	headers: string[];
	rows: string[][];
};

function TableBlock({title, headers, rows}: TableProps) {
	const widths = headers.map((header, column) =>
		Math.max(header.length, ...rows.map(row => (row[column] ?? '').length)),
	);
	const renderCells = (cells: string[]) =>
		headers.map((_, column) => pad(cells[column] ?? '', widths[column] ?? 0)).join('  ');

	return (
		<Box flexDirection="column" marginY={1}>
			<Text bold color={theme.heading}>
				{title}
			</Text>
			<Text color={theme.dim}>{renderCells(headers)}</Text>
			{rows.map((row, index) => (
				<Text key={index}>{renderCells(row)}</Text>
			))}
		</Box>
	);
}

function EntryView({entry}: {entry: TranscriptEntry}) {
	switch (entry.kind) {
		case 'line':
			return <Text>{entry.text}</Text>;
		case 'echo':
			return (
				<Text color={theme.accent}>
					{`${glyph.caret} ${entry.text.replace(/^>\s*/, '')}`}
				</Text>
			);
		case 'system':
			return <Text color={theme.warn}>{`${glyph.info} ${entry.text}`}</Text>;
		case 'error':
			return <Text color={theme.danger}>{`${glyph.cross} ${entry.text}`}</Text>;
		case 'table':
			return <TableBlock title={entry.title} headers={entry.headers} rows={entry.rows} />;
	}
}

/**
 * Renders the transcript as committed scrollback. Ink's Static prints each entry
 * once and never repaints it, so the live region below (status + prompt) stays
 * crisp and the console reads like a modern agent CLI rather than a redrawn log
 */
export function TranscriptView({entries}: {entries: TranscriptEntry[]}) {
	return (
		<Static items={entries}>
			{(entry, index) => (
				<Box key={index} flexDirection="column">
					<EntryView entry={entry} />
				</Box>
			)}
		</Static>
	);
}
