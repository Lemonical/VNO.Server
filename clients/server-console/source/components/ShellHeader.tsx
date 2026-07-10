import {Box, Text} from 'ink';
import type {WelcomeState} from '../session-state.js';
import {glyph, theme} from '../theme.js';

type Props = {
	connected: boolean;
	name: string;
	url: string;
	welcome?: WelcomeState;
};

/**
 * A calm shell header that frames the session without flooding first boot with
 * command lists or tutorial text
 */
export function ShellHeader({connected, name, url, welcome}: Props) {
	const title = welcome?.name ?? name;
	const subtitle = welcome
		? `admin console  ${glyph.bullet}  protocol ${welcome.version}`
		: 'admin console  ·  establishing session';
	const endpoint = welcome?.url ?? url;
	const guidance = connected
		? 'Type to search commands. Tab accepts. ↑↓ browse suggestions. Ctrl+C detaches.'
		: 'Connecting to the server…';

	return (
		<Box flexDirection="column" marginBottom={1}>
			<Text>
				<Text bold color={theme.heading}>
					{title}
				</Text>
				<Text color={theme.dim}>{`  ${subtitle}`}</Text>
			</Text>
			<Text color={theme.dim}>{endpoint}</Text>
			<Box marginTop={1}>
				<Text color={theme.subtle}>{guidance}</Text>
			</Box>
		</Box>
	);
}
