import pg from 'pg';
import { randomUUID } from 'node:crypto';
import { createService } from '../lib/service.js';
import { ApiError } from '../lib/security.js';
const pool = new pg.Pool({ connectionString: process.env.DATABASE_URL, max: 3, connectionTimeoutMillis: 5000, idleTimeoutMillis: 10000, statement_timeout: 10000, options: '-c timezone=UTC' });
const service = createService(pool, process.env);
export default async function handler(req,res) {
  const requestId = randomUUID();
  res.setHeader('Cache-Control','no-store'); res.setHeader('X-Content-Type-Options','nosniff'); res.setHeader('X-Request-Id',requestId);
  const origin=req.headers.origin;
  if(origin && origin!==process.env.APPEAL_ORIGIN) return res.status(403).json({error:'Origin is not allowed.',requestId});
  if(origin) { res.setHeader('Access-Control-Allow-Origin',origin); res.setHeader('Vary','Origin'); res.setHeader('Access-Control-Allow-Methods','POST,OPTIONS'); res.setHeader('Access-Control-Allow-Headers','Content-Type'); }
  if(req.method==='OPTIONS') return res.status(204).end();
  try {
    if(!process.env.DATABASE_URL) throw new ApiError(503,'Moderation service is not configured.');
    if(Number(req.headers['content-length'] ?? 0)>16384) throw new ApiError(413,'Request is too large.');
    const raw=typeof req.body==='string'?req.body:JSON.stringify(req.body ?? {});
    if(Buffer.byteLength(raw)>16384) throw new ApiError(413,'Request is too large.');
    let body; try {body=JSON.parse(raw);} catch {throw new ApiError(400,'Invalid JSON.');}
    if(!body || typeof body!=='object' || Array.isArray(body)) throw new ApiError(400,'Invalid request.');
    const path=String(req.query.path ?? '').replace(/^\/+|\/+$/g,'');
    if(req.method==='GET')body={after:String(req.query.after??''),search:String(req.query.search??'')};
    if(origin && path!=='appeals') throw new ApiError(403,'Website access is limited to appeals.');
    const bearer=/^Bearer ([A-Za-z0-9_-]+)$/.exec(req.headers.authorization ?? '')?.[1];
    const ip=String(req.headers['x-vercel-forwarded-for'] ?? req.socket?.remoteAddress ?? 'unknown').split(',')[0];
    res.status(200).json(await service(path,req.method,body,bearer,ip));
  } catch(error) {
    // Never log request bodies, bearer tokens, device codes, database connection strings or upstream errors.
    if(!(error instanceof ApiError)) console.error(JSON.stringify({requestId,event:'moderation_request_failed'}));
    res.status(error instanceof ApiError?error.status:503).json({error:error instanceof ApiError?error.message:'Service unavailable. Try again.',requestId});
  }
}
