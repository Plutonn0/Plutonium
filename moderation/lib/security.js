import { createHash, randomBytes } from 'node:crypto';
import { createRemoteJWKSet, jwtVerify } from 'jose';
export const consumerTenant = '9188040d-6c67-4c5b-b112-36a304b66dad';
export const issuer = `https://login.microsoftonline.com/${consumerTenant}/v2.0`;
const keys = createRemoteJWKSet(new URL('https://login.microsoftonline.com/consumers/discovery/v2.0/keys'));
export const digest = value => createHash('sha256').update(value).digest('hex');
export const token = () => randomBytes(32).toString('base64url');
export class ApiError extends Error { constructor(status, message) { super(message); this.status = status; } }
export function requireOwnerConfig(env) {
  if (!/^[a-f0-9-]{36}$/i.test(env.OWNER_MICROSOFT_OID ?? '') || !/^[a-f0-9]{32}$/.test(env.OWNER_MINECRAFT_UUID ?? '') || !/^[a-f0-9-]{36}$/i.test(env.MICROSOFT_CLIENT_ID ?? ''))
    throw new ApiError(503, 'Owner authentication is not configured.');
}
export async function verifyOwner(idToken, uuid, env, keySet = keys) {
  requireOwnerConfig(env);
  const { payload } = await jwtVerify(idToken, keySet, { issuer, audience: env.MICROSOFT_CLIENT_ID, algorithms: ['RS256'], requiredClaims: ['exp','iat','sub','oid','tid'], maxTokenAge: '10m' });
  // Email is mutable and is never the source of owner authority. Both immutable IDs are pinned.
  if (payload.tid !== consumerTenant || payload.oid.toLowerCase() !== env.OWNER_MICROSOFT_OID.toLowerCase() || uuid !== env.OWNER_MINECRAFT_UUID)
    throw new ApiError(403, 'This Microsoft account is not the configured owner.');
  return payload;
}
export function roleFor(account, session, env, now = Date.now()) {
  if (account.disabled) return 'user';
  if (account.uuid === env.OWNER_MINECRAFT_UUID && new Date(session.owner_until ?? 0).getTime() > now) return 'owner';
  return account.role === 'admin' ? 'admin' : 'user';
}
export function authorize(role, required) {
  if (required === 'owner' ? role !== 'owner' : !['owner','admin'].includes(role)) throw new ApiError(403, 'You do not have permission for this action.');
}
export const features = ['mods', 'players', 'storage', 'spawners', 'tracers', 'xray', 'fullbright', 'suschunk', 'freecam', 'fastplace', 'fly', 'elytraglide', 'inventorymove', 'autoclutch', 'aimassist', 'autoeat', 'autofirework', 'automace', 'autototem', 'doubleanchor', 'mobs', 'nametags', 'freelook', 'trajectory', 'sprint', 'nohitdelay', 'autoclicker', 'quickexp', 'invtotem', 'fakepay', 'netherite', 'fakestats', 'weather', 'notifications', 'discord', 'radio', 'coordinates', 'active'];
export function validateConfig(body) {
  if (typeof body.maintenance !== 'boolean' || typeof body.message !== 'string' || body.message.length > 240 || !Array.isArray(body.disabledFeatures) || body.disabledFeatures.some(f => !features.includes(f))) throw new ApiError(400, 'Invalid configuration.');
  return { maintenance: body.maintenance, message: body.message.trim() || 'The launcher is currently under maintenance.', disabledFeatures: [...new Set(body.disabledFeatures)] };
}
export function validateAppeal(body) {
  if (!/^[A-Za-z0-9_]{3,16}$/.test(body.username ?? '') || typeof body.explanation !== 'string' || body.explanation.trim().length < 10 || body.explanation.length > 2000) throw new ApiError(400, 'Enter your Minecraft username and an explanation between 10 and 2000 characters.');
  return { username: body.username, explanation: body.explanation.trim() };
}
