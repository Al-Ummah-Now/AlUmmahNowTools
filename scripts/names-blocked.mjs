#!/usr/bin/env node
/**
 * Every name given must lead nowhere on this machine, over IPv4 and IPv6 each: the system's own answer is only 0.0.0.0 for the
 * one and only :: for the other. On Windows, which gives no answer at all for those, it is the machine itself: only 127.0.0.1
 * and only ::1. Names are only looked up; nothing connects to them. Usage: names-blocked.mjs <host>...
 */
import { ALL } from 'node:dns';
import { lookup } from 'node:dns/promises';
import { readFileSync } from 'node:fs';
import { networkInterfaces } from 'node:os';

const hosts = process.argv.slice(2);
if (hosts.length === 0) throw new Error('no host given');
const file = process.platform === 'win32' ? `${process.env.SystemRoot ?? 'C:\\Windows'}\\System32\\drivers\\etc\\hosts` : '/etc/hosts';
const NOWHERE = process.platform === 'win32' ? { 4: '127.0.0.1', 6: '::1' } : { 4: '0.0.0.0', 6: '::' };
// Without any hint macOS takes AI_V4MAPPED_CFG | AI_ADDRCONFIG, which leaves a family out, or writes IPv4 as IPv6; ALL alone does nothing else
const hints = process.platform === 'darwin' ? ALL : 0;
/** An IPv6 address of this machine that leads outside it (not the loopback, not link-local) */
const hasIPv6 = Object.values(networkInterfaces()).flat().some((i) => i.family === 'IPv6' && !i.internal && !/^fe[89ab]/i.test(i.address));
const listed = (host) => readFileSync(file, 'utf8').split(/\r?\n/).some((line) => {
	const [address, ...names] = line.replace(/#.*/, '').trim().split(/\s+/);
	return address === NOWHERE[6] && names.includes(host);
});

async function check(host, family) {
	try {
		const found = (await lookup(host, { all: true, family, hints })).map((a) => a.address);
		return { blocked: found.length > 0 && found.every((a) => a === NOWHERE[family]), said: found.join(', ') };
	} catch (e) {
		// A machine without IPv6 may not answer for it at all: then only the hosts file itself can say it
		const blocked = family === 6 && e.code === 'ENOTFOUND' && !hasIPv6 && listed(host);
		return { blocked, said: blocked ? `no answer, no IPv6 here, ${NOWHERE[6]} in the hosts file` : `${e.code}` };
	}
}

// The system reads a changed hosts file a moment later
const until = Date.now() + 10_000;
let results;
do {
	if (results) await new Promise((r) => setTimeout(r, 500));
	results = await Promise.all(hosts.flatMap((host) => [4, 6].map(async (family) => ({ host, family, ...(await check(host, family)) }))));
} while (results.some((r) => !r.blocked) && Date.now() < until);
for (const r of results) console.log(`${r.blocked ? 'blocked' : 'NOT BLOCKED'} IPv${r.family} ${r.host}: ${r.said}`);
const open = results.filter((r) => !r.blocked).length;
if (open > 0) {
	console.error(`::error::${open} of ${results.length} production lookups are not blocked`);
	process.exit(1);
}
