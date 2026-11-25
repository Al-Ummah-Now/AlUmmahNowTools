#!/usr/bin/env node
/**
 * The real update, end to end, on one machine (wiki AUN-APP-UPDATES §13): an installed test build N finds N+1 on a
 * local server, checks its signature and hash, installs it as it closes, and starts as N+1. Keys are made for the run.
 *
 *   update-test.mjs --exe <installed app> --package <N+1 nupkg> --platform windows|macos --arch x64|arm64
 *                   --from <version N> --to <version N+1> --packages <Velopack's package folder> --package-name <its file name there>
 *                   --state <the app's updates.json>
 *                   [--case ok|tampered|bad-signature|foreign-host|planted|swapped|yanked]
 *
 * `ok` must end on N+1. Every other case must stay on N through a second start:
 *  - tampered, bad-signature, foreign-host: the download is refused and the reason reported;
 *  - planted: a good N+1 package is already in the package folder, but this run verified none;
 *  - swapped: the staged file is replaced (same size) before the app closes;
 *  - yanked: after staging, a newer key list yanks N+1; the app learns it on a check and then closes.
 */
import { createHash } from 'node:crypto';
import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { generateKey, signBytes, buildKeyList } from './app-release.mjs';

const a = {};
for (let i = 2; i < process.argv.length; i += 2) a[process.argv[i].slice(2)] = process.argv[i + 1];
const kase = a.case ?? 'ok';
const REPO = 'AlUmmahNowTools';
const tag = `v${a.to}`;
const base = `https://github.com/Al-Ummah-Now/${REPO}/releases/download/${tag}/`;
const dir = mkdtempSync(join(tmpdir(), 'tools-update-test-'));
const log = join(dir, 'log.txt');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const lines = () => (existsSync(log) ? readFileSync(log, 'utf8').trim().split('\n') : []);

const [root, release, stranger] = [await generateKey(), await generateKey(), await generateKey()];
const serial = Math.floor(Date.now() / 1000);
const list = Buffer.from(JSON.stringify(await buildKeyList({ repo: REPO, serial, keys: [release.publicKey] })));
const yankedList = Buffer.from(JSON.stringify(await buildKeyList({ repo: REPO, serial: serial + 1, keys: [release.publicKey], yanked: [a.to] })));
const stagedFile = join(a.packages, a['package-name']);
const pkg = readFileSync(a.package);
const manifest = Buffer.from(
	JSON.stringify({
		schema: 1,
		repo: REPO,
		tag,
		version: a.to,
		channel: 'stable',
		publishedAt: new Date().toISOString().replace(/\.\d{3}Z$/, 'Z'),
		notes: { en: 'Test.', ar: 'اختبار.' },
		artifacts: [{ app: 'tools', platform: a.platform, arch: a.arch, kind: 'velopack-full', url: `${base}update.nupkg`, sha256: createHash('sha256').update(pkg).digest('hex'), size: pkg.byteLength }],
		stores: [],
		yanked: [],
	}),
);
const served = Buffer.from(pkg);
if (kase === 'tampered') served[served.length >> 1] ^= 1;
const sig = await signBytes(manifest, [kase === 'bad-signature' || kase === 'planted' ? stranger.privateKey : release.privateKey]);
const asset = kase === 'foreign-host' ? 'https://downloads.example.com/update.nupkg' : 'https://release-assets.githubusercontent.com/update.nupkg';
const configWith = async (keys) => JSON.stringify({
	update: { enabled: true, status: 'recommended', minimumVersion: '0.0.0', latestVersion: a.to, storeUrl: null, channel: 'stable', publishedAt: null, notes: null, message: null, action: 'install', url: null, manifestUrl: `${base}app-release.json`, tag, signed: true, available: true, checkHours: 6, keys: { list: keys.toString(), sig: await signBytes(keys, [root.privateKey]) } },
});
let config = await configWith(list);
const calls = [];
let reported = null;
const server = createServer((req, res) => {
	const path = req.url.split('?')[0];
	calls.push(path);
	const ok = (body) => (res.writeHead(200, { 'Content-Length': Buffer.byteLength(body) }), res.end(body));
	if (path.endsWith('/api/v1/app-config')) return ok(config);
	if (path.endsWith('/api/v1/app-config/update-failure')) {
		let body = '';
		req.on('data', (d) => (body += d));
		req.on('end', () => ((reported = JSON.parse(body)), res.writeHead(202), res.end()));
		return;
	}
	const gh = `/github.com${new URL(base).pathname}`;
	if (path === `${gh}app-release.json`) return ok(manifest);
	if (path === `${gh}app-release.json.sig`) return ok(sig);
	if (path === `${gh}update.nupkg`) return res.writeHead(302, { Location: asset }), res.end();
	if (path === '/release-assets.githubusercontent.com/update.nupkg' || path === '/downloads.example.com/update.nupkg') return ok(served);
	res.writeHead(404);
	res.end();
});
await new Promise((r) => server.listen(0, '127.0.0.1', r));
const env = { ...process.env, TOOLS_DEMO: '1', TOOLS_DEMO_LANG: 'en', TOOLS_UPDATE_TEST_ROOT: root.publicKey, TOOLS_UPDATE_TEST_SERVER: `http://127.0.0.1:${server.address().port}`, TOOLS_UPDATE_TEST_LOG: log, ...(kase === 'swapped' || kase === 'yanked' ? { TOOLS_UPDATE_TEST_EXIT_MS: '8000' } : {}), ...(kase === 'yanked' ? { TOOLS_UPDATE_TEST_RECHECK: '1' } : {}) };
// Each case starts as a new install: a yank is remembered for good, so an earlier case's would count here
rmSync(a.state, { force: true });
if (kase === 'planted') {
	mkdirSync(a.packages, { recursive: true });
	writeFileSync(stagedFile, pkg);
}
const start = () => spawn(a.exe, [], { env, stdio: 'inherit' });
const exited = (p) => new Promise((r) => (p.exitCode !== null ? r() : p.once('exit', r)));
async function until(what, ms, test) {
	const end = Date.now() + ms;
	while (Date.now() < end) {
		if (test()) return true;
		await sleep(500);
	}
	console.error(`timed out: ${what}\nlog:\n${lines().join('\n')}\ncalls:\n${calls.join('\n')}\nreported: ${JSON.stringify(reported)}`);
	return false;
}
/** Through the updater's time and another start, the app is still N */
async function unchanged() {
	await sleep(10_000);
	const before = lines().length;
	const second = start();
	if (!(await until('the app starts again', 60_000, () => lines().slice(before).some((l) => l.startsWith('started '))))) fail('did not start again');
	second.kill();
	if (lines().slice(before).find((l) => l.startsWith('started ')) !== `started ${a.from} self=true`) fail('the version changed');
}
function fail(why) {
	console.error(`::error::update test (${kase}): ${why}\nlog:\n${lines().join('\n')}\ncalls:\n${calls.join('\n')}\nreported: ${JSON.stringify(reported)}`);
	process.exit(1);
}

