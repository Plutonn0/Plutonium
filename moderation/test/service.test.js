import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { newDb } from 'pg-mem';
import { createService } from '../lib/service.js';
import { digest,token } from '../lib/security.js';
const env={OWNER_MINECRAFT_UUID:'a'.repeat(32)};
async function fixture(fetcher=async()=>{throw new Error('Unexpected external request');},extraEnv={},clock=()=>new Date()){
 const db=newDb(); db.public.none(await readFile(new URL('../schema.sql',import.meta.url),'utf8'));
 const {Pool}=db.adapters.createPg();const pool=new Pool();
 const users={owner:'a'.repeat(32),admin:'b'.repeat(32),user:'c'.repeat(32)};const tokens={};
 for(const [name,uuid] of Object.entries(users)){
  await pool.query('INSERT INTO accounts(uuid,username,role) VALUES($1,$2,$3)',[uuid,name,name==='admin'?'admin':'user']);
  tokens[name]=token();await pool.query('INSERT INTO sessions(hash,uuid,expires,owner_until) VALUES($1,$2,$3,$4)',[digest(tokens[name]),uuid,new Date(Date.now()+600000),name==='owner'?new Date(Date.now()+600000):null]);
 }
 return {pool,users,tokens,api:createService(pool,{...env,...extraEnv},fetcher,undefined,clock)};
}
test('all administrative endpoints reject unsigned and ordinary users',async()=>{
 const {api,tokens}=await fixture();
 for(const [path,method] of [['admin/users','GET'],['admin/appeals','GET'],['admin/config','PUT'],['admin/restriction','POST'],['admin/role','POST'],['admin/review','POST']]){
  await assert.rejects(api(path,method,{},null),e=>e.status===401);
  await assert.rejects(api(path,method,{},tokens.user),e=>e.status===403);
 }
});
test('admin cannot grant roles, change configuration or read owner audit',async()=>{
 const {api,tokens}=await fixture();
 for(const [path,method] of [['admin/role','POST'],['admin/config','PUT'],['admin/audit','GET'],['admin/stats','GET']]) await assert.rejects(api(path,method,{},tokens.admin),e=>e.status===403);
});
test('restriction changes apply to heartbeats and are audited',async()=>{
 const {api,pool,tokens,users}=await fixture();
 await api('admin/restriction','POST',{uuid:users.user,disabled:true,reason:'Test restriction'},tokens.admin);
 const state=await api('me','GET',{},tokens.user);assert.equal(state.disabled,true);
 const audit=await pool.query('SELECT action FROM audit');assert.equal(audit.rows[0].action,'account.disable');
 await api('admin/restriction','POST',{uuid:users.user,disabled:false,reason:''},tokens.admin);
 assert.equal((await api('me','GET',{},tokens.user)).disabled,false);
});
test('owner is protected against account disabling and role changes',async()=>{
 const {api,tokens,users}=await fixture();
 for(const actor of ['owner','admin'])await assert.rejects(api('admin/restriction','POST',{uuid:users.owner,disabled:true,reason:'Attempt'},tokens[actor]),e=>e.status===403);
 await assert.rejects(api('admin/role','POST',{uuid:users.owner,role:'admin'},tokens.owner),e=>e.status===403);
});
test('role revocation immediately invalidates existing admin authority',async()=>{
 const {api,tokens,users}=await fixture();
 await api('admin/role','POST',{uuid:users.admin,role:'user'},tokens.owner);
 await assert.rejects(api('admin/users','GET',{},tokens.admin),e=>e.status===403);
});
test('configuration uses revision checks to avoid overwriting newer changes',async()=>{
 const {api,tokens}=await fixture();const config=await api('config','GET',{});
 await api('admin/config','PUT',{...config,maintenance:true},tokens.owner);
 await assert.rejects(api('admin/config','PUT',{...config,maintenance:false},tokens.owner),e=>e.status===409);
 assert.equal((await api('config','GET',{})).maintenance,true);
});
test('appeal acceptance restores client access; duplicate review is rejected',async()=>{
 const {api,pool,tokens,users}=await fixture();
 await pool.query('UPDATE accounts SET disabled=true WHERE uuid=$1',[users.user]);
 const inserted=await pool.query('INSERT INTO appeals(uuid,username,explanation) VALUES($1,$2,$3) RETURNING id',[users.user,'user','Please reconsider this restriction.']);
 const id=inserted.rows[0].id;
 await api('admin/review','POST',{id,decision:'accepted'},tokens.admin);
 assert.equal((await api('me','GET',{},tokens.user)).disabled,false);
 await assert.rejects(api('admin/review','POST',{id,decision:'rejected'},tokens.admin),e=>e.status===409);
});
test('signout revokes the opaque session token',async()=>{
 const {api,tokens}=await fixture();await api('session','DELETE',{},tokens.admin);await assert.rejects(api('me','GET',{},tokens.admin),e=>e.status===401);
});
test('client-supplied username, UUID and role cannot impersonate the owner',async()=>{
 const {api}=await fixture(async url=>{assert.equal(url,'https://api.minecraftservices.com/minecraft/profile');return Response.json({id:'c'.repeat(32),name:'VerifiedUser'});});
 const session=await api('session','POST',{minecraftToken:'test-only',username:'justquirk.business@gmail.com',uuid:env.OWNER_MINECRAFT_UUID,role:'owner'});
 assert.equal(session.uuid,'c'.repeat(32));assert.equal(session.username,'VerifiedUser');assert.equal(session.role,'user');
 await assert.rejects(api('admin/users','GET',{},session.token),e=>e.status===403);
});
test('invalid Minecraft access tokens never create a service session',async()=>{
 const {api,pool}=await fixture(async()=>new Response('',{status:401}));
 await assert.rejects(api('session','POST',{minecraftToken:'invalid'}),e=>e.status===401);
 assert.equal((await pool.query('SELECT * FROM sessions')).rows.length,3);
});
test('a public appeal requires valid CAPTCHA hostname and action',async()=>{
 const {api}=await fixture(async()=>Response.json({success:true,hostname:'attacker.example',action:'appeal'}),{TURNSTILE_SECRET_KEY:'test-only',APPEAL_HOSTNAME:'plutoniumclient.vercel.app'});
 await assert.rejects(api('appeals','POST',{username:'user',explanation:'Please review my restriction.',captcha:'test-only'}),e=>e.status===400);
});
test('appeal submission cannot restore an account or overwrite a pending appeal',async()=>{
 const {api,pool,users}=await fixture(async()=>Response.json({success:true,hostname:'plutoniumclient.vercel.app',action:'appeal'}),{TURNSTILE_SECRET_KEY:'test-only',APPEAL_HOSTNAME:'plutoniumclient.vercel.app'});
 await pool.query('UPDATE accounts SET disabled=true WHERE uuid=$1',[users.user]);
 for(const explanation of ['First valid explanation.','Attempt to overwrite explanation.'])await api('appeals','POST',{username:'user',explanation,captcha:'test-only',disabled:false});
 const appeals=(await pool.query('SELECT * FROM appeals')).rows;assert.equal(appeals.length,1);assert.equal(appeals[0].explanation,'First valid explanation.');
 assert.equal((await pool.query('SELECT disabled FROM accounts WHERE uuid=$1',[users.user])).rows[0].disabled,true);
});

