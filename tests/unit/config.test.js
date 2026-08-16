// tests/unit/config.test.js — _config.js 단위 테스트
// 실행: node tests/unit/config.test.js

const assert = require('assert');
const config = require('../../.claude/hooks/_config');

let passed = 0, failed = 0;
function test(name, fn) {
  try { fn(); passed++; console.log(`  ✅ ${name}`); }
  catch (e) { failed++; console.log(`  ❌ ${name}: ${e.message}`); }
}

// ── deepMerge ──────────────────────────────────────────────────────

console.log('\n── deepMerge ──');

test('flat merge', () => {
  const result = config.deepMerge({ a: 1, b: 2 }, { b: 3, c: 4 });
  assert.strictEqual(result.a, 1);
  assert.strictEqual(result.b, 3);
  assert.strictEqual(result.c, 4);
});

test('nested merge', () => {
  const result = config.deepMerge(
    { x: { a: 1, b: 2 } },
    { x: { b: 3, c: 4 } }
  );
  assert.strictEqual(result.x.a, 1);
  assert.strictEqual(result.x.b, 3);
  assert.strictEqual(result.x.c, 4);
});

test('array replaces (not merges)', () => {
  const result = config.deepMerge({ arr: [1, 2] }, { arr: [3] });
  assert.deepStrictEqual(result.arr, [3]);
});

test('null/undefined in source skipped', () => {
  const result = config.deepMerge({ a: 1, b: 2 }, { a: null, b: undefined });
  assert.strictEqual(result.a, 1);
  assert.strictEqual(result.b, 2);
});

test('original not mutated', () => {
  const original = { a: { b: 1 } };
  config.deepMerge(original, { a: { c: 2 } });
  assert.strictEqual(original.a.c, undefined);
});

// ── CONFIG_DEFAULTS ──────────────────────────────────────────────

console.log('\n── CONFIG_DEFAULTS ──');

test('has project section', () => {
  assert(config.CONFIG_DEFAULTS.project);
  assert.strictEqual(typeof config.CONFIG_DEFAULTS.project.name, 'string');
});

test('has commands section', () => {
  assert(config.CONFIG_DEFAULTS.commands);
  assert.strictEqual(typeof config.CONFIG_DEFAULTS.commands.test_command, 'string');
});

test('has thresholds section', () => {
  assert(config.CONFIG_DEFAULTS.thresholds);
  assert.strictEqual(config.CONFIG_DEFAULTS.thresholds.max_auto_fix, 3);
});

test('has directories section', () => {
  assert(config.CONFIG_DEFAULTS.directories);
  assert.strictEqual(config.CONFIG_DEFAULTS.directories.agents_dir, '.claude/agents');
});

test('has features section', () => {
  assert(config.CONFIG_DEFAULTS.features);
  assert.strictEqual(config.CONFIG_DEFAULTS.features.auto_lint, true);
});

test('source_dirs defaults to [src/]', () => {
  assert.deepStrictEqual(config.CONFIG_DEFAULTS.source_dirs, ['src/']);
});

// ── loadConfig ──────────────────────────────────────────────────

console.log('\n── loadConfig ──');

test('returns object', () => {
  const cfg = config.loadConfig(true);
  assert(typeof cfg === 'object');
});

test('has project.name from CLAUDE.md', () => {
  const cfg = config.loadConfig(true);
  assert.strictEqual(typeof cfg.project.name, 'string');
});

test('has thresholds with numbers', () => {
  const cfg = config.loadConfig(true);
  assert(typeof cfg.thresholds.max_auto_fix === 'number');
  assert(cfg.thresholds.max_auto_fix >= 0);
});

test('cached loadConfig returns same object', () => {
  const cfg1 = config.loadConfig(true);
  const cfg2 = config.loadConfig(); // cached
  assert.strictEqual(cfg1, cfg2);
});

// ── getConfigValue ──────────────────────────────────────────────

console.log('\n── getConfigValue ──');

