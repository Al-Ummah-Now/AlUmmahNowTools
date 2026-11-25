#!/usr/bin/env node
/**
 * What each release file is, for app-release.json: release-files.mjs <dist> <version> prints the spec that
 * `app-release.mjs build --artifacts` reads. A file it doesn't know, or a missing one, fails the release.
 */
import { readdirSync } from 'node:fs';
import { join } from 'node:path';

const [dist, version] = process.argv.slice(2);
const v = version.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const rule = (pattern, platform, arch, kind) => ({ pattern: new RegExp(`^${pattern.replace('V', () => v)}$`), platform, arch, kind });
const RULES = [
	rule('AlUmmahNow-Tools-Windows-V-x64-update\\.nupkg', 'windows', 'x64', 'velopack-full'),
	rule('AlUmmahNow-Tools-Windows-V-x64-Setup(-unsigned)?\\.exe', 'windows', 'x64', 'exe'),
	rule('AlUmmahNow-Tools-Windows-V-x64(-unsigned)?\\.msi', 'windows', 'x64', 'msi'),
	rule('AlUmmahNow-Tools-Windows-V-x64-portable(-unsigned)?\\.zip', 'windows', 'x64', 'zip'),
	rule('AlUmmahNow-Tools-macOS-V-arm64-update\\.nupkg', 'macos', 'arm64', 'velopack-full'),
	rule('AlUmmahNow-Tools-macOS-V-x64-update\\.nupkg', 'macos', 'x64', 'velopack-full'),
	rule('AlUmmahNow-Tools-macOS-V-arm64(-unsigned)?\\.dmg', 'macos', 'arm64', 'dmg'),
	rule('AlUmmahNow-Tools-macOS-V-x64(-unsigned)?\\.dmg', 'macos', 'x64', 'dmg'),
	rule('AlUmmahNow-Tools-Linux-V-x64\\.tar\\.gz', 'linux', 'x64', 'tar'),
	rule('alummahnow-tools_V_amd64\\.deb', 'linux', 'x64', 'deb'),
];
const files = readdirSync(dist).sort();
const out = [];
for (const { pattern, platform, arch, kind } of RULES) {
	const hits = files.filter((f) => pattern.test(f));
	if (hits.length !== 1) throw new Error(`${platform} ${arch} ${kind}: ${hits.length} files match ${pattern}`);
	out.push({ app: 'tools', platform, arch, kind, file: join(dist, hits[0]) });
}
const unknown = files.filter((f) => !RULES.some((r) => r.pattern.test(f)));
if (unknown.length) throw new Error(`not a known release file: ${unknown.join(', ')}`);
process.stdout.write(JSON.stringify(out, null, 2) + '\n');