test('missing appeal verification reports a validation error without calling Cloudflare',async()=>{
 const {api}=await fixture(undefined,{TURNSTILE_SECRET_KEY:'test-only'});
 await assert.rejects(api('appeals','POST',{username:'user',explanation:'Please reconsider this restriction.',captcha:''}),e=>e.status===400&&e.message.includes('Complete the verification'));
});
test('client heartbeats preserve the last reported launcher version',async()=>{
 const {api,pool,tokens,users}=await fixture();
 await api('heartbeat','POST',{kind:'launcher',version:'2.0.1'},tokens.user);
 await api('heartbeat','POST',{kind:'client',version:'different-client-version'},tokens.user);
 assert.equal((await pool.query('SELECT launcher_version FROM accounts WHERE uuid=$1',[users.user])).rows[0].launcher_version,'2.0.1');
});

test('admins can stop maintenance without changing other global settings',async()=>{
 const {api,tokens,pool}=await fixture();const before=await api('config','GET',{});
 await api('admin/config','PUT',{...before,maintenance:true,message:'Scheduled work',disabledFeatures:['fly']},tokens.owner);
 await assert.rejects(api('admin/maintenance/stop','POST',{},tokens.user),e=>e.status===403);
 await assert.rejects(api('admin/maintenance/stop','POST',{},null),e=>e.status===401);
 const result=await api('admin/maintenance/stop','POST',{message:'not allowed',disabledFeatures:[]},tokens.admin);
 assert.equal(result.maintenance,false);assert.equal(result.message,'Scheduled work');assert.deepEqual(result.disabledFeatures,['fly']);
 assert.equal(result.revision,before.revision+2);
 assert.equal((await pool.query("SELECT action FROM audit WHERE action='maintenance.stop'")).rows.length,1);
 await api('admin/role','POST',{uuid:'b'.repeat(32),role:'user'},tokens.owner);
 await assert.rejects(api('admin/maintenance/stop','POST',{},tokens.admin),e=>e.status===403);
});

