import {useEffect, useState} from 'react';
import {spinnerFrames} from './theme.js';

// Cycles the braille spinner frames only while active, so an idle console does no
// timer work. Implemented in house to avoid pulling in ink-spinner
const intervalMs = 80;

/** Returns the current spinner frame, advancing while active */
export function useSpinner(active: boolean): string {
	const [frame, setFrame] = useState(0);

	useEffect(() => {
		if (!active) {
			setFrame(0);
			return;
		}

		const timer = setInterval(() => {
			setFrame(previous => (previous + 1) % spinnerFrames.length);
		}, intervalMs);
		return () => clearInterval(timer);
	}, [active]);

	return spinnerFrames[frame] ?? spinnerFrames[0]!;
}
