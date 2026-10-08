import {test} from 'node:test';
import assert from 'node:assert/strict';
import {dailySeries} from '../lib/analytics.js';
test('analytics fills missing UTC dates, preserves zero and excludes older data',()=>{
 const points=dailySeries([{day:'2026-10-08',count:4},{day:'2026-09-25',count:2},{day:'2026-09-24',count:99}],[{day:'2026-10-07',count:3}],new Date('2026-10-08T23:59:59Z'));
 assert.equal(points.length,14);assert.deepEqual(points[0],{day:'2026-09-25',active:2,installs:0});
 assert.deepEqual(points.at(-1),{day:'2026-10-08',active:4,installs:0});assert.equal(points.at(-2).installs,3);
 assert.equal(points[1].active,0);
});
