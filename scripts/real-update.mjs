#!/usr/bin/env node
/**
 * A real update of a published Tools release (docs/RELEASE.md, Real update test): the installed release N, as people have it,
 * finds N+1 through QA's app-config, downloads it from GitHub, checks its signature, installs it as it closes, and starts as
 * N+1. Nothing here is a test build or a test key. Run by .github/workflows/real-update.yml on a throwaway runner.
 *
 *   real-update.mjs --exe <installed app> --packages <Velopack's package folder> --data <the app's local data folder>
 *                   --platform windows|macos --arch x64|arm64 --from 2026.11.25 --to 2026.11.25.2
 */
import { spawn, execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';

const a = {};
for (let i = 2; i < process.argv.length; i += 2) a[process.argv[i].slice(2)] = process.argv[i + 1];
const QA = 'https://videos.api.qa.alummahnow.net';
const work = mkdtempSync(join(tmpdir(), 'tools-real-update-'));
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const say = (text) => console.log(`${new Date().toISOString()} ${text}`);
function fail(why) {
	console.error(`::error::real update: ${why}`);
	process.exit(1);
}
async function until(what, ms, test) {
	for (const end = Date.now() + ms; Date.now() < end; await sleep(1000)) if (test()) return;
	fail(`timed out: ${what}`);
}
/** The Velopack package version of a Tools version (VelopackInstaller.PackVersion) */
const pack = (v) => {
	const [y, m, d, n] = v.split('.').map(Number);
	return `${y}.${m}.${d * 100 + (n ?? 1)}`;
};
/** What is installed now, as the updater wrote it */
function installed() {
	const file = join(dirname(a.exe), 'sq.version');
	return existsSync(file) ? /<version>([^<]+)<\/version>/.exec(readFileSync(file, 'utf8'))?.[1] : undefined;
}
/**
 * The version the program itself carries, and what it is for a Tools version. Windows: the product version without its commit.
 * macOS: release.yml writes YYYY.M.D as CFBundleShortVersionString and N (1 for the first of a day) as CFBundleVersion.
 */
function program() {
	if (a.platform === 'windows') return execFileSync('powershell', ['-NoProfile', '-Command', `(Get-Item -LiteralPath '${a.exe}').VersionInfo.ProductVersion`], { encoding: 'utf8' }).trim().split('+')[0];
	const plist = (key) => execFileSync('/usr/libexec/PlistBuddy', ['-c', `Print ${key}`, join(dirname(a.exe), '..', 'Info.plist')], { encoding: 'utf8' }).trim();
	return `${plist('CFBundleShortVersionString')} (${plist('CFBundleVersion')})`;
}
function expected(v) {
	if (a.platform === 'windows') return v;
	const [y, m, d, n] = v.split('.').map(Number);
	return `${y}.${m}.${d} (${n ?? 1})`;
}

// QA must offer exactly this update, or the app has nothing to find
const answer = await (await fetch(`${QA}/api/v1/app-config?app=tools&platform=${a.platform}&version=${a.from}&source=direct&arch=${a.arch}&channel=stable`)).json();
const u = answer.update ?? {};
say(`QA offers ${u.latestVersion} (${u.action}, ${u.tag}, signed ${u.signed})`);
if (!u.enabled || u.latestVersion !== a.to || u.action !== 'install' || u.tag !== `v${a.to}`) fail(`QA does not offer ${a.to} to ${a.from}`);

// What will be downloaded, as the published manifest lists it
const manifest = await (await fetch(`https://github.com/Al-Ummah-Now/AlUmmahNowTools/releases/download/v${a.to}/app-release.json`)).json();
const artifact = manifest.artifacts.find((x) => x.app === 'tools' && x.platform === a.platform && x.arch === a.arch && x.kind === 'velopack-full');
if (manifest.version !== a.to || !artifact) fail(`the manifest of v${a.to} has no update for ${a.platform} ${a.arch}`);

// The app asks QA and no other environment; the workflow also sends the production names nowhere
const settings = join(a.data, 'AlUmmahNow', 'Tools');
mkdirSync(settings, { recursive: true });
writeFileSync(join(settings, 'preferences.json'), JSON.stringify({ Language: 'en', Environment: 'Qa' }));
// A queue from before the update is as it was after it
const queue = join(a.data, 'AlUmmahNowTools', 'queue-qa.json');
mkdirSync(dirname(queue), { recursive: true });
const steps = ['Hash', 'Encode', 'Upload', 'Attach', 'Transcribe', 'Check', 'Translate'].map((kind) => ({ kind, status: 'Pending', attempts: 0 }));
const video = { path: join(work, 'lesson.mp4'), size: 10, lastModifiedMs: 0, mimeType: 'video/mp4', captions: [], metadata: { fallbackTitle: 'lesson' }, notes: [] };
writeFileSync(queue, JSON.stringify({ settings: { parallel: { Hash: 2 }, maxAttempts: 6 }, items: [{ id: '0123456789abcdef0123456789abcdef', batchId: 'b', channelId: 'c', video, paused: true, steps, createdAt: '2026-11-25T12:00:00+00:00', log: [] }] }));
/** What matters of it, however it is written */
const items = () => JSON.stringify(JSON.parse(readFileSync(queue, 'utf8')).items.map((i) => [i.id, i.paused, i.video.path, i.steps.map((s) => [s.kind, s.status])]));
const before = items();

if (installed() !== pack(a.from) || program() !== expected(a.from)) fail(`installed is ${installed()}, program ${program()}, expected ${pack(a.from)} and ${expected(a.from)}`);
say(`installed: ${installed()}, program ${program()}`);

/** Starts the installed app as a person would (no demo mode), and has it quit by itself after a while, as the close button does */
function start(name, quitAfterMs) {
	const ready = join(work, `${name}.ready`);
	const env = { ...process.env, TOOLS_SMOKE_CLOSE: 'quit', TOOLS_SMOKE_CLOSE_DELAY_MS: String(quitAfterMs), TOOLS_SMOKE_READY: ready };
	for (const k of Object.keys(env)) if (k.startsWith('TOOLS_DEMO') || k.startsWith('TOOLS_SCREENSHOT')) delete env[k];
	const app = spawn(a.exe, [], { env, stdio: 'inherit' });
	const exited = new Promise((r) => app.once('exit', (code, signal) => r(code ?? signal)));
	return { app, exited, ready, closing: `${ready}.closing` };
}
const staged = join(a.packages, `AlUmmahNow.Tools-${pack(a.to)}-full.nupkg`);

/** The app's own quit was asked for (not a crash), and it ended by itself with 0 */
async function ended(run, what, ms) {
	const code = await Promise.race([run.exited, sleep(ms).then(() => 'timeout')]);
	if (code === 'timeout') fail(`${what} was still running`);
	if (!existsSync(run.closing)) fail(`${what} ended before it was closed (${code})`);
	if (code !== 0) fail(`${what} ended with ${code}`);
	say(`${what} was closed and ended (0)`);
}

// 1. N finds, downloads and stages N+1 (the first check is 30 s after the start), and installs it as it closes after 3 minutes
const first = start('first', 180_000);
await until(`${a.from} starts`, 120_000, () => existsSync(first.ready));
say(`${a.from} started`);
await until('the update is staged', 150_000, () => existsSync(staged) && statSync(staged).size === artifact.size);
// Velopack's check of the file, and the app's, take a moment more; the close comes after them
if (existsSync(first.closing)) fail('the update was staged too late to be installed at the close');
say(`staged: ${staged}, ${artifact.size} bytes as the manifest lists`);
await ended(first, a.from, 240_000);
await until(`${a.to} is installed`, 180_000, () => installed() === pack(a.to));
// The updater has put everything in place once the program itself is the new one
await until('the program is the new one', 60_000, () => {
	try {
		return program() === expected(a.to);
	} catch {
		return false;
	}
});
say(`installed: ${installed()}, program ${program()}`);
if (a.platform === 'macos') execFileSync('codesign', ['--verify', '--deep', '--strict', dirname(dirname(dirname(a.exe)))], { stdio: 'inherit' });

// 2. N+1 starts, runs past its own update check and closes, and stays N+1
await sleep(10_000);
const second = start('second', 60_000);
await until(`${a.to} starts`, 120_000, () => existsSync(second.ready));
say(`${a.to} started`);
await ended(second, a.to, 150_000);
await sleep(15_000);
if (installed() !== pack(a.to) || program() !== expected(a.to)) fail(`after a second run: ${installed()}, ${program()}`);
const left = existsSync(a.packages) ? readdirSync(a.packages).filter((f) => f.endsWith('.nupkg') && !f.includes(pack(a.to))) : [];
if (left.length) fail(`other packages left: ${left}`);
if (items() !== before) fail('the import queue changed');
const state = JSON.parse(readFileSync(join(settings, 'updates.json'), 'utf8'));
say(`the app's update state: ${JSON.stringify(state).slice(0, 300)}`);
say(`real update passed: ${a.from} became ${a.to}`);
process.exit(0);