const ownerEnvironment={OWNER_MICROSOFT_OID:'00000000-0000-0000-0000-000000000001',MICROSOFT_CLIENT_ID:'00000000-0000-0000-0000-000000000002'};
test('remembered owner devices survive service restart but require the matching Minecraft session',async()=>{
 const {api,pool,tokens}=await fixture(undefined,ownerEnvironment);
 const device=await api('owner/device/register','POST',{},tokens.owner);
 assert.match(device.deviceToken,/^[A-Za-z0-9_-]{43}$/);
 assert.equal((await pool.query('SELECT hash FROM owner_devices')).rows[0].hash,digest(device.deviceToken));
 await pool.query('UPDATE sessions SET owner_until=NULL');
 const restarted=createService(pool,{...env,...ownerEnvironment});
 await assert.rejects(restarted('owner/device/resume','POST',device,null),e=>e.status===401);
 await assert.rejects(restarted('owner/device/resume','POST',device,tokens.user),e=>e.status===403);
 await restarted('owner/device/resume','POST',device,tokens.owner);
 assert.equal((await restarted('me','GET',{},tokens.owner)).role,'owner');
});
test('forgetting a device revokes its active elevation and prevents replay',async()=>{
 const {api,tokens}=await fixture(undefined,ownerEnvironment);
 const device=await api('owner/device/register','POST',{},tokens.owner);
 await api('owner/device/forget','POST',device,tokens.owner);
 assert.equal((await api('me','GET',{},tokens.owner)).role,'user');
 await assert.rejects(api('owner/device/resume','POST',device,tokens.owner),e=>e.status===403);
});
test('changing the pinned Microsoft identity invalidates remembered devices',async()=>{
 const {api,pool,tokens}=await fixture(undefined,ownerEnvironment);
 const device=await api('owner/device/register','POST',{},tokens.owner);
 const changed=createService(pool,{...env,...ownerEnvironment,OWNER_MICROSOFT_OID:'00000000-0000-0000-0000-000000000099'});
 assert.equal((await changed('me','GET',{},tokens.owner)).role,'user');
 await assert.rejects(changed('owner/device/resume','POST',device,tokens.owner),e=>e.status===403);
});
test('only a verified owner can remember a device or revoke all devices',async()=>{
 const {api,tokens,pool}=await fixture(undefined,ownerEnvironment);
 for(const actor of ['admin','user'])for(const path of ['owner/device/register','owner/devices/revoke-all'])await assert.rejects(api(path,'POST',{},tokens[actor]),e=>e.status===403);
 const device=await api('owner/device/register','POST',{},tokens.owner);
 await api('owner/devices/revoke-all','POST',{},tokens.owner);
 assert.equal((await pool.query('SELECT * FROM owner_devices')).rows.length,0);
 await assert.rejects(api('owner/device/resume','POST',device,tokens.owner),e=>e.status===403);
});


test('daily beta key is owner-only, stable within a UTC day and rotates at midnight',async()=>{
 let now=new Date('2026-10-09T23:59:00Z');const {api,tokens,pool}=await fixture(undefined,{},()=>now);
 for(const actor of [null,tokens.user,tokens.admin])await assert.rejects(api('admin/beta','GET',{},actor),e=>e.status===401||e.status===403);
 const first=await api('admin/beta','GET',{},tokens.owner);
 assert.match(first.key,/^[A-F0-9]{8}(-[A-F0-9]{8}){3}$/);assert.equal(first.expiresAt,'2026-10-10T00:00:00.000Z');
 const restarted=createService(pool,env,undefined,undefined,()=>now);
 assert.equal((await restarted('admin/beta','GET',{},tokens.owner)).key,first.key);
 now=new Date('2026-10-10T00:00:00Z');const second=await api('admin/beta','GET',{},tokens.owner);assert.notEqual(first.key,second.key);
 await assert.rejects(api('beta/redeem','POST',{key:first.key},tokens.user),e=>e.status===400);
 await api('beta/redeem','POST',{key:second.key.toLowerCase()},tokens.user);
 assert.equal((await api('me','GET',{},tokens.user)).betaAccess,true);
 assert.equal((await api('me','GET',{},tokens.user)).role,'user');
});

