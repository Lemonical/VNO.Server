// Wire protocol mirrored from the C# ServerAdminEndpoint.
// The shapes are camelCased to match the C# web JSON defaults.

/** Metadata about one admin command, sent in the welcome frame */
export interface CommandDescriptor {
	name: string;
	aliases: string[];
	summary: string;
	usage: string;
}

/** One autocomplete suggestion for a partially typed line */
export interface CompletionDescriptor {
	value: string;
	description: string;
}

export interface WelcomeFrame {
	type: 'welcome';
	name: string;
	protocolVersion: string;
	commands: CommandDescriptor[];
}

export interface LineFrame {
	type: 'line';
	text: string;
}

export interface TableFrame {
	type: 'table';
	title: string;
	headers: string[];
	rows: string[][];
}

export interface ClearFrame {
	type: 'clear';
}

export interface StatusFrame {
	type: 'status';
	name: string;
	port: number;
	running: boolean;
	sessions: number;
	servers: number;
	uptimeSeconds: number;
}

export interface CompletionsFrame {
	type: 'completions';
	requestId: number;
	candidates: CompletionDescriptor[];
}

export interface CommandCompletedFrame {
	type: 'commandCompleted';
	requestId: number;
	sessionEnded: boolean;
}

export interface ErrorFrame {
	type: 'error';
	requestId?: number;
	message: string;
}

/** Every frame the server can push to the client */
export type ServerFrame =
	| WelcomeFrame
	| LineFrame
	| TableFrame
	| ClearFrame
	| StatusFrame
	| CompletionsFrame
	| CommandCompletedFrame
	| ErrorFrame;

/** A request the client sends to the server */
export interface AdminRequest {
	type: 'command' | 'complete';
	id: number;
	line: string;
}
