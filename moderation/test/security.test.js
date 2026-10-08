import { test } from 'node:test';
import assert from 'node:assert/strict';
import { generateKeyPair, SignJWT } from 'jose';
import { verifyOwner, roleFor, authorize, validateConfig, validateAppeal, issuer, consumerTenant } from '../lib/security.js';
const env={MICROSOFT_CLIENT_ID:'11111111-1111-1111-1111-111111111111',OWNER_MICROSOFT_OID:'22222222-2222-2222-2222-222222222222',OWNER_MINECRAFT_UUID:'a'.repeat(32)};
const {privateKey,publicKey}=await generateKeyPair('RS256');
async function signed(overrides={},options={}){
 return new SignJWT({oid:env.OWNER_MICROSOFT_OID,tid:consumerTenant,...overrides}).setProtectedHeader({alg:'RS256'}).setSubject('owner-subject').setIssuer(options.issuer??issuer).setAudience(options.audience??env.MICROSOFT_CLIENT_ID).setIssuedAt().setExpirationTime(options.expires??'5m').sign(privateKey);
}
test('owner requires both pinned Microsoft identity and Minecraft UUID',async()=>{
 const jwt=await signed();await verifyOwner(jwt,env.OWNER_MINECRAFT_UUID,env,publicKey);
 await assert.rejects(verifyOwner(jwt,'b'.repeat(32),env,publicKey));
});
test('spoofing the owner email never grants access',async()=>{
 const jwt=await signed({oid:'33333333-3333-3333-3333-333333333333',email:'justquirk.business@gmail.com',preferred_username:'justquirk.business@gmail.com'});
 await assert.rejects(verifyOwner(jwt,env.OWNER_MINECRAFT_UUID,env,publicKey));
});
test('wrong issuer, audience, expired token and modified signature are rejected',async()=>{
 for(const options of [{issuer:'https://attacker.example'},{audience:'other-app'},{expires:'-1s'}])await assert.rejects(verifyOwner(await signed({},options),env.OWNER_MINECRAFT_UUID,env,publicKey));
 const jwt=await signed();await assert.rejects(verifyOwner(jwt.slice(0,-8)+'AAAAAAAA',env.OWNER_MINECRAFT_UUID,env,publicKey));
});
test('missing owner configuration fails closed',async()=>{await assert.rejects(verifyOwner(await signed(),env.OWNER_MINECRAFT_UUID,{},publicKey));});
test('admins cannot change global settings or elevate themselves',()=>{
 authorize('admin','admin'); assert.throws(()=>authorize('admin','owner'));assert.throws(()=>authorize('user','admin'));
});
test('disabled admins and expired owner sessions lose authority',()=>{
 assert.equal(roleFor({uuid:'b'.repeat(32),role:'admin',disabled:true},{},env),'user');
 assert.equal(roleFor({uuid:env.OWNER_MINECRAFT_UUID,role:'user',disabled:false},{owner_until:new Date(0)},env),'user');
 assert.equal(roleFor({uuid:'b'.repeat(32),role:'user',disabled:false},{owner_until:new Date(Date.now()+60000)},env),'user');
});
test('remote config accepts only known features and typed values',()=>{
 assert.deepEqual(validateConfig({maintenance:false,message:' hello ',disabledFeatures:['radio','radio']}),{maintenance:false,message:'hello',disabledFeatures:['radio']});
 for(const body of [{maintenance:'false',message:'',disabledFeatures:[]},{maintenance:false,message:'',disabledFeatures:['executeCommand']},{maintenance:false,message:'x'.repeat(241),disabledFeatures:[]}])assert.throws(()=>validateConfig(body));
});
test('appeals validate usernames and bounded explanations',()=>{
 assert.equal(validateAppeal({username:'JustQuirk',explanation:'Please review my restriction.'}).username,'JustQuirk');
 for(const body of [{username:'<script>',explanation:'valid explanation'},{username:'Player',explanation:'tiny'},{username:'Player',explanation:'x'.repeat(2001)}])assert.throws(()=>validateAppeal(body));
});
