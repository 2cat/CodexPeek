import net from 'node:net';
import { randomUUID } from 'node:crypto';
import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { existsSync } from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { DatabaseSync } from 'node:sqlite';
import { present, TaskFeed, readRecentHistory } from './status.mjs';

const once = process.argv.includes('--once');
const codexHome = process.env.CODEX_HOME || path.join(process.env.USERPROFILE,'.codex');
const feed = new TaskFeed(), known = new Map(), owners = new Map();
let socket, clientId, connected = false, buffer = Buffer.alloc(0), quota = null, quotaChild, retry, poll, closed = false, catalogError = false, protocolError = false;
let updatedAt = null, initTimer, history = [];

function emit() {
  const recentTasks=feed.recentTasks(history.filter(t=>!owners.has(t.id)));
  console.log(JSON.stringify({...present(feed.tasks(),quota,connected && !catalogError && !protocolError,{lastTask:recentTasks[0] ?? null,recentTasks}),updatedAt,diagnostic:protocolError?'Codex 状态协议发生变化，请更新组件':catalogError?'无法读取本机任务列表':connected?'':'等待 Codex 桌面端连接'}));
}
function send(message) {
  if(!socket || socket.destroyed) return;
  const payload = Buffer.from(JSON.stringify(message)), header = Buffer.alloc(4); header.writeUInt32LE(payload.length);
  socket.write(Buffer.concat([header,payload]));
}
function follow(id,following=true) {
  send({type:'broadcast',sourceClientId:clientId,version:1,method:'thread-stream-following-changed',params:{hostId:'local',conversationId:id,following}});
}
function scan() {
  if(!connected) return;
  let db;
  try {
    db = new DatabaseSync(path.join(codexHome,'state_5.sqlite'),{readOnly:true});
    const rows = db.prepare("SELECT id, updated_at_ms, title, rollout_path FROM threads WHERE archived=0 AND source='vscode' AND (originator IS NULL OR originator='Codex Desktop') AND (thread_source IS NULL OR thread_source IN ('user','composer_link')) ORDER BY updated_at_ms DESC").all();
    const ids = new Set(rows.map(r => r.id));
    for(const id of known.keys()) if(!ids.has(id)) {follow(id,false);known.delete(id);owners.delete(id);feed.remove(id);}
    for(const row of rows) {
      if(!known.has(row.id) || known.get(row.id)!==row.updated_at_ms) { known.set(row.id,row.updated_at_ms);follow(row.id); }
    }
    history=readRecentHistory(rows,codexHome);
    catalogError = false;
  } catch { catalogError = true; }
  finally { db?.close(); }
  emit();
}
function connect() {
  if(closed) return;
  buffer = Buffer.alloc(0); clientId = 'initializing-client';
  socket = net.createConnection(process.env.CODEX_PEEK_PIPE || '\\\\.\\pipe\\codex-ipc');
  initTimer=setTimeout(()=>socket.destroy(),8000);
  socket.on('connect',() => send({type:'request',requestId:randomUUID(),sourceClientId:clientId,version:0,method:'initialize',params:{clientType:'codex-peek'}}));
  socket.on('error',() => {});
  socket.on('close',() => {
    clearTimeout(initTimer);connected = false; feed.clear();history=[];known.clear();owners.clear();clearInterval(poll);emit();
    if(!closed) retry = setTimeout(connect,3000);
  });
  socket.on('data',chunk => {
    buffer = Buffer.concat([buffer,chunk]);
    // Bound messages from the internal transport. Fail visibly rather than interpreting a changed protocol.
    while(buffer.length>=4) {
      const length = buffer.readUInt32LE(0);
      if(length>64*1024*1024) {protocolError=true;socket.destroy();return;}
      if(buffer.length<length+4) break;
      let message;
      try { message = JSON.parse(buffer.subarray(4,4+length)); } catch {protocolError=true;socket.destroy();return;}
      buffer = buffer.subarray(4+length);
      receive(message);
    }
  });
}
function receive(m) {
  if(m.type==='client-discovery-request') {send({type:'client-discovery-response',requestId:m.requestId,response:{canHandle:false}});return;}
  if(m.type==='response' && m.method==='initialize' && m.resultType==='success') {
    clearTimeout(initTimer);clientId=m.result.clientId;connected=true;protocolError=false;scan();poll=setInterval(scan,4000);return;
  }
  if(m.type!=='broadcast') return;
  if(m.method==='thread-stream-state-changed') {
    const id=m.params?.conversationId;
    if(!known.has(id)) return;
    if(feed.accept(m)) {owners.set(id,m.sourceClientId);updatedAt=Date.now();}
    else if(m.version!==11) protocolError=true;
    else {follow(id,false);follow(id);}
    emit();
  } else if(m.method==='thread-stream-following-status-requested') {
    const id=m.params?.conversationId;
    if(id && known.has(id)) follow(id); else if(!id) for(const key of known.keys()) follow(key);
  } else if(m.method==='client-status-changed') {
    if(m.params?.status==='disconnected') {for(const [id,owner] of owners) if(owner===m.params.clientId) {feed.remove(id);owners.delete(id);}}
    else scan();
    emit();
  } else if(m.method==='ipc-connection-reset') socket.destroy();
}

function readQuota() {
  if(quotaChild || closed || process.argv.includes('--no-quota')) return;
  const cli = path.join(process.env.APPDATA,'npm','node_modules','@openai','codex','bin','codex.js');
  if(!existsSync(cli)) {if(quota) quota.stale=true;emit();return;}
  let exe;
  try {
    const resolve = createRequire(cli);
    const platform = process.arch==='arm64'?'arm64':'x64';
    const root=path.dirname(resolve.resolve(`@openai/codex-win32-${platform}/package.json`));
    exe=path.join(root,'vendor',platform==='arm64'?'aarch64-pc-windows-msvc':'x86_64-pc-windows-msvc','bin','codex.exe');
  } catch {if(quota) quota.stale=true;emit();return;}
  const child = quotaChild = spawn(exe,['app-server','--listen','stdio://'],{windowsHide:true,stdio:['pipe','pipe','pipe']});
  let success = false;
  const timer=setTimeout(()=>child.kill(),25000);
  const write = message => {if(!child.stdin.destroyed) child.stdin.write(JSON.stringify(message)+'\n');};
  createInterface({input:child.stdout}).on('line',line=>{
    let m;try{m=JSON.parse(line);}catch{return;}
    if(m.id===1 && m.result) {write({method:'initialized'});write({id:2,method:'account/rateLimits/read'});}
    if(m.id===2) {if(m.result){quota={...m.result,stale:false};success=true;}child.stdin.end();child.kill();}
  });
  child.stderr.on('data',()=>{});child.stdin.on('error',()=>{});
  child.on('error',()=>{});
  child.on('close',()=>{clearTimeout(timer);quotaChild=null;if(!success && quota) quota.stale=true;emit();});
  write({id:1,method:'initialize',params:{clientInfo:{name:'codex_peek',title:'Codex Peek',version:'0.1.0'}}});
}
function stop() {
  closed=true;clearTimeout(retry);clearInterval(poll);clearInterval(quotaTimer);clearInterval(heartbeat);
  socket?.destroy();quotaChild?.kill();setTimeout(()=>process.exit(),100).unref();
}
process.on('SIGTERM',stop);process.on('SIGINT',stop);
if(!once) {process.stdin.resume();process.stdin.on('end',stop);}
const heartbeat=setInterval(emit,5000),quotaTimer=setInterval(readQuota,60000);
connect();readQuota();emit();
if(once) setTimeout(()=>{emit();stop();},12000);