test('beta enrollment survives key rotation and service restart, and does not leak invitation codes',async()=>{
 let now=new Date('2026-10-09T12:00:00Z');const {api,pool,tokens}=await fixture(undefined,{},()=>now);
 const {key}=await api('admin/beta','GET',{},tokens.owner);
 await api('beta/redeem','POST',{key},tokens.user);now=new Date('2026-10-12T12:00:00Z');
 const restarted=createService(pool,env,undefined,undefined,()=>now);
 assert.equal((await restarted('me','GET',{},tokens.user)).betaAccess,true);
 const audit=(await pool.query('SELECT details FROM audit')).rows;
 for(const response of [await api('config','GET',{}),await api('me','GET',{},tokens.user),audit]) assert.equal(JSON.stringify(response).includes(key),false);
});

test('revocation blocks redemption of future keys until the owner restores membership',async()=>{
 const {api,tokens,users}=await fixture();const {key}=await api('admin/beta','GET',{},tokens.owner);
 await api('beta/redeem','POST',{key},tokens.user);
 for(const actor of [tokens.admin,tokens.user])await assert.rejects(api('admin/beta/member','POST',{uuid:users.user,revoked:true},actor),e=>e.status===403);
 await api('admin/beta/member','POST',{uuid:users.user,revoked:true},tokens.owner);
 assert.equal((await api('me','GET',{},tokens.user)).betaAccess,false);
 await assert.rejects(api('beta/redeem','POST',{key},tokens.user),e=>e.status===403);
 await api('admin/beta/member','POST',{uuid:users.user,revoked:false},tokens.owner);
 assert.equal((await api('me','GET',{},tokens.user)).betaAccess,true);
});

test('beta feature gates reach existing clients, and global restrictions take precedence',async()=>{
 const {api,pool,tokens,users}=await fixture();const settings=await api('admin/beta','GET',{},tokens.owner);
 await api('admin/beta/features','PUT',{features:['fly','mods'],revision:settings.revision},tokens.owner);
 let state=await api('heartbeat','POST',{kind:'client'},tokens.user);assert.equal(state.betaAccess,false);assert.ok(state.disabledFeatures.includes('fly'));
 await api('beta/redeem','POST',{key:settings.key},tokens.user);
 state=await api('heartbeat','POST',{kind:'client'},tokens.user);assert.equal(state.betaAccess,true);assert.ok(!state.disabledFeatures.includes('fly'));
 const config=await api('config','GET',{});await api('admin/config','PUT',{...config,disabledFeatures:['fly'],maintenance:true},tokens.owner);
 state=await api('heartbeat','POST',{kind:'client'},tokens.user);assert.ok(state.disabledFeatures.includes('fly'));assert.equal(state.maintenance,true);
 await pool.query('UPDATE accounts SET disabled=true WHERE uuid=$1',[users.user]);
 state=await api('heartbeat','POST',{kind:'client'},tokens.user);assert.equal(state.allowed,false);assert.equal(state.betaAccess,false);
});

test('beta feature editing validates IDs, enforces owner authority and detects stale edits',async()=>{
 const {api,tokens}=await fixture();
 for(const actor of [tokens.admin,tokens.user])await assert.rejects(api('admin/beta/features','PUT',{features:['fly'],revision:1},actor),e=>e.status===403);
 await assert.rejects(api('admin/beta/features','PUT',{features:['made-up'],revision:1},tokens.owner),e=>e.status===400);
 await api('admin/beta/features','PUT',{features:['fly'],revision:1},tokens.owner);
 await assert.rejects(api('admin/beta/features','PUT',{features:[],revision:1},tokens.owner),e=>e.status===409);
});

test('beta enrollment rejects unsigned, disabled and maintenance requests and rate limits guessing',async()=>{
 const {api,pool,tokens,users}=await fixture();const {key}=await api('admin/beta','GET',{},tokens.owner);
 await assert.rejects(api('beta/redeem','POST',{key},null),e=>e.status===401);
 await pool.query('UPDATE accounts SET disabled=true WHERE uuid=$1',[users.user]);
 await assert.rejects(api('beta/redeem','POST',{key},tokens.user),e=>e.status===403);
 await pool.query('UPDATE accounts SET disabled=false WHERE uuid=$1',[users.user]);
 const config=await api('config','GET',{});await api('admin/config','PUT',{...config,maintenance:true},tokens.owner);
 await assert.rejects(api('beta/redeem','POST',{key},tokens.user),e=>e.status===403);
 for(let i=0;i<3;i++)await assert.rejects(api('beta/redeem','POST',{key:'wrong'},tokens.user));
 await assert.rejects(api('beta/redeem','POST',{key},tokens.user),e=>e.status===429);
});
