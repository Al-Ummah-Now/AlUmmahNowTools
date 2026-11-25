#!/usr/bin/env node
/**
 * The boxes that suggest while one types, in the real app with the system's own keyboard (the owner's report 2026-11-25: the
 * channels found in Import showed and vanished at once). Starts the app's demo mode on a page, types, and fails unless the
 * list stays. Usage: suggest-check.mjs <exe> <page> [pictures dir]
 * The keyboard: SendKeys on Windows, System Events on macOS, xdotool on Linux (run under xvfb-run).
 */
import { spawn, spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const [exe, page, pictures] = process.argv.slice(2);
const log = join(mkdtempSync(join(tmpdir(), 'suggest-')), 'log.tsv');
const server = page === 'Import' || page === 'SocialSources'; // the list comes from the server, with its rows about the search
const env = {
	...process.env,
	TOOLS_DEMO: '1', TOOLS_DEMO_LANG: process.env.CHECK_LANG ?? 'en', TOOLS_DEMO_THEME: process.env.CHECK_THEME ?? 'dark', TOOLS_DEMO_UPDATE: 'none',
	TOOLS_DEMO_PAGE: page, TOOLS_DEMO_ROLE: 'super_admin', TOOLS_DEMO_IMPORT: 'empty', TOOLS_DEMO_SOCIAL: 'all', TOOLS_DEMO_SCANNER: '1',
	TOOLS_DEMO_CHANNELS_MS: '1500', TOOLS_DEMO_SUGGEST_LOG: log,
};
const app = spawn(exe, [], { env, stdio: 'ignore' });
let ended = false;
app.on('exit', () => (ended = true));
const wait = (ms) => new Promise((done) => setTimeout(done, ms));

const lines = () => (existsSync(log) ? readFileSync(log, 'utf8').trim().split('\n').filter(Boolean) : []);
const state = () => {
	const [ms, open, items, focus, text, first, row, under, chosen] = (lines().at(-1) ?? '').split('\t');
	const of = (field) => (field ?? '').split('=').slice(1).join('=');
	return { ms: Number(ms), open: of(open) === '1', items: Number(of(items) || 0), focus: of(focus), text: of(text), first: of(first), row: of(row), under: of(under), chosen: of(chosen) };
};

function run(command, args, timeout = 30_000) {
	const r = spawnSync(command, args, { encoding: 'utf8', timeout });
	if (r.status !== 0) throw new Error(`${command} ${args.join(' ')}: ${r.error ?? ''} ${r.stderr} ${r.stdout}`);
	return r.stdout;
}

const powershell = (script) => run('powershell', ['-NoProfile', '-STA', '-Command', script]);
/** Windows opens each window lower than the one before, down to where a row lies behind the taskbar. */
const moved = (to) => powershell(`Add-Type -Name S -Namespace W -MemberDefinition '[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] public struct R { public int L; public int T; public int Right; public int B; } [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out R r); [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool MoveWindow(System.IntPtr h, int x, int y, int w, int t, bool p);'; $h = (Get-Process -Id ${app.pid}).MainWindowHandle; $r = New-Object W.S+R; [W.S]::GetWindowRect($h, [ref]$r) | Out-Null; [W.S]::MoveWindow($h, ${to}, $true) | Out-Null`);
const KEYS = {
	win32: { front: () => powershell(`(New-Object -ComObject WScript.Shell).AppActivate(${app.pid}) | Out-Null`), text: (t) => powershell(`Add-Type -AssemblyName System.Windows.Forms; foreach ($c in '${t}'.ToCharArray()) { [System.Windows.Forms.SendKeys]::SendWait([string]$c); Start-Sleep -Milliseconds 150 }`), top: () => moved('0, 0, $r.Right - $r.L, $r.B - $r.T'), narrower: () => moved('$r.L, $r.T, $r.Right - $r.L - 120, $r.B - $r.T'), click: (x, y) => powershell(`Add-Type -Name M -Namespace W -MemberDefinition '[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] public struct P { public int X; public int Y; } [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool ClientToScreen(System.IntPtr h, ref P p); [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, System.UIntPtr e);'; $p = New-Object W.M+P; $p.X = ${x}; $p.Y = ${y}; [W.M]::ClientToScreen((Get-Process -Id ${app.pid}).MainWindowHandle, [ref]$p) | Out-Null; [W.M]::SetCursorPos($p.X, $p.Y) | Out-Null; Start-Sleep -Milliseconds 300; [W.M]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 80; [W.M]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)`), key: (k) => powershell(`Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.SendKeys]::SendWait('${{ Backspace: '{BACKSPACE}', Escape: '{ESC}', Down: '{DOWN}', Enter: '{ENTER}', Tab: '{TAB}', All: '^a' }[k]}')`) },
	darwin: {
		front: () => run('osascript', ['-e', `tell application "System Events" to set frontmost of (first process whose unix id is ${app.pid}) to true`]),
		text: (t) => run('osascript', [...t].flatMap((c) => ['-e', `tell application "System Events" to keystroke "${c}"`, '-e', 'delay 0.15'])),
		narrower: () => run('osascript', ['-e', `tell application "System Events" to tell (first process whose unix id is ${app.pid})`, '-e', 'set s to size of window 1', '-e', 'set size of window 1 to {(item 1 of s) - 120, item 2 of s}', '-e', 'end tell']),
		key: (k) => run('osascript', ['-e', `tell application "System Events" to ${k === 'All' ? 'keystroke "a" using command down' : `key code ${{ Backspace: 51, Escape: 53, Down: 125, Enter: 36, Tab: 48 }[k]}`}`]),
	},
	linux: { front: () => {}, text: (t) => run('xdotool', ['type', '--delay', '150', t]), key: (k) => run('xdotool', ['key', { Backspace: 'BackSpace', Escape: 'Escape', Down: 'Down', Enter: 'Return', Tab: 'Tab', All: 'ctrl+a' }[k]]) },
}[process.platform];

const problems = [];
function expect(what, ok) {
	const s = state();
	// Wherever it opens, the channel lists lie under their box (they are placed anew at every change)
	const placed = !s.open || (s.under !== '' && Math.abs(Number(s.under)) <= 2);
	console.log(`${ok(s) && placed ? 'ok  ' : 'FAIL'} ${page}: ${what} -- ${JSON.stringify(s)}`);
	if (!ok(s)) problems.push(what);
	if (!placed) problems.push(`${what}: the list is not under its box (${s.under})`);
}
/** Everything the list did from `ms` on. */
const since = (ms) => lines().filter((l) => Number(l.split('\t')[0]) > ms);

function picture(name) {
	if (!pictures) return;
	mkdirSync(pictures, { recursive: true });
	const file = join(pictures, `${page}-${name}.png`);
	try {
		if (process.platform === 'win32') powershell(`Add-Type -AssemblyName System.Windows.Forms,System.Drawing; $b=[System.Windows.Forms.Screen]::PrimaryScreen.Bounds; $i=New-Object System.Drawing.Bitmap $b.Width,$b.Height; [System.Drawing.Graphics]::FromImage($i).CopyFromScreen($b.Location,[System.Drawing.Point]::Empty,$b.Size); $i.Save('${file}')`);
		else if (process.platform === 'darwin') run('screencapture', ['-x', file]);
		else run('import', ['-window', 'root', file]);
	} catch (e) { console.log(`no picture ${name}: ${e.message}`); }
}

const SEERAH = 'ch_demo_seerah';
/** The row the arrow key was on, as the box said it: what is typed after it is added to it. */
const ALL_FIRST = () => state().first;
/** The keyboard goes back to the box (a file the app looks for: Tab alone goes where the page's order says). */
async function back() {
	writeFileSync(`${log}.focus`, '');
	await wait(800);
}
const [SOME, NONE] = ['se', 'zz'];
try {
	for (let i = 0; i < 240 && !ended && !lines().length; i++) await wait(500);
	if (!lines().length) throw new Error(ended ? 'the app ended' : 'the box was not found');
	KEYS.top?.();
	KEYS.front();
	await wait(3000);
	if (server) expect('moving to the empty box lists the first channels, and they stay', (s) => s.open && s.items > 1);
	KEYS.text(SOME);
	await wait(400);
	const typedAt = state().ms;
	await wait(3000);
	picture('found');
	expect('what is typed is found, and the list stays', (s) => s.open && s.items > 0 && s.text === SOME);
	console.log(since(typedAt).join('\n'));
	if (server) {
		KEYS.text(NONE);
		await wait(250);
		picture('searching');
		await wait(3000);
		picture('none');
		expect('nothing found: the list says so', (s) => s.open && s.items === 1 && s.text === SOME + NONE);
		for (const _ of NONE) KEYS.key('Backspace');
		await wait(3000);
		expect('found again after Backspace', (s) => s.open && s.items > 0 && s.text === SOME);
	}
	KEYS.key('Escape');
	await wait(2000);
	expect('Escape closes the list, and it stays closed', (s) => !s.open && s.focus === 'TextBox');
	if (server) {
		KEYS.key('Backspace');
		await wait(100);
		KEYS.key('Escape');
		await wait(2500);
		expect('Escape before the search has answered: the answer does not open it again', (s) => !s.open);
		KEYS.text('e');
	} else {
		KEYS.key('Backspace');
	}
	await wait(3000);
	expect('typing opens it again', (s) => s.open && s.items > 0);
	KEYS.key('Down');
	await wait(700);
	expect('the arrow key moves into the list, which stays open, and chooses nothing', (s) => s.open && s.text.length > 2 && !s.chosen);
	picture('arrow');
	KEYS.key('Enter');
	await wait(2500);
	expect('Enter chooses and closes, and it stays closed', (s) => !s.open && s.text.length > 2 && (!server || s.chosen === SEERAH));
	picture('chosen');
	if (KEYS.click) {
		KEYS.key('All');
		KEYS.text(SOME);
		await wait(3000);
		expect('typing over the chosen one opens it again', (s) => s.open && s.items > 0 && s.text === SOME && s.row !== '');
		const [x, y] = state().row.split(',');
		KEYS.click(x, y);
		await wait(2500);
		expect('a click chooses and closes, and it stays closed', (s) => !s.open && s.text.length > SOME.length && (!server || s.chosen === SEERAH));
		picture('clicked');
		KEYS.front();
	}
	if (server) {
		// Every channel has an "a" and an "r"; the one chosen is Seerah's
		const again = async (what, letter) => {
			await back();
			KEYS.key('All');
			KEYS.text(letter);
			await wait(1000);
			expect(`${what}: the list is open while the server is asked`, (s) => s.open && s.text === letter);
			await wait(2500);
			expect(`${what}: several are found`, (s) => s.open && s.items === 3 && s.text === letter);
		};
		await again('arrow then Escape', 'a');
		KEYS.key('Down');
		await wait(500);
		expect('the arrow key is on another channel, which is not chosen', (s) => s.open && s.text !== 'a' && s.chosen === SEERAH);
		KEYS.key('Escape');
		await wait(1500);
		expect('arrow then Escape keeps what was typed and what was chosen', (s) => !s.open && s.text === 'a' && s.chosen === SEERAH);
		await again('arrow then Tab', 'r');
		KEYS.key('Down');
		await wait(500);
		KEYS.key('Tab');
		await wait(1500);
		expect('arrow then Tab keeps what was typed and what was chosen', (s) => !s.open && s.focus !== 'TextBox' && s.text === 'r' && s.chosen === SEERAH);
		await back();
		KEYS.key('All');
		KEYS.text('zz');
		KEYS.key('Tab');
		await wait(2500);
		expect('Tab before the search has answered: it is given up, the rows are as before', (s) => !s.open && s.focus !== 'TextBox' && s.items === 3 && s.text === 'zz');
		await back();
		KEYS.key('All');
		KEYS.text('se');
		KEYS.key('Enter');
		await wait(2500);
		expect('Enter before the search has answered: it is given up, and nothing is chosen', (s) => !s.open && s.focus === 'TextBox' && s.text === 'se' && s.chosen === SEERAH);
		// The arrow keys were on a row, then something else is typed: Enter does not take that row
		await again('arrow, typing, Enter', 'a');
		KEYS.key('Down');
		await wait(300);
		KEYS.text('h');
		KEYS.key('Enter');
		await wait(2500);
		expect('arrow, typing, Enter: the row the arrow was on is not chosen', (s) => !s.open && s.text === `${ALL_FIRST()}h` && s.chosen === SEERAH);
	}
	// The list closes with the box's width, on every page, and opens under the box again
	const [WORD, OTHER] = ['se', 'h'];
	await back();
	KEYS.key('All');
	KEYS.text(WORD);
	await wait(3000);
	expect('before the window gets narrower: the list is open', (s) => s.open && s.items > 0);
	if (KEYS.narrower) {
		KEYS.narrower();
		await wait(1500);
		expect('the window got narrower: the list is closed', (s) => !s.open && s.text === WORD);
	}
	KEYS.front();
	await back();
	KEYS.key('All');
	KEYS.text(server ? OTHER : 'e');
	await wait(3000);
	expect('typing opens it under the box again', (s) => s.open && s.items > 0);
	if (!server) {
		const refresh = async () => {
			writeFileSync(`${log}.refresh`, '');
			await wait(2000);
		};
		await refresh();
		expect('the names are loaded again while the list is open: it stays', (s) => s.open && s.items > 0 && s.text === 'e');
		KEYS.key('Escape');
		await wait(800);
		await refresh();
		expect('the names are loaded again after Escape: it stays closed', (s) => !s.open && s.text === 'e');
		KEYS.key('All');
		KEYS.text(WORD);
		await wait(1500);
		expect('typing opens it again after that', (s) => s.open && s.items > 0 && s.text === WORD);
	}
} catch (e) {
	problems.push(e.message);
	console.log(e.stack);
	picture('error');
} finally {
	console.log(`--- ${page}: everything the list did\n${lines().join('\n')}`);
	app.kill(process.platform === 'win32' ? undefined : 'SIGKILL');
}
if (problems.length) {
	for (const p of problems) console.log(`::error::suggestions on ${page}: ${p}`);
	process.exit(1);
}
console.log(`suggestions on ${page}: passed`);
process.exit(0);
