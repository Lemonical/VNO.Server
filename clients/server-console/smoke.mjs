import {readFileSync} from 'node:fs';
import {AdminConnection} from './dist/connection.js';

const token = readFileSync(process.argv[2], 'utf8').trim();
const url = 'ws://127.0.0.1:6544/';
const c = new AdminConnection();
const seen = [];

c.on('open', () => console.log('open'));
c.on('rejected', code => {
	console.log('REJECTED', code);
	process.exit(1);
});
c.on('error', e => console.log('error', e.message));
c.on('frame', f => {
	seen.push(f.type);
	if (f.type === 'welcome') console.log('welcome:', f.name, 'proto', f.protocolVersion, '-', f.commands.length, 'commands');
	if (f.type === 'status') {/* noisy, skip */}
	if (f.type === 'line') console.log('line:', f.text);
	if (f.type === 'table') console.log('table:', f.title, '-', f.rows.length, 'rows');
	if (f.type === 'commandCompleted') console.log('completed id', f.requestId, 'sessionEnded', f.sessionEnded);
});

c.connect(url, token);

setTimeout(async () => {
	const cands = await c.complete('user ');
	console.log('complete "user " ->', cands.map(x => x.value).join(','));
	c.sendCommand('status');
}, 500);

setTimeout(() => c.sendCommand('quit'), 1200);
setTimeout(() => {
	console.log('frames seen:', [...new Set(seen)].join(','));
	process.exit(0);
}, 2000);
