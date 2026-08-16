// tests/unit/state.test.js — _state.js 단위 테스트
// 실행: node tests/unit/state.test.js

const assert = require('assert');
const fs = require('fs');
const path = require('path');

// _state.js는 process.cwd() 기준으로 STATE_FILE을 결정하므로
// 임시 디렉토리에서 실행하도록 세팅
const TEST_DIR = path.join(__dirname, '_tmp_state_test');
const DOCS_MEMORY = path.join(TEST_DIR, 'docs', 'memory');
const STATE_PATH = path.join(DOCS_MEMORY, 'pipeline-state.json');

let passed = 0, failed = 0;
function test(name, fn) {
  try { fn(); passed++; console.log(`  ✅ ${name}`); }
  catch (e) { failed++; console.log(`  ❌ ${name}: ${e.message}`); }
}

// ── 테스트 유틸 ──────────────────────────────────────────────────
function rmRecursive(p) {
  if (!fs.existsSync(p)) return;
  if (fs.statSync(p).isDirectory()) {
    for (const f of fs.readdirSync(p)) rmRecursive(path.join(p, f));
    fs.rmdirSync(p);
  } else {
    fs.unlinkSync(p);
  }
}

function setup() {
  rmRecursive(TEST_DIR);
  fs.mkdirSync(DOCS_MEMORY, { recursive: true });
  // _state.js는 require 시점에 process.cwd() 기반 경로를 설정하므로
  // 직접 모듈의 함수를 테스트할 수 있도록 validateState만 단독 테스트
}

function teardown() {
  rmRecursive(TEST_DIR);
}

// ── validateState 직접 테스트 (순수 함수) ──────────────────────────
// _state.js를 require하면 cwd 기반 초기화가 일어나므로
// validateState만 추출하여 테스트
const state = require('../../.claude/hooks/_state');

console.log('\n── validateState ──');

test('valid state passes', () => {
  const result = state.validateState({
    phase: 'dev', track: 'C', autoFixCount: 0
  });
  assert(result.valid);
  assert.strictEqual(result.errors.length, 0);
});

test('null state fails', () => {
  const result = state.validateState(null);
  assert(!result.valid);
});

test('undefined state fails', () => {
  const result = state.validateState(undefined);
  assert(!result.valid);
});

test('invalid phase fails', () => {
  const result = state.validateState({ phase: 'invalid', track: 'C' });
  assert(!result.valid);
  assert(result.errors.some(e => e.includes('phase')));
});

test('invalid track fails', () => {
  const result = state.validateState({ phase: 'dev', track: 'Z' });
  assert(!result.valid);
  assert(result.errors.some(e => e.includes('track')));
});

test('null track is valid', () => {
  const result = state.validateState({ phase: 'setup', track: null });
  assert(result.valid);
});

test('negative autoFixCount fails', () => {
  const result = state.validateState({ phase: 'dev', track: 'B', autoFixCount: -1 });
  assert(!result.valid);
});

test('all valid phases accepted', () => {
  for (const phase of state.VALID_PHASES) {
    const result = state.validateState({ phase, track: 'C' });
    assert(result.valid, `phase '${phase}' should be valid`);
  }
});

test('all valid tracks accepted', () => {
  for (const track of state.VALID_TRACKS) {
    const result = state.validateState({ phase: 'dev', track });
    assert(result.valid, `track '${track}' should be valid`);
  }
});

// ── DEFAULT_STATE ──────────────────────────────────────────────────

console.log('\n── DEFAULT_STATE ──');

test('DEFAULT_STATE is valid', () => {
  const result = state.validateState(state.DEFAULT_STATE);
  assert(result.valid);
});

test('DEFAULT_STATE phase is setup', () => {
  assert.strictEqual(state.DEFAULT_STATE.phase, 'setup');
});

test('DEFAULT_STATE track is null', () => {
  assert.strictEqual(state.DEFAULT_STATE.track, null);
});

test('DEFAULT_STATE autoFixCount is 0', () => {
  assert.strictEqual(state.DEFAULT_STATE.autoFixCount, 0);
});

// ── loadState / saveState (파일 I/O) ────────────────────���─────────
// 이 테스트는 실제 프로젝트의 pipeline-state.json을 사용
// _state.js가 모듈 로드 시점에 경로를 고정하므로 현재 프로젝트 기준 동작

console.log('\n── loadState ──');

test('loadState returns object with phase', () => {
  const loaded = state.loadState();
  assert(typeof loaded === 'object');
  assert(state.VALID_PHASES.includes(loaded.phase));
});

test('loadState returns object with track', () => {
  const loaded = state.loadState();
  assert(state.VALID_TRACKS.includes(loaded.track));
});

// ── VALID_PHASES / VALID_TRACKS 상수 ──────────────────────────────

console.log('\n── Constants ──');

test('VALID_PHASES has 8 entries', () => {
  assert.strictEqual(state.VALID_PHASES.length, 8);
});

test('VALID_TRACKS has 4 entries', () => {
  assert.strictEqual(state.VALID_TRACKS.length, 4);
});

test('VALID_PHASES includes setup and complete', () => {
  assert(state.VALID_PHASES.includes('setup'));
  assert(state.VALID_PHASES.includes('complete'));
});

// ── 결과 ──────────────────────────────────────────────────────────

console.log(`\n══ 결과: ${passed} passed, ${failed} failed ══`);
if (failed > 0) process.exit(1);
