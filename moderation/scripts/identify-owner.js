// Local setup helper only. It prints public identity IDs, never tokens, and grants no permissions.
import { createRemoteJWKSet, jwtVerify } from 'jose';
import { consumerTenant,issuer } from '../lib/security.js';
const clientId=process.env.MICROSOFT_CLIENT_ID;
if(!clientId)throw new Error('Set MICROSOFT_CLIENT_ID to your public app registration ID.');
const microsoft='https://login.microsoftonline.com/consumers/oauth2/v2.0/';
const start=await fetch(microsoft+'devicecode',{method:'POST',body:new URLSearchParams({client_id:clientId,scope:'openid profile email'}),signal:AbortSignal.timeout(15000)});
if(!start.ok)throw new Error('Device-code sign-in could not start. Check the registration and public-client flow setting.');
const flow=await start.json();
console.log('Open https://www.microsoft.com/link and enter this one-time code:',flow.user_code);
console.log('Sign in specifically as justquirk.business@gmail.com. Do not share this code.');
const end=Date.now()+Math.min(flow.expires_in,900)*1000;let interval=Math.max(5,flow.interval??5);
while(Date.now()<end){
 await new Promise(resolve=>setTimeout(resolve,interval*1000));
 const response=await fetch(microsoft+'token',{method:'POST',body:new URLSearchParams({client_id:clientId,grant_type:'urn:ietf:params:oauth:grant-type:device_code',device_code:flow.device_code}),signal:AbortSignal.timeout(15000)});
 const result=await response.json();
 if(result.error==='authorization_pending')continue;
 if(result.error==='slow_down'){interval+=5;continue;}
 if(!response.ok||!result.id_token)throw new Error('Microsoft sign-in was denied or expired.');
 const {payload}=await jwtVerify(result.id_token,createRemoteJWKSet(new URL('https://login.microsoftonline.com/consumers/discovery/v2.0/keys')),{issuer,audience:clientId,algorithms:['RS256'],requiredClaims:['exp','iat','sub','oid','tid'],maxTokenAge:'10m'});
 if(payload.tid!==consumerTenant)throw new Error('This is not a personal Microsoft account.');
 console.log('Verified Microsoft account label:',payload.preferred_username??payload.email??payload.name??'(not supplied)');
 console.log('OWNER_MICROSOFT_OID='+payload.oid);
 console.log('Confirm this is the intended Microsoft account before configuring the server. No permission has been granted by this script.');
 process.exit(0);
}
throw new Error('Sign-in timed out.');
