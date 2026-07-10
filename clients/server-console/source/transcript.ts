// The transcript is an append only list of rendered entries, folded from server
// frames and a few client side notices. It is committed to terminal scrollback
// through Ink's Static, a clear frame resets it, see App

export type TranscriptEntry =
	| {kind: 'line'; text: string}
	| {kind: 'echo'; text: string}
	| {kind: 'system'; text: string}
	| {kind: 'error'; text: string}
	| {kind: 'table'; title: string; headers: string[]; rows: string[][]};