test('dot path resolves', () => {
  const val = config.getConfigValue('thresholds.max_auto_fix', 99);
  assert(typeof val === 'number');
});

test('missing path returns default', () => {
  const val = config.getConfigValue('nonexistent.path.deep', 'fallback');
  assert.strictEqual(val, 'fallback');
});

test('empty string path returns default', () => {
  const val = config.getConfigValue('', 'fallback');
  assert.strictEqual(val, 'fallback');
});

test('partial path returns default', () => {
  const val = config.getConfigValue('thresholds.nonexistent', 42);
  assert.strictEqual(val, 42);
});

// ── getSourceDirs ──────────────────────────────────────────────

console.log('\n── getSourceDirs ──');

test('returns array', () => {
  const dirs = config.getSourceDirs();
  assert(Array.isArray(dirs));
  assert(dirs.length > 0);
});

// ── isSourcePath ──────────────────────────────────────────────

console.log('\n── isSourcePath ──');

test('src/ path matches', () => {
  assert(config.isSourcePath('/project/src/file.js'));
});

test('docs/ path does not match', () => {
  assert(!config.isSourcePath('/project/docs/file.md'));
});

test('case insensitive', () => {
  assert(config.isSourcePath('/project/SRC/File.js'));
});

// ── isSourceExtension ──────────────────────────────────────────

console.log('\n── isSourceExtension ──');

test('.js is source', () => { assert(config.isSourceExtension('file.js')); });
test('.ts is source', () => { assert(config.isSourceExtension('file.ts')); });
test('.py is source', () => { assert(config.isSourceExtension('app.py')); });
test('.cs is source', () => { assert(config.isSourceExtension('Program.cs')); });
test('.go is source', () => { assert(config.isSourceExtension('main.go')); });
test('.rs is source', () => { assert(config.isSourceExtension('lib.rs')); });
test('.java is source', () => { assert(config.isSourceExtension('App.java')); });
test('.md is not source', () => { assert(!config.isSourceExtension('README.md')); });
test('.json is not source', () => { assert(!config.isSourceExtension('package.json')); });
test('.yaml is not source', () => { assert(!config.isSourceExtension('config.yaml')); });

// ── readEnvConfig ──────────────────────────────────────────────

console.log('\n── readEnvConfig ──');

test('reads CLAUDE_MAX_AUTO_FIX env', () => {
  const orig = process.env.CLAUDE_MAX_AUTO_FIX;
  process.env.CLAUDE_MAX_AUTO_FIX = '7';
  const env = config.readEnvConfig();
  assert.strictEqual(env.thresholds.max_auto_fix, 7);
  if (orig !== undefined) process.env.CLAUDE_MAX_AUTO_FIX = orig;
  else delete process.env.CLAUDE_MAX_AUTO_FIX;
});

test('ignores non-numeric env values', () => {
  const orig = process.env.CLAUDE_MAX_AUTO_FIX;
  process.env.CLAUDE_MAX_AUTO_FIX = 'abc';
  const env = config.readEnvConfig();
  assert.strictEqual(env.thresholds.max_auto_fix, undefined);
  if (orig !== undefined) process.env.CLAUDE_MAX_AUTO_FIX = orig;
  else delete process.env.CLAUDE_MAX_AUTO_FIX;
});

test('reads CLAUDE_SOURCE_DIRS env', () => {
  const orig = process.env.CLAUDE_SOURCE_DIRS;
  process.env.CLAUDE_SOURCE_DIRS = 'lib/, app/';
  const env = config.readEnvConfig();
  assert.deepStrictEqual(env.source_dirs, ['lib/', 'app/']);
  if (orig !== undefined) process.env.CLAUDE_SOURCE_DIRS = orig;
  else delete process.env.CLAUDE_SOURCE_DIRS;
});

// ── 결과 ──────────────────────────────────────────────────────────

console.log(`\n══ 결과: ${passed} passed, ${failed} failed ══`);
if (failed > 0) process.exit(1);
