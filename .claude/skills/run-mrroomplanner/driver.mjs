#!/usr/bin/env node
/**
 * MRRoomPlanner driver — one entry point for driving this Unity project.
 *
 * Two transports, picked automatically:
 *   BRIDGE   — Unity Editor is OPEN and the Meta MCP bridge is listening.
 *              Fast (EditMode tests ~7s), can inspect the live scene, screenshot windows.
 *   BATCH    — Unity Editor is CLOSED. Runs `Unity.exe -batchmode` (~60-90s per run).
 *              The only way to run PlayMode tests and the rig setup reliably.
 *
 * The bridge is reached over plain HTTP using the token Unity writes to
 * %TEMP%/mcpbridge_*.info — so this works even when the agent has no MCP
 * tools registered.
 *
 * Usage:
 *   node .claude/skills/run-mrroomplanner/driver.mjs status
 *   node .claude/skills/run-mrroomplanner/driver.mjs test [EditMode|PlayMode|All]
 *   node .claude/skills/run-mrroomplanner/driver.mjs setup            # rebuild rig (needs Editor closed)
 *   node .claude/skills/run-mrroomplanner/driver.mjs build            # Quest APK (needs Editor closed)
 *   node .claude/skills/run-mrroomplanner/driver.mjs scene <pattern>  # find GameObjects (bridge)
 *   node .claude/skills/run-mrroomplanner/driver.mjs inspect <id>     # components of a GameObject (bridge)
 *   node .claude/skills/run-mrroomplanner/driver.mjs shot <Window> [out.png]
 *   node .claude/skills/run-mrroomplanner/driver.mjs errors           # console errors (bridge)
 *   node .claude/skills/run-mrroomplanner/driver.mjs mcp <Tool> <method> [k=v ...]
 */

