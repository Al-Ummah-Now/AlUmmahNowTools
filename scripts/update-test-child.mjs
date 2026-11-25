#!/usr/bin/env node
// update-test-child.mjs <step|scanner> <beat file>: a child the app started, for the update test. Writes its pid and the time
// to the file ten times a second, and ends only when it is killed. The scanner answers the app's health check.
import { createServer } from 'node:http';
import { renameSync, writeFileSync } from 'node:fs';

const [kind, beat] = process.argv.slice(2);
const write = () => {
	writeFileSync(`${beat}.tmp`, JSON.stringify({ pid: process.pid, at: Date.now() }));
	try {
		renameSync(`${beat}.tmp`, beat);
	} catch {
		// Being read
	}
};
write();
setInterval(write, 100);
process.on('SIGTERM', () => {});
process.on('SIGINT', () => {});
process.stdin.on('error', () => {}).resume();
if (kind === 'scanner') createServer((_, res) => res.end('ok')).listen(47923);