const first = start();
if (!(await until('the app starts', 60_000, () => lines().some((l) => l === `started ${a.from} self=true`)))) fail(`the installed app is not a self-updating ${a.from}`);
if (kase === 'ok') {
	if (!(await until('the update is staged', 180_000, () => lines().some((l) => l === `Ready ${a.to}`)))) fail('never staged');
	await exited(first);
	// Velopack's updater runs after the exit; starting again before it's done would race it
	await sleep(20_000);
	const before = lines().length;
	const second = start();
	if (!(await until('the app starts again', 60_000, () => lines().slice(before).some((l) => l.startsWith('started '))))) fail('did not start after the update');
	second.kill();
	const started = lines().slice(before).find((l) => l.startsWith('started '));
	if (started !== `started ${a.to} self=true`) fail(`after the update: ${started}`);
	if (reported) fail('a good update was reported as failed');
} else if (kase === 'swapped' || kase === 'yanked') {
	if (!(await until('the update is staged', 180_000, () => lines().some((l) => l === `Ready ${a.to}`)))) fail('never staged');
	if (kase === 'swapped') {
		if (!existsSync(stagedFile)) fail(`nothing staged at ${stagedFile}`);
		const other = Buffer.from(pkg);
		other[other.length >> 1] ^= 1;
		writeFileSync(stagedFile, other);
	} else config = await configWith(yankedList);
	await exited(first);
	if (kase === 'yanked' && !lines().includes('rechecked Hidden')) fail('the yank did not cancel the staged update');
	if (kase === 'yanked' && reported?.reason !== 'yanked') fail(`reported ${reported?.reason}, expected yanked`);
	await unchanged();
} else {
	const reason = { tampered: 'hash', 'bad-signature': 'unknown_key', planted: 'unknown_key', 'foreign-host': 'host' }[kase];
	if (!(await until('the refusal is reported', 180_000, () => reported !== null))) fail('not reported');
	await sleep(3_000);
	first.kill();
	await exited(first);
	if (reported.reason !== reason) fail(`reported ${reported.reason}, expected ${reason}`);
	if (lines().some((l) => l.startsWith('Ready'))) fail('staged a refused update');
	if (kase === 'foreign-host' && calls.some((c) => c.startsWith('/downloads.example.com'))) fail('asked the foreign host');
	await unchanged();
}
if (!existsSync(a.state)) fail(`no state at ${a.state}`);
console.log(`update test (${kase}) passed:\n${lines().join('\n')}`);
server.close();
process.exit(0);
