// Small string helpers shared by the components

/** Pads a value with trailing spaces to a fixed width */
export function pad(value: string, width: number): string {
	return value.length >= width ? value : value + ' '.repeat(width - value.length);
}

/** Formats whole seconds as HH:MM:SS */
export function formatUptime(totalSeconds: number): string {
	const hours = Math.floor(totalSeconds / 3600);
	const minutes = Math.floor((totalSeconds % 3600) / 60);
	const seconds = Math.floor(totalSeconds % 60);
	const two = (n: number) => n.toString().padStart(2, '0');
	return `${two(hours)}:${two(minutes)}:${two(seconds)}`;
}

/** Splits a line into the settled prefix and the partial token being typed */
export function splitLastToken(line: string): {base: string; partial: string} {
	const lastSpace = line.lastIndexOf(' ');
	if (lastSpace < 0) {
		return {base: '', partial: line};
	}

	return {base: line.slice(0, lastSpace + 1), partial: line.slice(lastSpace + 1)};
}

/** Longest common prefix of a set of strings, case sensitive */
export function longestCommonPrefix(values: string[]): string {
	if (values.length === 0) {
		return '';
	}

	let prefix = values[0] ?? '';
	for (const value of values.slice(1)) {
		while (!value.startsWith(prefix)) {
			prefix = prefix.slice(0, -1);
			if (prefix === '') {
				return '';
			}
		}
	}

	return prefix;
}
