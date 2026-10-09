import { ApiError, authorize, digest, token, roleFor, verifyOwner, requireOwnerConfig, validateConfig, validateAppeal, features } from './security.js';
import { dailySeries } from './analytics.js';

export function createService(pool, env, fetcher = fetch, ownerVerifier = verifyOwner) {
  async function upstream(url, options) {
    try { return await fetcher(url, { ...options, signal: AbortSignal.timeout(12000), redirect: 'error' }); }
    catch { throw new ApiError(503, 'Identity service is temporarily unavailable. Try again.'); }
  }
  async function limit(key, maximum, seconds = 60) {
    const bucket = Math.floor(Date.now() / (seconds * 1000));
    const { rows } = await pool.query('INSERT INTO rate_limits(key,count,expires) VALUES($1,1,$2) ON CONFLICT(key) DO UPDATE SET count=rate_limits.count+1 RETURNING count', [digest(`${key}:${bucket}`), new Date((bucket + 2) * seconds * 1000)]);
    if (rows[0].count > maximum) throw new ApiError(429, 'Too many requests. Please wait before trying again.');
  }
  async function principal(bearer, db = pool, lock = false) {
    if (!/^[A-Za-z0-9_-]{43}$/.test(bearer ?? '')) throw new ApiError(401, 'Sign in to continue.');
    const { rows } = await db.query(`SELECT a.*,s.owner_until,s.hash,s.owner_device_hash FROM sessions s JOIN accounts a ON a.uuid=s.uuid WHERE s.hash=$1 AND s.expires>now()${lock ? ' FOR UPDATE' : ''}`, [digest(bearer)]);
    if (!rows[0]) throw new ApiError(401, 'Session expired. Sign in again.');
    if(rows[0].owner_device_hash) {
      const trusted=(await db.query('SELECT hash FROM owner_devices WHERE hash=$1 AND uuid=$2 AND microsoft_oid=$3',[rows[0].owner_device_hash,env.OWNER_MINECRAFT_UUID,env.OWNER_MICROSOFT_OID])).rows;
      if(!trusted.length) rows[0].owner_until=null;
    }
    return { ...rows[0], access: roleFor(rows[0], rows[0], env) };
  }
  async function configuration(db = pool) {
    const { rows } = await db.query('SELECT value,revision FROM configuration WHERE id=1');
    if (!rows[0]) throw new ApiError(503, 'Moderation database needs migration.');
    return { ...rows[0].value, revision: rows[0].revision };
  }
  async function audit(db, actor, action, target, details = {}) {
    await db.query('INSERT INTO audit(actor,action,target,details) VALUES($1,$2,$3,$4)', [actor.uuid, action, target, JSON.stringify(details)]);
  }
  async function mutation(bearer, permission, callback) {
    const db = await pool.connect();
    try { await db.query('BEGIN'); const actor = await principal(bearer, db, true); authorize(actor.access, permission); const result = await callback(db, actor); await db.query('COMMIT'); return result; }
    catch (error) { await db.query('ROLLBACK'); throw error; } finally { db.release(); }
  }
  async function protectedTarget(db, uuid, actor) {
    if (!/^[a-f0-9]{32}$/.test(uuid ?? '')) throw new ApiError(400, 'Invalid account ID.');
    const target = (await db.query('SELECT * FROM accounts WHERE uuid=$1 FOR UPDATE', [uuid])).rows[0];
    if (!target) throw new ApiError(404, 'Account not found.');
    if (uuid === env.OWNER_MINECRAFT_UUID || uuid === actor.uuid || target.role === 'admin' && actor.access !== 'owner') throw new ApiError(403, 'This account is protected.');
    return target;
  }
  async function restrict(db, actor, uuid, disabled, reason) {
    await protectedTarget(db, uuid, actor);
    await db.query('UPDATE accounts SET disabled=$2,reason=$3 WHERE uuid=$1', [uuid, disabled, disabled ? reason : '']);
    // Revoke elevated sessions immediately, but ordinary sessions may still view restrictions and appeal.
    if (disabled) await db.query('UPDATE sessions SET owner_until=NULL WHERE uuid=$1', [uuid]);
    await audit(db, actor, disabled ? 'account.disable' : 'account.restore', uuid, { reason });
  }
  return async function handle(path, method, body, bearer, ip = 'unknown') {
    // Public configuration polling has no session or mutation side effects.
    if (path === 'config' && method === 'GET') return configuration();
    await limit(`ip:${ip}`, 600);
    if (path === 'session' && method === 'POST') {
      await limit(`login:${ip}`, 12);
      if (typeof body.minecraftToken !== 'string' || body.minecraftToken.length > 8192 || !body.minecraftToken) throw new ApiError(400, 'A Minecraft session is required.');
      const result = await upstream('https://api.minecraftservices.com/minecraft/profile', { headers: { Authorization: `Bearer ${body.minecraftToken}` } });
      if (!result.ok) throw new ApiError(result.status === 401 || result.status === 403 ? 401 : 503, 'Minecraft could not verify your account. Sign in again.');
      const profile = await result.json();
      if (!/^[a-f0-9]{32}$/.test(profile.id ?? '') || !/^[A-Za-z0-9_]{3,16}$/.test(profile.name ?? '')) throw new ApiError(502, 'Invalid Minecraft profile response.');
      await pool.query('INSERT INTO accounts(uuid,username) VALUES($1,$2) ON CONFLICT(uuid) DO UPDATE SET username=$2,last_seen=now()', [profile.id, profile.name]);
      const session = token();
      await pool.query('INSERT INTO sessions(hash,uuid,expires) VALUES($1,$2,$3)', [digest(session), profile.id, new Date(Date.now() + 30 * 60 * 1000)]);
      // Bounded housekeeping. No authentication payloads or raw IPs are stored.
      await pool.query('DELETE FROM sessions WHERE expires<now()');
      await pool.query('DELETE FROM rate_limits WHERE expires<now()');
      const me = await principal(session);
      return { token: session, expiresIn: 1800, username: me.username, uuid: me.uuid, role: me.access, disabled: me.disabled };
    }
    if (path === 'appeals' && method === 'POST') {
      await limit(`appeal:${ip}`, 3, 3600);
      const appeal = validateAppeal(body);
      if (!env.TURNSTILE_SECRET_KEY) throw new ApiError(503, 'Website appeals are not configured.');
      if (typeof body.captcha !== 'string' || !body.captcha.trim() || body.captcha.length > 4096) throw new ApiError(400, 'Complete the verification below the form before sending your appeal.');
      const response = await upstream('https://challenges.cloudflare.com/turnstile/v0/siteverify', { method: 'POST', body: new URLSearchParams({ secret: env.TURNSTILE_SECRET_KEY, response: body.captcha }) });
      const check = await response.json();
      if (!check.success || check.hostname !== env.APPEAL_HOSTNAME || check.action !== 'appeal') throw new ApiError(400, 'Please complete the appeal verification again.');
      const users = (await pool.query('SELECT uuid,username FROM accounts WHERE lower(username)=lower($1) AND disabled=true', [appeal.username])).rows;
      // Same response whether or not an account exists; a submission never restores access automatically.
      if (users.length === 1) await pool.query('INSERT INTO appeals(uuid,username,explanation) VALUES($1,$2,$3) ON CONFLICT DO NOTHING', [users[0].uuid, users[0].username, appeal.explanation]);
      return { message: 'If this username has a restriction, your appeal has been added for review.' };
    }
    const me = await principal(bearer);
    await limit(`session:${me.hash}`, 120);
    if (path === 'session' && method === 'DELETE') { await pool.query('DELETE FROM sessions WHERE hash=$1', [me.hash]); return { ok: true }; }
    if (path === 'me' && method === 'GET') return { username: me.username, uuid: me.uuid, role: me.access, disabled: me.disabled, reason: me.reason, ownerCandidate: me.uuid === env.OWNER_MINECRAFT_UUID };
    if(path==='owner/device/register' && method==='POST') return mutation(bearer,'owner',async(db,actor)=>{
      requireOwnerConfig(env);
      const credential=token();
      await db.query('INSERT INTO owner_devices(hash,uuid,microsoft_oid) VALUES($1,$2,$3)',[digest(credential),actor.uuid,env.OWNER_MICROSOFT_OID]);
      await db.query('UPDATE sessions SET owner_device_hash=$2 WHERE hash=$1',[actor.hash,digest(credential)]);
      await audit(db,actor,'owner.device.register',actor.uuid);
      return {deviceToken:credential};
    });
    if(path==='owner/device/resume' && method==='POST') {
      requireOwnerConfig(env);await limit(`device:${me.uuid}`,6);
      if(me.disabled||me.uuid!==env.OWNER_MINECRAFT_UUID||!/^[A-Za-z0-9_-]{43}$/.test(body.deviceToken??'')) throw new ApiError(403,'Verify the owner account on this device.');
      const device=(await pool.query('SELECT hash FROM owner_devices WHERE hash=$1 AND uuid=$2 AND microsoft_oid=$3',[digest(body.deviceToken),me.uuid,env.OWNER_MICROSOFT_OID])).rows[0];
      if(!device) throw new ApiError(403,'This device is no longer trusted. Verify with Microsoft again.');
      await pool.query('UPDATE sessions SET owner_until=$2,expires=$2,owner_device_hash=$3 WHERE hash=$1',[me.hash,new Date(Date.now()+7200000),device.hash]);
      await pool.query('UPDATE owner_devices SET last_used=now() WHERE hash=$1',[device.hash]);
      return {pending:false,role:'owner',sessionExpiresIn:7200};
    }
    if(path==='owner/device/forget' && method==='POST') {
      if(!/^[A-Za-z0-9_-]{43}$/.test(body.deviceToken??''))throw new ApiError(400,'Invalid device credential.');
      await pool.query('DELETE FROM owner_devices WHERE hash=$1 AND uuid=$2',[digest(body.deviceToken),me.uuid]);
      return {ok:true};
    }
    if(path==='owner/devices/revoke-all' && method==='POST') return mutation(bearer,'owner',async(db,actor)=>{
      await db.query('DELETE FROM owner_devices WHERE uuid=$1',[actor.uuid]);
      await db.query('UPDATE sessions SET owner_until=NULL WHERE uuid=$1',[actor.uuid]);
      await audit(db,actor,'owner.devices.revoke','all');return {ok:true};
    });
    if (path === 'heartbeat' && method === 'POST') {
      if (!['launcher', 'client'].includes(body.kind)) throw new ApiError(400, 'Invalid heartbeat kind.');
      if (body.kind === 'launcher') await pool.query('UPDATE accounts SET last_seen=now(),launcher_version=$2 WHERE uuid=$1', [me.uuid, String(body.version ?? '').slice(0,32)]);
      else await pool.query('UPDATE accounts SET last_seen=now() WHERE uuid=$1', [me.uuid]);
      if (body.kind === 'client' && !me.disabled) await pool.query('UPDATE accounts SET client_seen=now() WHERE uuid=$1', [me.uuid]);
      await pool.query('INSERT INTO activity_days(uuid) VALUES($1) ON CONFLICT DO NOTHING', [me.uuid]);
      if (body.kind === 'launcher' && /^[a-f0-9-]{36}$/i.test(body.installationId ?? '')) await pool.query('INSERT INTO installations(id) VALUES($1) ON CONFLICT(id) DO UPDATE SET last_seen=now()', [digest(body.installationId)]);
      const config = await configuration();
      return { allowed: !me.disabled, reason: me.reason, role: me.access, ownerCandidate: me.uuid===env.OWNER_MINECRAFT_UUID, appealUrl: 'https://plutoniumclient.vercel.app/appeal', ...config, leaseSeconds: 120 };
    }
    if (path === 'owner/start' && method === 'POST') {
      requireOwnerConfig(env);
      if (me.uuid !== env.OWNER_MINECRAFT_UUID || me.disabled) throw new ApiError(403, 'Sign in with the owner Minecraft account first.');
      await limit(`owner:${me.uuid}`, 3, 600);
      const response = await upstream('https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode', { method: 'POST', body: new URLSearchParams({ client_id: env.MICROSOFT_CLIENT_ID, scope: 'openid profile email' }) });
      if (!response.ok) throw new ApiError(503, 'Microsoft owner sign-in is not configured correctly.');
      const flow = await response.json();
      const interval = Math.max(5, Number(flow.interval) || 5);
      await pool.query('INSERT INTO owner_flows(hash,device_code,expires,next_poll,interval_seconds) VALUES($1,$2,$3,$4,$5) ON CONFLICT(hash) DO UPDATE SET device_code=$2,expires=$3,next_poll=$4,interval_seconds=$5', [me.hash, flow.device_code, new Date(Date.now()+Math.min(900,flow.expires_in)*1000), new Date(Date.now()+interval*1000), interval]);
      return { userCode: flow.user_code, verificationUri: 'https://www.microsoft.com/link', interval, expiresIn: flow.expires_in };
    }
    if (path === 'owner/poll' && method === 'POST') {
      requireOwnerConfig(env);
      if (me.uuid !== env.OWNER_MINECRAFT_UUID || me.disabled) throw new ApiError(403, 'Owner account required.');
      const flows = await pool.query("UPDATE owner_flows SET next_poll=now()+interval_seconds*interval '1 second' WHERE hash=$1 AND expires>now() AND next_poll<=now() RETURNING *", [me.hash]);
      if (!flows.rows[0]) throw new ApiError(429, 'Wait for the sign-in polling interval or start again.');
      const result = await upstream('https://login.microsoftonline.com/consumers/oauth2/v2.0/token', { method: 'POST', body: new URLSearchParams({ grant_type: 'urn:ietf:params:oauth:grant-type:device_code', client_id: env.MICROSOFT_CLIENT_ID, device_code: flows.rows[0].device_code }) });
      const auth = await result.json();
      if (auth.error === 'authorization_pending') return { pending: true };
      if (auth.error === 'slow_down') { await pool.query('UPDATE owner_flows SET interval_seconds=interval_seconds+5 WHERE hash=$1',[me.hash]); return { pending: true, interval: flows.rows[0].interval_seconds+5 }; }
      await pool.query('DELETE FROM owner_flows WHERE hash=$1', [me.hash]);
      if (!result.ok || !auth.id_token) throw new ApiError(401, 'Microsoft sign-in failed or expired.');
      try { await ownerVerifier(auth.id_token, me.uuid, env); } catch { throw new ApiError(403, 'This is not the verified owner Microsoft account.'); }
      const db=await pool.connect();
      try {
        await db.query('BEGIN');const current=await principal(bearer,db,true);
        if(current.uuid!==env.OWNER_MINECRAFT_UUID||current.disabled)throw new ApiError(403,'Owner account required.');
        await db.query('UPDATE sessions SET owner_until=$2,expires=$2,owner_device_hash=NULL WHERE hash=$1', [me.hash, new Date(Date.now()+2*60*60*1000)]);
        await audit(db,current,'owner.signin',current.uuid);await db.query('COMMIT');
      }catch(error){await db.query('ROLLBACK');throw error;}finally{db.release();}
      return { pending: false, role: 'owner', expiresIn: 7200, sessionExpiresIn: 7200 };
    }
    if (!path.startsWith('admin/')) throw new ApiError(404, 'Endpoint not found.');
    authorize(me.access, 'admin');
    if (path === 'admin/maintenance/stop' && method === 'POST') return mutation(bearer, 'admin', async (db, actor) => {
      // A narrow recovery action: admins cannot alter other global settings.
      const current=(await db.query('SELECT value FROM configuration WHERE id=1 FOR UPDATE')).rows[0];
      if(!current) throw new ApiError(503,'Moderation database needs migration.');
      await db.query('UPDATE configuration SET value=$1,revision=revision+1 WHERE id=1',[JSON.stringify({...current.value,maintenance:false})]);
      await audit(db,actor,'maintenance.stop','global');
      return configuration(db);
    });
    if (path === 'admin/stats' && method === 'GET') {
      authorize(me.access,'owner');
      const accounts = (await pool.query("SELECT count(*)::int AS total, count(*) FILTER(WHERE last_seen>now()-interval '5 minutes')::int AS active, count(*) FILTER(WHERE client_seen>now()-interval '2 minutes' AND NOT disabled)::int AS playing, count(*) FILTER(WHERE disabled)::int AS disabled FROM accounts")).rows[0];
      const installs = (await pool.query("SELECT count(*)::int AS total, count(*) FILTER(WHERE first_seen>=CURRENT_DATE-6)::int AS this_week, count(*) FILTER(WHERE first_seen<CURRENT_DATE-6 AND first_seen>=CURRENT_DATE-13)::int AS last_week FROM installations")).rows[0];
      const weekly = (await pool.query("SELECT count(DISTINCT uuid) FILTER(WHERE day>=CURRENT_DATE-6)::int AS this_week, count(DISTINCT uuid) FILTER(WHERE day<CURRENT_DATE-6 AND day>=CURRENT_DATE-13)::int AS last_week FROM activity_days")).rows[0];
      const pending = (await pool.query("SELECT count(*)::int AS count FROM appeals WHERE status='pending'")).rows[0].count;
      const activity = (await pool.query('SELECT day,count(*)::int AS count FROM activity_days WHERE day>=CURRENT_DATE-13 GROUP BY day ORDER BY day')).rows;
      const added = (await pool.query('SELECT first_seen::date AS day,count(*)::int AS count FROM installations WHERE first_seen>=CURRENT_DATE-13 GROUP BY first_seen::date ORDER BY day')).rows;
      const versions = (await pool.query("SELECT launcher_version AS version,count(*)::int AS count FROM accounts WHERE last_seen>=CURRENT_DATE-6 AND launcher_version<>'' GROUP BY launcher_version ORDER BY count(*) DESC LIMIT 8")).rows;
      const reviews = (await pool.query("SELECT count(*) FILTER(WHERE status='accepted')::int AS accepted,count(*) FILTER(WHERE status='rejected')::int AS rejected FROM appeals WHERE reviewed_at>=CURRENT_DATE-6")).rows[0];
      return { accounts, installs, weekly, pendingAppeals: pending, daily: dailySeries(activity, added), versions, reviews, generatedAt: new Date().toISOString(), definition: 'Active accounts: seen within 5 minutes. Playing: client heartbeat within 2 minutes. Installs count reported installation IDs, not people or downloads. Weeks compare the latest 7 UTC calendar days (including today) with the preceding 7. Today is partial; collection began when this service launched.' };
    }
    if (path === 'admin/users' && method === 'GET') {
      const after=body.after??'',search=body.search??'';
      if(after!==''&&!/^[a-f0-9]{32}$/.test(after)||!/^[A-Za-z0-9_]{0,16}$/.test(search))throw new ApiError(400,'Invalid account search or cursor.');
      const rows=(await pool.query('SELECT uuid,username,role,disabled,reason,last_seen,first_seen FROM accounts WHERE uuid>$1 AND lower(username) LIKE $2 ORDER BY uuid LIMIT 101',[after,'%'+search.toLowerCase().replaceAll('_','\\_')+'%'])).rows;
      return {users:rows.slice(0,100),nextCursor:rows.length>100?rows[99].uuid:null};
    }
    if (path === 'admin/appeals' && method === 'GET') return { appeals: (await pool.query("SELECT id,uuid,username,explanation,status,created_at FROM appeals ORDER BY (status='pending') DESC,created_at DESC LIMIT 200")).rows };
    if (path === 'admin/audit' && method === 'GET') { authorize(me.access,'owner'); return { events: (await pool.query('SELECT * FROM audit ORDER BY id DESC LIMIT 100')).rows }; }
    if (path === 'admin/config' && method === 'GET') { authorize(me.access,'owner'); return { ...await configuration(), availableFeatures: features }; }
    if (path === 'admin/config' && method === 'PUT') return mutation(bearer, 'owner', async (db, actor) => {
      const config = validateConfig(body);
      const updated = await db.query('UPDATE configuration SET value=$1,revision=revision+1 WHERE id=1 AND revision=$2 RETURNING revision', [JSON.stringify(config), body.revision]);
      if (!updated.rows[0]) throw new ApiError(409, 'Configuration changed. Refresh before saving.');
      await audit(db, actor, 'config.update', 'global', config); return { ...config, revision: updated.rows[0].revision };
    });
    if (path === 'admin/restriction' && method === 'POST') return mutation(bearer, 'admin', async (db, actor) => {
      if (typeof body.disabled !== 'boolean' || typeof body.reason !== 'string' || body.reason.length>500 || body.disabled && body.reason.trim().length<3) throw new ApiError(400,'Provide a restriction reason.');
      await restrict(db, actor, body.uuid, body.disabled, body.reason.trim()); return { ok: true };
    });
    if (path === 'admin/role' && method === 'POST') return mutation(bearer, 'owner', async (db, actor) => {
      if (!['user','admin'].includes(body.role)) throw new ApiError(400, 'Invalid role.');
      await protectedTarget(db, body.uuid, actor);
      await db.query('UPDATE accounts SET role=$2 WHERE uuid=$1', [body.uuid,body.role]);
      await audit(db,actor,'role.update',body.uuid,{role:body.role}); return {ok:true};
    });
    if (path === 'admin/review' && method === 'POST') return mutation(bearer, 'admin', async (db, actor) => {
      if (!['accepted','rejected'].includes(body.decision) || !/^\d{1,18}$/.test(String(body.id))) throw new ApiError(400,'Invalid review.');
      const appeal = (await db.query("SELECT * FROM appeals WHERE id=$1 AND status='pending' FOR UPDATE",[body.id])).rows[0];
      if (!appeal) throw new ApiError(409,'This appeal was already reviewed or does not exist.');
      await protectedTarget(db,appeal.uuid,actor);
      if(body.decision==='accepted') await restrict(db,actor,appeal.uuid,false,'Appeal accepted');
      await db.query('UPDATE appeals SET status=$2,reviewed_at=now(),reviewed_by=$3 WHERE id=$1',[body.id,body.decision,actor.uuid]);
      await audit(db,actor,'appeal.'+body.decision,String(body.id)); return {ok:true};
    });
    throw new ApiError(404,'Endpoint not found.');
  };
}
