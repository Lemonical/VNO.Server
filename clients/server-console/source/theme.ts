// The single source of visual truth for the console, so every component pulls the
// same palette and glyphs and the look stays cohesive

/** Named or hex colors understood by Ink (rendered through chalk) */
export const theme = {
	// the clay accent that ties the console together, used for the name, caret,
	// echoed commands, and active suggestions
	accent: '#f08a5d',
	// secondary text, separators, and inactive hints
	dim: '#8b93a6',
	// helper text that should stay present without competing with content
	subtle: '#6b7280',
	// thin borders and panel edges
	edge: '#384152',
	// selected command rows in the palette
	selection: '#273246',
	// selected row text over the palette
	selectionText: '#f8fafc',
	// healthy state and success replies
	success: '#4ade80',
	// client side notices
	warn: '#fbbf24',
	// errors from the server or the transport
	danger: '#f87171',
	// table titles and other structural headings
	heading: '#57c7ff',
} as const;

/** Glyphs used across the console */
export const glyph = {
	// separator between status fields
	bullet: '·',
	// connection indicator, filled when attached
	dotOn: '●',
	dotOff: '○',
	// the input caret
	caret: '›',
	// prefixes for informational transcript lines
	info: 'i',
	cross: 'x',
} as const;

// Braille spinner frames, the same family used by most modern CLIs
export const spinnerFrames = ['⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏'] as const;
