#!/usr/bin/env node
/**
 * Every name given must lead nowhere on this machine: the system's own answer for it (its hosts file) is 0.0.0.0 or :: and
 * nothing else. Names are only looked up; nothing connects to them. Usage: names-blocked.mjs <host>...
 */
import { lookup } from 'node:dns/promises';

const hosts = process.argv.slice(2);
if (hosts.length === 0) throw new Error('no host given');
let open = 0;
for (const host of hosts) {
	// A name without an answer is no proof either: the hosts file has one for each
	const found = await lookup(host, { all: true }).then((all) => all.map((a) => a.address), (e) => [e.code]);
	const blocked = found.length > 0 && found.every((a) => a === '0.0.0.0' || a === '::');
	console.log(`${blocked ? 'blocked' : 'NOT BLOCKED'} ${host}: ${found.join(', ')}`);
	if (!blocked) open++;
}
if (open > 0) {
	console.error(`::error::${open} of ${hosts.length} production names are not blocked`);
	process.exit(1);
}
