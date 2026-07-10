import type {ReactNode} from 'react';
import {Box, Text} from 'ink';
import type {StatusFrame} from '../types.js';
import {formatUptime} from '../text.js';
import {theme, glyph} from '../theme.js';
import {useSpinner} from '../useSpinner.js';

type Props = {
	status?: StatusFrame;
	name: string;
	connected: boolean;
	busy: boolean;
};

/** One field of the footer, carries its own leading separator so chips wrap cleanly */
function Field({children}: {children: ReactNode}) {
	return (
		<Text color={theme.subtle}>
			{`  ${glyph.bullet}  `}
			{children}
		</Text>
	);
}

/**
 * A lightweight live status row that keeps runtime state visible without
 * competing with the command surface
 */
export function StatusBar({status, name, connected, busy}: Props) {
	const spinner = useSpinner(busy);
	const label = status?.name ?? name;
	const online = connected && (status?.running ?? false);
	const state = !connected ? 'connecting' : status?.running ? 'online' : 'attached';
	const uptime = status ? formatUptime(status.uptimeSeconds) : '--:--:--';

	return (
		<Box width="100%" paddingX={1} flexWrap="wrap" marginBottom={1}>
			<Text color={online ? theme.success : theme.dim}>{online ? glyph.dotOn : glyph.dotOff}</Text>
			<Text color={theme.heading}>{` ${label}`}</Text>
			<Field>{state}</Field>
			<Field>{`port ${status?.port ?? '---'}`}</Field>
			<Field>{`players ${status?.sessions ?? 0}`}</Field>
			<Field>{`peak ${status?.servers ?? 0}`}</Field>
			<Field>{`up ${uptime}`}</Field>
			{busy && (
				<Text color={theme.subtle}>
					{`  ${glyph.bullet}  `}
					<Text color={theme.accent}>{spinner}</Text>
					{' running'}
				</Text>
			)}
		</Box>
	);
}
