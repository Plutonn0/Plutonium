import { test } from 'node:test';
import assert from 'node:assert/strict';
import { withTransaction } from '../lib/transaction.js';

function fixture(failures = {}) {
  const calls = [];
  const db = {
    async query(command) {
      calls.push(command);
      if (failures[command]) throw failures[command];
    },
    release(error) {
      calls.push(['release', error]);
      if (failures.release) throw failures.release;
    },
  };
  return { calls, db, pool: { connect: async () => db } };
}

test('a successful transaction commits its result and releases once', async () => {
  const { calls, db, pool } = fixture();
  const result = await withTransaction(pool, async (connection) => {
    assert.equal(connection, db);
    await connection.query('WRITE');
    return { ok: true };
  });
  assert.deepEqual(result, { ok: true });
  assert.deepEqual(calls, ['BEGIN', 'WRITE', 'COMMIT', ['release', undefined]]);
});

test('rollback and release failures cannot replace the original error or leak its contents', async (t) => {
  const logs = [];
  t.mock.method(console, 'error', (message) => logs.push(message));
  const original = new Error('private-original');
  const rollback = new Error('private-database-secret');
  const { calls, pool } = fixture({ ROLLBACK: rollback, release: new Error('private-release') });
  await assert.rejects(
    withTransaction(pool, async () => {
      throw original;
    }),
    (error) => error === original,
  );
  assert.deepEqual(calls, ['BEGIN', 'ROLLBACK', ['release', rollback]]);
  assert.equal(logs.length, 2);
  assert.ok(logs.every((line) => !line.includes('private-')));
});

test('begin and commit failures roll back and release the connection', async () => {
  for (const command of ['BEGIN', 'COMMIT']) {
    const original = new Error(command);
    const { calls, pool } = fixture({ [command]: original });
    await assert.rejects(
      withTransaction(pool, async () => 1),
      (error) => error === original,
    );
    assert.equal(calls.at(-2), 'ROLLBACK');
    assert.deepEqual(calls.at(-1), ['release', undefined]);
  }
});

test('a release error after a successful commit is still reported', async () => {
  const original = new Error('release failed');
  const { pool } = fixture({ release: original });
  await assert.rejects(
    withTransaction(pool, async () => 1),
    (error) => error === original,
  );
});
