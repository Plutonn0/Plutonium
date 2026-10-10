/** Keep transaction and cleanup failures separate; never log database errors or SQL values. */
export async function withTransaction(pool, callback) {
  const db = await pool.connect();
  let failed = false;
  let discardConnection;
  try {
    await db.query('BEGIN');
    const result = await callback(db);
    await db.query('COMMIT');
    return result;
  } catch (error) {
    failed = true;
    try {
      await db.query('ROLLBACK');
    } catch (rollbackError) {
      discardConnection = rollbackError;
      console.error(JSON.stringify({ event: 'moderation_rollback_failed' }));
    }
    throw error;
  } finally {
    try {
      // A failed rollback leaves an unknown transaction state: do not reuse that connection.
      db.release(discardConnection);
    } catch (releaseError) {
      if (!failed) throw releaseError;
      console.error(JSON.stringify({ event: 'moderation_connection_release_failed' }));
    }
  }
}
