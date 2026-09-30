export function present(tasks, quota = null, connected = true, {lastTask = null, now = Date.now()} = {}) {
  const order = {approval:0,input:1,failed:2,running:3};
  tasks = connected ? tasks.filter(t => t.state in order).sort((a,b) => order[a.state]-order[b.state] || a.id.localeCompare(b.id)) : [];
  const count = state => tasks.filter(t => t.state === state).length;
  const running = count('running'), approval = count('approval'), input = count('input'), failed = count('failed');
  const headline = !connected ? '状态未同步' : approval ? `等待确认 · ${approval} 项` : input ? `等待回复 · ${input} 项` : failed ? `任务异常 · ${failed} 项` : running > 1 ? `正在运行 ${running} 项任务` : running === 1 ? compactActivity(tasks.find(t => t.state === 'running').detail || '正在执行任务') : lastTask ? `上次：${lastTask.title}` : '暂无运行任务';
  const buckets = quota?.rateLimitsByLimitId ? Object.values(quota.rateLimitsByLimitId) : [quota?.rateLimits];
  const windows = buckets.flatMap(b => [b?.primary,b?.secondary].filter(w => Number.isFinite(w?.usedPercent)).map(w => ({remaining:Math.max(0,Math.min(100,100-w.usedPercent)),label:windowLabel(w.windowDurationMins),resetsAt:w.resetsAt ?? null})));
  windows.sort((a,b) => a.remaining-b.remaining);
  const limit = windows[0];
  return {headline, running, waiting:approval+input, tasks, connected, tone:!connected?'muted':approval||input?'amber':failed?'red':running?'blue':'muted', quotaText:limit ? `剩余 ${Math.floor(limit.remaining)}%${resetCountdown(limit.resetsAt,now)}${quota.stale?' · 未更新':''}` : '额度暂不可用', windows};
}

function compactActivity(detail) {
  return detail.replace(/^正在([^·]+) · /,(_,action)=>`${({执行命令:'命令',运行测试:'测试',调用工具:'工具',整理回复:'回复',执行任务:'任务',搜索代码:'搜索',查看项目文件:'文件'})[action] ?? action} · `);
}

function resetCountdown(resetsAt, now) {
  if(!Number.isFinite(resetsAt)) return '';
  const seconds = Math.floor(resetsAt-now/1000);
  if(seconds<=0) return ' · 待刷新';
  const minutes=Math.floor(seconds/60),hours=Math.floor(minutes/60),days=Math.floor(hours/24);
  const duration=days ? `${days}天${hours%24}小时` : hours ? `${hours}小时${minutes%60}分` : minutes ? `${minutes}分钟` : '不到1分钟';
  return ` · ${duration}后重置`;
}

function windowLabel(minutes) {
  return !Number.isFinite(minutes) ? '当前周期' : minutes%1440===0 ? `${minutes/1440}天` : minutes%60===0 ? `${minutes/60}小时` : `${minutes}分钟`;
}

// Desktop IPC is internal. A revision/version mismatch removes the old state until a fresh snapshot arrives.
export class TaskFeed {
  states = new Map();
  observed = new Map();
  accept(message, now = Date.now()) {
    const {conversationId:id,hostId,change} = message.params ?? {};
    if (hostId !== 'local' || typeof id !== 'string') return false;
    try {
      if (message.version !== 11) throw Error('Unsupported desktop protocol');
      if (change.type === 'snapshot') this.states.set(id,{revision:change.revision,state:structuredClone(change.conversationState)});
      else if (change.type === 'patches') {
        const entry = this.states.get(id);
        if (!entry || entry.revision !== change.baseRevision) throw Error('Missing revision');
        for (const patch of change.patches) {
          if (!Array.isArray(patch.path) || !patch.path.length || patch.path.some(p => ['__proto__','constructor','prototype'].includes(p))) throw Error('Invalid path');
          let target = entry.state;
          for (const key of patch.path.slice(0,-1)) target = target[key];
          const key = patch.path.at(-1);
          if (patch.op === 'remove') { if(Array.isArray(target)) target.splice(Number(key),1); else delete target[key]; }
          else if (patch.op === 'add' || patch.op === 'replace') {
            if(Array.isArray(target) && patch.op === 'add') target.splice(Number(key),0,patch.value);
            else target[key] = patch.value;
          } else throw Error('Invalid operation');
        }
        entry.revision = change.revision;
      } else throw Error('Unknown change');
      const task=taskFromState(id,this.states.get(id).state),previous=this.observed.get(id);
      this.observed.set(id,{state:task?.state,waitingSince:['approval','input'].includes(task?.state)?previous?.state===task.state?previous.waitingSince:now:null});
      return true;
    } catch { this.remove(id); return false; }
  }
  tasks() { return [...this.states].map(([id,{state}]) => {const task=taskFromState(id,state);return task?{...task,waitingSince:this.observed.get(id)?.waitingSince ?? null}:null;}).filter(Boolean); }
  lastTask() { return [...this.states].map(([id,{state}]) => taskFromState(id,state,true)).filter(t => t?.state==='idle' && Number.isFinite(t.startedAt)).sort((a,b) => b.startedAt-a.startedAt)[0] ?? null; }
  remove(id) {this.states.delete(id);this.observed.delete(id);}
  clear() { this.states.clear();this.observed.clear(); }
}

