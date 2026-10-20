import { test } from 'node:test';
import { readFileSync } from 'node:fs';
import assert from 'node:assert/strict';
import { Mobile, validateMobilePage } from '../dist/index.js';

test('mobile declarations serialize as data and builders preserve prior values', () => {
  const action = Mobile.rpc('quantum.plugin.notes.notes.create');
  const bound = action.constant('notebook', 'inbox').input('text', 'content');
  const page = Mobile.page('notes', 'Notes', Mobile.vStack('root', Mobile.textField('content').limit(500), Mobile.button('save', 'Save').onClick(bound)));
  validateMobilePage(page);
  assert.deepEqual(action.constants, {});
  const wire = JSON.parse(JSON.stringify(page));
  assert.equal(wire.root.children[1].action.inputs.text, 'content');
  assert.equal(wire.root.children[1].action.constants.notebook, 'inbox');
});
test('mobile validation rejects duplicate nodes, bad mappings, and executable node kinds', () => {
  assert.throws(() => validateMobilePage(Mobile.page('p', 'Page', Mobile.vStack('root', Mobile.text('x', '1'), Mobile.text('x', '2')))));
  assert.throws(() => validateMobilePage(Mobile.page('p', 'Page', Mobile.button('save', 'Save').onClick(Mobile.rpc('p.s.m').input('text', 'missing')))));
  assert.throws(() => validateMobilePage(Mobile.page('p', 'Page', { type: 'Script', id: 'script' })));
});

test('version-one fixture and .NET nullable defaults use the same declaration contract', () => {
  const page = JSON.parse(readFileSync(new URL('../../fixtures/mobile-page-v1.json', import.meta.url), 'utf8'));
  validateMobilePage(page);
  assert.equal(page.root.children[3].action.inputs.text, 'content');
  assert.equal(page.root.children[3].action.inputs.pinned, 'pinned');
  const dotnetDefaults = { type: 'Text', id: 'text', text: null, placeholder: null, style: 'body', maxLength: 500, enabled: true, actionId: null, inputIds: [], action: null, children: [] };
  validateMobilePage(Mobile.page('p', 'Page', dotnetDefaults));
  assert.throws(() => validateMobilePage({ ...page, schemaVersion: 2 }));
  assert.throws(() => validateMobilePage(Mobile.page('p', 'Page', { ...dotnetDefaults, enabled: 'false' })));
});
