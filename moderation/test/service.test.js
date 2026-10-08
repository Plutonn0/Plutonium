import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { newDb } from 'pg-mem';
import { createService } from '../lib/service.js';
import { digest,token } from '../lib/security.js';
const env={OWNER_MINECRAFT_UUID:'a'.repeat(32)};
async function fixture(fetcher=async()=>{throw new Error('Unexpected external request');},extraEnv={}){
 const db=newDb(); db.public.none(await readFile(new URL('../schema.sql',import.meta.url),'utf8'));
 const {Pool}=db.adapters.createPg();const pool=new Pool();
 const users={owner:'a'.repeat(32),admin:'b'.repeat(32),user:'c'.repeat(32)};const tokens={};
 for(const [name,uuid] of Object.entries(users)){
  await pool.query('INSERT INTO accounts(uuid,username,role) VALUES($1,$2,$3)',[uuid,name,name==='admin'?'admin':'user']);
  tokens[name]=token();await pool.query('INSERT INTO sessions(hash,uuid,expires,owner_until) VALUES($1,$2,$3,$4)',[digest(tokens[name]),uuid,new Date(Date.now()+600000),name==='owner'?new Date(Date.now()+600000):null]);
 }
 return {pool,users,tokens,api:createService(pool,{...env,...extraEnv},fetcher)};
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
