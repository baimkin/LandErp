// Executes the production script with controlled layout/visibility, without a browser.
const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const source = fs.readFileSync('src/LandErp.ParserSpike/LocalCollection/DomSourcePage.cs', 'utf8');
const script = source.match(/public const string MapSelectionScript = """\r?\n([\s\S]*?)\r?\n\s*""";/)[1];
const fixture = JSON.parse(fs.readFileSync('tests/LandErp.ParserSpike.Tests/Fixtures/avito-map-mfe-shape.json', 'utf8'));
let count = 0;
function read(options = {}, diagnostic = false) {
  const settings = { marker: fixture.mapMarker, tiles: true, canvas: false, reset: true, ...options };
  function element(width = 800, height = 600, overrides = {}) {
    return {
      parentElement: null, style: {}, disabled: false, textContent: '',
      getAttribute: () => null,
      getBoundingClientRect: () => ({ width, height, left: 0, top: 0, right: width, bottom: height }),
      ...overrides
    };
  }
  const root = element(settings.small ? 100 : 800, 600);
  if (settings.parentHidden) root.parentElement = element(800, 600, { tagName: 'DIV', style: { opacity: '0' } });
  if (settings.opacityZero) root.style.opacity = '0';
  if (settings.hidden) root.style.display = 'none';
  if (settings.offscreen) root.getBoundingClientRect = () => ({ width: 800, height: 600, left: 1100, right: 1900, top: 0, bottom: 600 });
  const tile = element(256, 256, { parentElement: root, complete: !settings.pending, naturalWidth: settings.broken ? 0 : 256 });
  if (settings.hiddenTile) tile.style.visibility = 'hidden';
  const canvas = element(settings.smallSurface ? 10 : 800, 600, { parentElement: root });
  const reset = element(180, 35, { parentElement: root, textContent: fixture.resetText, disabled: !!settings.disabledReset });
  if (settings.hiddenReset) reset.style.display = 'none';
  const loader = element(30, 30, { parentElement: root });
  root.querySelectorAll = selector => {
    if (selector === 'canvas,svg') return settings.canvas ? [canvas] : [];
    if (selector === '.ymaps3--tile-layer img') return settings.tiles ? [tile] : [];
    if (selector.startsWith('button,')) return settings.reset ? [reset] : [];
    return settings.loading ? [loader] : [];
  };
  const result = vm.runInNewContext('(' + script + ')(' + diagnostic + ')', {
    innerWidth: 1000, innerHeight: 800,
    getComputedStyle: e => ({ display: 'block', visibility: 'visible', opacity: '1', ...e.style }),
    document: {
      readyState: settings.documentLoading ? 'loading' : (settings.readyState || 'complete'),
      querySelectorAll: selector => selector.includes('[data-marker="' + settings.marker + '"]') ? [root] : []
    }
  });
  return JSON.parse(result);
}
function check(options, expected) {
  const state = read(options);
  const explained = read(options, true);
  for (const key of ['MapVisible', 'SurfaceVisible', 'ResetVisible', 'Loading']) assert.equal(state[key], explained[key], key + ' must match diagnostic evaluation');
  assert.equal(state.Diagnostic, undefined);
  for (const [key, value] of Object.entries(expected)) assert.equal(state[key], value, JSON.stringify(options) + ' ' + key);
  count++;
}
check({}, { MapVisible: true, SurfaceVisible: true, ResetVisible: true, Loading: false });
for (const marker of ['map-view', 'map-full']) {
  check({ marker, canvas: true, tiles: false }, { MapVisible: true, SurfaceVisible: true, ResetVisible: true });
}
check({ marker: 'unrelated' }, { MapVisible: false, SurfaceVisible: false });
check({ hidden: true }, { MapVisible: false, SurfaceVisible: false, ResetVisible: false });
check({ offscreen: true }, { MapVisible: false, SurfaceVisible: false });
check({ hiddenTile: true }, { SurfaceVisible: false });
check({ pending: true }, { SurfaceVisible: false });
check({ broken: true }, { SurfaceVisible: false });
check({ small: true }, { SurfaceVisible: false });
check({ tiles: false }, { SurfaceVisible: false });
check({ tiles: false, canvas: true, smallSurface: true }, { SurfaceVisible: false });
check({ reset: false }, { ResetVisible: false });
check({ disabledReset: true }, { ResetVisible: false });
check({ hiddenReset: true }, { ResetVisible: false });
check({ documentLoading: true }, { MapVisible: false });
check({ loading: true }, { Loading: true });
check({ parentHidden: true }, { MapVisible: false, SurfaceVisible: false, ResetVisible: false });
check({ opacityZero: true }, { MapVisible: false });
check({ readyState: 'interactive' }, { MapVisible: true, SurfaceVisible: true, ResetVisible: true, Loading: false });
for (const options of [
  { hidden: true }, { tiles: false }, { reset: false }, { disabledReset: true },
  { loading: true }, { pending: true }, { broken: true }
]) {
  const state = read({ readyState: 'interactive', ...options }, true);
  assert.equal(state.MapVisible && state.SurfaceVisible && state.ResetVisible && !state.Loading, false, JSON.stringify(options));
  count++;
}
assert.equal(read({ parentHidden: true }, true).Diagnostic.Maps[0].Map.Rejection.Code, 'OPACITY_ZERO');
assert.equal(read({ parentHidden: true }, true).Diagnostic.Maps[0].Map.Rejection.AncestorDepth, 1);
assert.equal(read({ hidden: true }, true).Diagnostic.Maps[0].Map.Rejection.Code, 'DISPLAY_NONE');
assert.equal(read({ offscreen: true }, true).Diagnostic.Maps[0].Map.Rejection.Code, 'OUTSIDE_VIEWPORT');
assert.equal(read({ hiddenReset: true }, true).Diagnostic.Maps[0].ResetButtons[0].Rejection.Code, 'DISPLAY_NONE');
assert.equal(read({ disabledReset: true }, true).Diagnostic.Maps[0].ResetButtons[0].Disabled, true);
assert.equal(read({ hiddenTile: true }, true).Diagnostic.Maps[0].Tiles[0].Rejection.Code, 'VISIBILITY_HIDDEN');
assert.equal(read({ broken: true }, true).Diagnostic.Maps[0].Tiles[0].NaturalWidth, 0);
assert.equal(read({ reset: false }, true).Diagnostic.Maps[0].ResetCount, 0);
count += 9;
console.log(count + '/' + count + ' map selection scenarios passed');