import { readFileSync, writeFileSync, existsSync, readdirSync, mkdirSync, rmSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(HERE, '..', '..', '..');          // <unit> = project root
const LOCKFILE = join(ROOT, 'Temp', 'UnityLockfile');

// ---------------------------------------------------------------- Unity exe

function unityExe() {
  if (process.env.UNITY_EXE && existsSync(process.env.UNITY_EXE)) return process.env.UNITY_EXE;
  const pv = join(ROOT, 'ProjectSettings', 'ProjectVersion.txt');
  let version = '6000.0.81f1';
  if (existsSync(pv)) {
    const m = readFileSync(pv, 'utf8').match(/m_EditorVersion:\s*(\S+)/);
    if (m) version = m[1];
  }
  const candidates = [
    `D:\\Unity\\Editors\\${version}\\Editor\\Unity.exe`,
    `C:\\Program Files\\Unity\\Hub\\Editor\\${version}\\Editor\\Unity.exe`,
  ];
  for (const c of candidates) if (existsSync(c)) return c;
  die(`Unity ${version} not found. Tried:\n  ${candidates.join('\n  ')}\nSet UNITY_EXE to override.`);
}

/** Does this pid still exist? (signal 0 = existence probe, EPERM means "alive, not ours") */
function pidAlive(pid) {
  if (!pid) return false;
  try { process.kill(pid, 0); return true; } catch (e) { return e.code === 'EPERM'; }
}

function unityRunning() {
  const r = spawnSync('tasklist', ['/FI', 'IMAGENAME eq Unity.exe', '/NH'], { encoding: 'utf8' });
  return /Unity\.exe/i.test(r.stdout || '');
}

/**
 * A crashed/killed Editor leaves Temp/UnityLockfile behind, so the file alone lies.
 * Trust it only when a Unity process actually exists.
 */
const editorOpen = () => existsSync(LOCKFILE) && unityRunning();
const staleLock = () => existsSync(LOCKFILE) && !unityRunning();

// ---------------------------------------------------------------- MCP bridge

/** Unity writes {port, token, projectPath, pid} here while the bridge runs. */
function bridgeInfo() {
  const dir = tmpdir();
  let files = [];
  try {
    files = readdirSync(dir).filter((f) => f.startsWith('mcpbridge_') && f.endsWith('.info'));
  } catch { return null; }
  for (const f of files) {
    try {
      const info = JSON.parse(readFileSync(join(dir, f), 'utf8'));
      // only trust the file that belongs to THIS project
      const p = (info.projectPath || '').replace(/\\/g, '/').toLowerCase();
      if (p && p !== ROOT.replace(/\\/g, '/').toLowerCase()) continue;
      // Unity does not always clean this up on exit — a file whose pid is gone is stale.
      if (!pidAlive(info.pid)) continue;
      return info;
    } catch { /* stale/partial file */ }
  }
  return null;
}

let _rpcId = 0;

async function mcpCall(tool, method, args = {}) {
  const info = bridgeInfo();
  if (!info) throw new Error('MCP bridge not running (no discovery file). Is the Unity Editor open?');
  const res = await fetch(`http://127.0.0.1:${info.port}/mcpbridge/`, {
    method: 'POST',
    headers: {
      'Authorization': `Bearer ${info.token}`,
      'Content-Type': 'application/json',
      'Accept': 'application/json, text/event-stream',
    },
    body: JSON.stringify({
      jsonrpc: '2.0',
      id: String(++_rpcId),
      method: 'tools/call',
      params: { name: tool, arguments: { method, ...args } },
    }),
  });
  const text = await res.text();
  // the endpoint may answer as plain JSON or as an SSE frame ("data: {...}")
  const payload = text.startsWith('data:') ? text.replace(/^data:\s*/gm, '').trim() : text;
  let json;
  try { json = JSON.parse(payload); } catch { throw new Error(`Bad MCP response: ${text.slice(0, 300)}`); }
  if (json.error) throw new Error(`MCP error: ${JSON.stringify(json.error)}`);

  // tools/call wraps the payload in result.content[].text
  const content = json.result?.content;
  if (Array.isArray(content)) {
    const t = content.map((c) => c.text ?? '').join('');
    try { return JSON.parse(t); } catch { return t; }
  }
  return json.result;
}

/** Unwrap the bridge's {success, "return value": ...} envelope. */
const unwrap = (r) => (r && typeof r === 'object' && 'return value' in r ? r['return value'] : r);

// ---------------------------------------------------------------- batchmode

function runUnity(args, logName, timeoutMin = 30) {
  const exe = unityExe();
  if (editorOpen()) die('Unity Editor is OPEN — batchmode needs the project lock. Close the Editor and retry.');
  const log = join(ROOT, `ci-${logName}.log`);
  const r = spawnSync(exe, args.concat(['-logFile', log]), {
    stdio: 'inherit',
    timeout: timeoutMin * 60 * 1000,
    windowsHide: true,
  });
  return { code: r.status, log };
}

function parseResults(xmlPath) {
  if (!existsSync(xmlPath)) return null;
  const xml = readFileSync(xmlPath, 'utf8');
  const attr = (n) => Number((xml.match(new RegExp(`<test-run[^>]*\\b${n}="(\\d+)"`)) || [])[1] ?? -1);
  const failed = [...xml.matchAll(/<test-case[^>]*fullname="([^"]+)"[^>]*result="Failed"/g)].map((m) => m[1]);
  return { total: attr('total'), passed: attr('passed'), failed: attr('failed'), skipped: attr('skipped'), failedNames: failed };
}

function batchTests(platform) {
  const xml = join(ROOT, `TestResults-${platform}.xml`);
  // Delete first: a run that dies on compiler errors leaves the PREVIOUS xml in place,
  // and reporting those stale numbers as this run's result is worse than no result.
  if (existsSync(xml)) rmSync(xml);
  console.log(`[batch] running ${platform} tests (Editor closed, ~60-90s)…`);
  const { code, log } = runUnity(
    ['-runTests', '-batchmode', '-projectPath', ROOT, '-testPlatform', platform, '-testResults', xml],
    platform,
  );
  const res = parseResults(xml);
  if (!res) {
    console.error(`[batch] ${platform}: NO RESULTS (exit ${code}) — see ci-${platform}.log`);
    // compiler errors are the usual cause; surface them instead of making the user grep
    try {
      const errs = [...new Set(readFileSync(log, 'utf8').match(/.*error CS\d+.*/g) || [])];
      errs.slice(0, 15).forEach((e) => console.error(`  ${e.trim()}`));
    } catch { /* no log */ }
    return 1;
  }
  console.log(`[batch] ${platform}: total=${res.total} passed=${res.passed} failed=${res.failed} skipped=${res.skipped}`);
  res.failedNames.forEach((n) => console.log(`  FAIL: ${n}`));
  return res.failed > 0 ? 1 : 0;
}

// ---------------------------------------------------------------- commands

async function cmdStatus() {
  const info = bridgeInfo();
  console.log(`project      : ${ROOT}`);
  console.log(`unity        : ${unityExe()}`);
  console.log(`editor open  : ${editorOpen() ? 'YES (bridge path; batchmode blocked)'
    : staleLock() ? 'no — STALE Temp/UnityLockfile from a crashed Editor (ignored)'
    : 'no (batchmode path)'}`);
  console.log(`mcp bridge   : ${info ? `port ${info.port} (pid ${info.pid})` : 'not running'}`);
  if (!info) return 0;
  try {
    const c = unwrap(await mcpCall('CompilationTools', 'GetCompilationStatus'));
    console.log(`compilation  : ${c.status} (errors: ${c.errorCount})`);
    if (c.errorCount > 0) {
      const e = unwrap(await mcpCall('CompilationTools', 'GetCompilationErrors'));
      console.log(typeof e === 'string' ? e : JSON.stringify(e, null, 2));
      return 1;
    }
  } catch (e) { console.log(`compilation  : unavailable (${e.message})`); }
  return 0;
}

async function bridgeTests(platform) {
  console.log(`[bridge] running ${platform} tests in the open Editor…`);
  const start = unwrap(await mcpCall('TestRunnerTools', 'RunAll', { testPlatform: platform }));
  const runId = start.runId;
  console.log(`[bridge] runId=${runId} queued=${start.testsQueued}`);
  // NEVER use WaitForTestRun here: it holds the HTTP response open while the Editor
  // main thread runs tests, and node's fetch kills it with UND_ERR_HEADERS_TIMEOUT.
  // Short calls + polling is the only reliable shape.
  let res;
  for (let i = 0; i < 120; i++) {
    await new Promise((r) => setTimeout(r, 3000));
    try {
      const r = unwrap(await mcpCall('TestRunnerTools', 'GetResults', { runId }));
      // guard against seeing the PREVIOUS run's (already finished) results
      if (r && r.runId === runId && r.isRunning === false) { res = r; break; }
    } catch { /* Editor busy mid-run — keep polling */ }
  }
  if (!res) { console.error('[bridge] no results'); return 1; }
  // NOTE: the bridge lists every test twice; unique names are the real count.
  const uniq = new Map((res.results || []).map((t) => [t.fullName, t.resultState]));
  const failed = [...uniq].filter(([, s]) => s !== 'Passed');
  console.log(`[bridge] ${platform}: ${uniq.size - failed.length}/${uniq.size} passed (${res.duration?.toFixed(1)}s)`);
  failed.forEach(([n, s]) => console.log(`  ${s}: ${n}`));
  return failed.length ? 1 : 0;
}

async function cmdTest(platform = 'All') {
  const plats = platform === 'All' ? ['EditMode', 'PlayMode'] : [platform];
  let bad = 0;
  for (const p of plats) {
    // PlayMode under the bridge enters play mode and is flaky to await — prefer batch when we can.
    if (bridgeInfo() && editorOpen()) bad |= await bridgeTests(p);
    else bad |= batchTests(p);
  }
  return bad;
}

function cmdSetup() {
  console.log('[batch] rebuilding the rig (RoomPlanner > Setup Measure Rig)…');
  const { code } = runUnity(
    ['-batchmode', '-quit', '-projectPath', ROOT, '-executeMethod', 'RoomPlanner.EditorTools.CiTools.SetupRig'],
    'SetupRig',
  );
  console.log(code === 0 ? '[batch] rig rebuilt and scene saved' : `[batch] FAILED (exit ${code}) — see ci-SetupRig.log`);
  return code === 0 ? 0 : 1;
}

function cmdBuild() {
  console.log('[batch] building Quest APK → Build/MRRoomPlanner.apk …');
  const { code } = runUnity(
    ['-batchmode', '-quit', '-projectPath', ROOT, '-executeMethod', 'RoomPlanner.EditorTools.CiTools.BuildAndroid'],
    'BuildAndroid', 45,
  );
  console.log(code === 0 ? '[batch] APK built' : `[batch] FAILED (exit ${code}) — see ci-BuildAndroid.log`);
  return code === 0 ? 0 : 1;
}

async function cmdScene(pattern) {
  if (!pattern) die('usage: driver.mjs scene <name-pattern>');
  console.log(unwrap(await mcpCall('SceneObjectsTools', 'SearchGameObjects', { searchPattern: pattern })));
  return 0;
}

async function cmdInspect(id) {
  if (!id) die('usage: driver.mjs inspect <instanceId>');
  console.log(unwrap(await mcpCall('SceneObjectsTools', 'InspectGameObject', { instanceId: String(id) })));
  return 0;
}

async function cmdErrors() {
  console.log(unwrap(await mcpCall('DiagnosticTools', 'GetErrorSummary')));
  return 0;
}

async function cmdShot(win = 'SceneView', out) {
  const dir = join(ROOT, 'ci-shots');
  mkdirSync(dir, { recursive: true });
  const file = out ? resolve(out) : join(dir, `${win}.png`);
  const r = await mcpCall('UIVerificationTools', 'CaptureWindow', { typeName: win });
  const val = unwrap(r);
  // the payload is base64 PNG, possibly wrapped in an object
  // the bridge returns it as `base64Png`; keep fallbacks for other shapes
  const b64 = typeof val === 'string'
    ? val
    : (val.base64Png || val.base64 || val.image || val.png || val.data || '');
  const clean = String(b64).replace(/^data:image\/png;base64,/, '').replace(/\s+/g, '');
  if (clean.length < 100) die(`No image returned for '${win}'. Raw: ${JSON.stringify(val).slice(0, 300)}`);
  writeFileSync(file, Buffer.from(clean, 'base64'));
  console.log(`saved ${file} (${(clean.length * 0.75 / 1024).toFixed(0)} KB)`);
  return 0;
}

async function cmdMcp(tool, method, ...kv) {
  if (!tool || !method) die('usage: driver.mjs mcp <Tool> <method> [key=value ...]');
  const args = Object.fromEntries(kv.map((s) => { const i = s.indexOf('='); return [s.slice(0, i), s.slice(i + 1)]; }));
  const r = unwrap(await mcpCall(tool, method, args));
  console.log(typeof r === 'string' ? r : JSON.stringify(r, null, 2));
  return 0;
}

function die(msg) { console.error(msg); process.exit(1); }

// ---------------------------------------------------------------- main

const [cmd, ...rest] = process.argv.slice(2);
const table = {
  status: cmdStatus,
  test: () => cmdTest(rest[0]),
  setup: cmdSetup,
  build: cmdBuild,
  scene: () => cmdScene(rest[0]),
  inspect: () => cmdInspect(rest[0]),
  errors: cmdErrors,
  shot: () => cmdShot(rest[0], rest[1]),
  mcp: () => cmdMcp(...rest),
};

if (!cmd || !table[cmd]) {
  console.log(readFileSync(fileURLToPath(import.meta.url), 'utf8').split('*/')[0].split('/**')[1]);
  process.exit(cmd ? 1 : 0);
}
process.exit((await table[cmd]()) ?? 0);