function taskFromState(id,s,includeIdle=false) {
  if (s.ephemeral || s.sideConversation || (s.threadSource && !['user','composer_link'].includes(s.threadSource)) || (s.source && s.source !== 'vscode') || (s.originator && s.originator !== 'Codex Desktop')) return null;
  const turns = s.turnHistory?.kind === 'canonical' ? Object.values(s.turnHistory.history?.entitiesByKey ?? {}) : s.turns ?? [];
  const turn = turns.reduce((last,t) => (t.turnStartedAtMs ?? 0) >= (last?.turnStartedAtMs ?? 0) ? t : last,null);
  const runtime = s.threadRuntimeStatus, flags = runtime?.activeFlags ?? [];
  const state = flags.includes('waitingOnApproval') ? 'approval' : flags.includes('waitingOnUserInput') ? 'input' : runtime?.type === 'systemError' || (turn?.status==='failed' && s.hasUnreadTurn===true) ? 'failed' : runtime?.type === 'active' ? 'running' : 'idle';
  if(state === 'idle' && !includeIdle) return null;
  const items = turn?.items ?? [];
  const activeItem = [...items].reverse().find(i => i.status === 'inProgress');
  const latest = items.at(-1);
  let detail = operation(activeItem ?? latest);
  if(state==='running' && s.title && ['正在执行任务','正在思考','正在整理回复','正在调用工具','正在搜索资料','正在生成图片','正在执行命令','正在搜索代码','正在查看项目文件'].includes(detail)) detail += ` · ${s.title}`;
  if(state === 'approval') detail = '需要确认操作，请打开原任务处理';
  if(state === 'input') detail = '任务正在等待你的回复';
  if(state === 'failed') detail = '任务遇到异常，请打开原任务查看';
  const comment = [...items].reverse().find(i => i.type === 'agentMessage');
  const text = typeof comment?.text === 'string' ? comment.text : '';
  return {id,title:s.title || '未命名任务',state,detail,progress:text.replace(/\s+/g,' ').slice(0,220),startedAt:turn?.turnStartedAtMs ?? null};
}

const basename = path => String(path ?? '').split(/[\\/]/).at(-1);
function operation(item) {
  if(!item || ['completed','failed','declined','cancelled'].includes(item.status)) return '正在执行任务';
  if(item.type === 'agentMessage') {
    const line = typeof item.text==='string' ? item.text.split(/\r?\n/).map(s=>s.trim()).filter(Boolean).at(-1) ?? '' : '';
    const characters=Array.from(line);
    return line ? `回复 · ${characters.length>28?'…'+characters.slice(-28).join(''):line}` : '正在整理回复';
  }
  if(['mcpToolCall','dynamicToolCall'].includes(item.type)) {
    const title = typeof item.arguments?.title==='string' ? item.arguments.title.trim() : '';
    const tool = [item.server,item.tool].filter(s=>typeof s==='string' && s).join('.');
    return title || tool ? `正在调用工具 · ${title || tool}` : '正在调用工具';
  }
  if(item.type === 'fileChange') return `正在修改 ${basename(item.changes?.[0]?.path) || '文件'}`;
  if(item.type === 'commandExecution') {
    const action = item.commandActions?.find(a => ['read','search','listFiles'].includes(a.type));
    if(action?.type === 'read') return `正在读取 ${basename(action.path || action.name) || '文件'}`;
    const raw = typeof item.command === 'string' ? item.command.trim() : '';
    // The desktop includes the PowerShell launcher path; show the script it actually runs.
    const script = raw.match(/^(?:"[^"\r\n]*[\\/](?:pwsh|powershell)\.exe"|(?:pwsh|powershell)(?:\.exe)?)\s+(?:-(?:NoLogo|NoProfile|NonInteractive)\s+)*-Command\s+([\s\S]+)$/i)?.[1];
    const command = (script ? script.replace(/^(['"])([\s\S]*)\1$/,'$2') : raw).replace(/\s+/g,' ').trim();
    const label = action?.type==='search' ? '正在搜索代码' : action?.type==='listFiles' ? '正在查看项目文件' : /\b(?:npm|pnpm|yarn|node|pytest|dotnet|cargo)\b[^\r\n]*\btest\b/.test(command) ? '正在运行测试' : '正在执行命令';
    return label + (command ? ` · ${command}` : '');
  }
  return ({reasoning:'正在思考',agentMessage:'正在整理回复',webSearch:'正在搜索资料',imageGeneration:'正在生成图片',mcpToolCall:'正在调用工具',dynamicToolCall:'正在调用工具'})[item.type] || '正在执行任务';
}
