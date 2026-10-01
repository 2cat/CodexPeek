import test from 'node:test';
import assert from 'node:assert/strict';
import { present, TaskFeed, readRecentHistory } from './status.mjs';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import net from 'node:net';
import { mkdtempSync, rmSync, mkdirSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { DatabaseSync } from 'node:sqlite';

test('one running conversation shows its actual operation', () => {
  const view = present([{id:'a', state:'running', detail:'正在修改 Login.tsx'}]);
  assert.equal(view.headline, '正在修改 Login.tsx');
  assert.equal(view.running, 1);
});

test('reply status shows the current public text and changes as streamed text advances', () => {
  const feed=new TaskFeed();
  feed.accept({version:11,params:{hostId:'local',conversationId:'reply',change:{type:'snapshot',revision:1,conversationState:{title:'完善状态栏',source:'vscode',threadRuntimeStatus:{type:'active'},turns:[{turnStartedAtMs:1,status:'inProgress',items:[{type:'agentMessage',text:'正在检查 Git 状态'}]}]}}}});
  assert.equal(present(feed.tasks()).headline,'回复 · 正在检查 Git 状态');
  feed.accept({version:11,params:{hostId:'local',conversationId:'reply',change:{type:'patches',baseRevision:1,revision:2,patches:[{op:'replace',path:['turns',0,'items',0,'text'],value:'正在检查 Git 状态\n开始更新 README 文档'}]}}});
  assert.equal(present(feed.tasks()).headline,'回复 · 开始更新 README 文档');
  const longText='🟦'.repeat(30)+'A';
  feed.accept({version:11,params:{hostId:'local',conversationId:'reply',change:{type:'patches',baseRevision:2,revision:3,patches:[{op:'replace',path:['turns',0,'items',0,'text'],value:longText}]}}});
  const previous=present(feed.tasks()).headline;
  assert.ok(previous.startsWith('回复 · …'));
  assert.ok(previous.isWellFormed());
  feed.accept({version:11,params:{hostId:'local',conversationId:'reply',change:{type:'patches',baseRevision:3,revision:4,patches:[{op:'replace',path:['turns',0,'items',0,'text'],value:longText+'B'}]}}});
  assert.notEqual(present(feed.tasks()).headline,previous);
  assert.ok(present(feed.tasks()).headline.endsWith('AB'));
});

test('tool status shows its public action title or exact tool name', () => {
  const feed=new TaskFeed();
  const show=item=>{feed.accept({version:11,params:{hostId:'local',conversationId:'tool',change:{type:'snapshot',revision:1,conversationState:{source:'vscode',threadRuntimeStatus:{type:'active'},turns:[{status:'inProgress',items:[item]}]}}}});return present(feed.tasks()).headline;};
  assert.equal(show({type:'mcpToolCall',status:'inProgress',server:'node_repl',tool:'js',arguments:{title:'检查状态栏文字显示',code:'ignored'}}),'工具 · 检查状态栏文字显示');
  assert.equal(show({type:'mcpToolCall',status:'inProgress',server:'github',tool:'get_commit',arguments:{}}),'工具 · github.get_commit');
});

test('missing action content falls back to the task title without reusing a completed command or internal reasoning', () => {
  const feed=new TaskFeed();
  const show=item=>{feed.accept({version:11,params:{hostId:'local',conversationId:'context',change:{type:'snapshot',revision:1,conversationState:{title:'修复状态栏内容摘要',source:'vscode',threadRuntimeStatus:{type:'active'},turns:[{status:'inProgress',items:[item]}]}}}});return present(feed.tasks()).headline;};
  assert.equal(show({type:'reasoning',content:['INTERNAL_NOT_FOR_DISPLAY']}),'思考 · 修复状态栏内容摘要');
  assert.equal(show({type:'agentMessage',text:''}),'回复 · 修复状态栏内容摘要');
  assert.equal(show({type:'commandExecution',status:'completed',command:'git old-command'}),'任务 · 修复状态栏内容摘要');
});

test('command status includes the command being executed in both the widget and task details', () => {
  const feed=new TaskFeed();
  const show=command=>{
    feed.accept({version:11,params:{hostId:'local',conversationId:'command',change:{type:'snapshot',revision:1,conversationState:{source:'vscode',threadRuntimeStatus:{type:'active'},turns:[{turnStartedAtMs:1000,status:'inProgress',items:[{type:'commandExecution',status:'inProgress',command}]}]}}}});
    return present(feed.tasks());
  };
  const view=show('git status --short\r\n git diff --stat');
  assert.equal(view.headline,'命令 · git status --short git diff --stat');
  assert.equal(view.tasks[0].detail,'正在执行命令 · git status --short git diff --stat');
  assert.equal(show('node --test test.mjs').headline,'测试 · node --test test.mjs');
  assert.equal(show('"C:\\Program Files\\PowerShell\\7\\pwsh.exe" -Command \'git status --short\'').headline,'命令 · git status --short');
  assert.equal(show(null).headline,'正在执行命令');
});

test('search and file-list command actions keep their actual command content', () => {
  const feed=new TaskFeed();
  const show=(type,command)=>{feed.accept({version:11,params:{hostId:'local',conversationId:'search',change:{type:'snapshot',revision:1,conversationState:{threadRuntimeStatus:{type:'active'},turns:[{status:'inProgress',items:[{type:'commandExecution',status:'inProgress',command,commandActions:[{type}]}]}]}}}});return present(feed.tasks()).headline;};
  assert.equal(show('search','rg CodexPeek App.cs'),'搜索 · rg CodexPeek App.cs');
  assert.equal(show('listFiles','rg --files'),'文件 · rg --files');
});

test('desktop snapshots and patches update a conversation, exclude internal work, and reject missing revisions', () => {
  const feed = new TaskFeed();
  const state = {id:'a', title:'登录页重构',source:'vscode',threadSource:'user',threadRuntimeStatus:{type:'active',activeFlags:[]},turnHistory:{kind:'canonical',history:{entitiesByKey:{last:{turnStartedAtMs:1000,status:'inProgress',items:[{type:'fileChange',status:'inProgress',changes:[{path:'C:/app/Login.tsx'}]}]}}}}};
  const msg = (change,version=11) => ({version,params:{hostId:'local',conversationId:'a',change}});
  assert.equal(feed.accept(msg({type:'snapshot',revision:1,conversationState:state})),true);
  assert.equal(feed.tasks()[0].detail,'正在修改 Login.tsx');
  assert.equal(feed.accept(msg({type:'patches',baseRevision:1,revision:2,patches:[{op:'replace',path:['threadRuntimeStatus','activeFlags'],value:['waitingOnApproval']}]})),true);
  assert.equal(feed.tasks()[0].state,'approval');
  assert.equal(feed.accept(msg({type:'patches',baseRevision:1,revision:3,patches:[]})),false);
  assert.equal(feed.tasks().length,0);
  feed.accept(msg({type:'snapshot',revision:4,conversationState:{...state,threadSource:'subagent'}}));
  assert.equal(feed.tasks().length,0);
  assert.equal(feed.accept(msg({type:'snapshot',revision:5,conversationState:state},12)),false);
});

test('waiting takes priority and is excluded from running count; multiple tasks show a count', () => {
  const tasks = [{id:'b',state:'running'}, {id:'a',state:'running'}, {id:'c',state:'approval'}];
  assert.equal(present(tasks).headline, '等待确认 · 1 项');
  assert.equal(present(tasks).running, 2);
  assert.equal(present(tasks).tasks[0].id, 'c');
  assert.equal(present(tasks.slice(0,2)).headline, '正在运行 2 项任务');
  assert.equal(present([{id:'q',state:'input'}]).headline, '等待回复 · 1 项');
  assert.equal(present([{id:'f',state:'failed'}]).headline, '任务异常 · 1 项');
  assert.equal(present(tasks, null, false).headline, '状态未同步');
});

test('quota uses remaining, correct limiting window, and explicit missing/stale data', () => {
  const quota = {rateLimitsByLimitId:{codex:{primary:{usedPercent:85,windowDurationMins:10080,resetsAt:1791051386},secondary:{usedPercent:30,windowDurationMins:300}}}};
  const now = 1791051386000 - (2*24+3)*3600000;
  assert.equal(present([],quota,true,{now}).quotaText, '剩余 15% · 2天3小时后重置');
  assert.equal(present([],null).quotaText, '额度暂不可用');
  assert.equal(present([],{...quota,stale:true},true,{now}).quotaText, '剩余 15% · 2天3小时后重置 · 未更新');
  assert.equal(present([],{rateLimits:{primary:{usedPercent:null,windowDurationMins:300}}}).quotaText, '额度暂不可用');
  const remaining = milliseconds => present([],quota,true,{now:1791051386000-milliseconds}).quotaText;
  assert.equal(remaining(95*60000), '剩余 15% · 1小时35分后重置');
  assert.equal(remaining(20*60000), '剩余 15% · 20分钟后重置');
  assert.equal(remaining(30000), '剩余 15% · 不到1分钟后重置');
  assert.equal(remaining(-1000), '剩余 15% · 待刷新');
  assert.equal(present([],{rateLimits:{primary:{usedPercent:40}}}).quotaText, '剩余 60%');
});

test('idle headline shows the latest task after completion and on a fresh snapshot, without counting it as running', () => {
  const feed=new TaskFeed();
  const snapshot=(id,title,startedAt,runtime='idle',threadSource='user') => feed.accept({version:11,params:{hostId:'local',conversationId:id,change:{type:'snapshot',revision:1,conversationState:{title,source:'vscode',threadSource,threadRuntimeStatus:{type:runtime},turns:[{turnStartedAtMs:startedAt,status:runtime==='active'?'inProgress':'completed',items:[]}]}}}});
  snapshot('new','刚完成的任务',3000,'active');
  assert.equal(present(feed.tasks(),null,true,{lastTask:feed.lastTask()}).running,1);
  snapshot('new','刚完成的任务',3000);
  snapshot('old','较早的任务',1000);
  snapshot('internal','内部任务',4000,'idle','subagent');
  assert.equal(feed.lastTask().title,'刚完成的任务');
  const view=present(feed.tasks(),null,true,{lastTask:feed.lastTask()});
  assert.equal(view.headline,'上次：刚完成的任务');
  assert.equal(view.running,0);
  assert.equal(view.tasks.length,0);
  assert.equal(view.tone,'muted');
  assert.equal(present([],null,false,{lastTask:feed.lastTask()}).headline,'状态未同步');
  feed.clear();
  assert.equal(feed.lastTask(),null);
  assert.equal(present([],null,true,{lastTask:feed.lastTask()}).headline,'暂无运行任务');
});

test('the popup keeps the three most recent ended user tasks separate from active work', () => {
  const feed=new TaskFeed();
  const snapshot=(id,startedAt,{runtime='idle',status='completed',threadSource='user',items=[]}={})=>feed.accept({version:11,params:{hostId:'local',conversationId:id,change:{type:'snapshot',revision:1,conversationState:{title:id,source:'vscode',threadSource,threadRuntimeStatus:{type:runtime},turns:[{turnStartedAtMs:startedAt,status,items}]}}}});
  snapshot('old',1000);
  snapshot('third',2000);
  snapshot('second',3000,{status:'failed'});
  snapshot('latest',4000,{items:[{type:'agentMessage',status:'completed',text:'已经完成。\n 验证通过。'}]});
  snapshot('active',5000,{runtime:'active',status:'inProgress'});
  snapshot('internal',6000,{threadSource:'subagent'});
  snapshot('unstarted',null);
  for(const [id,status,runtime] of [['sparse','inProgress',undefined],['unknown','newStatus',{type:'unknown'}]]) feed.accept({version:11,params:{hostId:'local',conversationId:id,change:{type:'snapshot',revision:1,conversationState:{title:id,source:'vscode',threadRuntimeStatus:runtime,turns:[{turnStartedAtMs:9000,status,items:[]}]}}}});
  const recentTasks=feed.recentTasks();
  assert.deepEqual(recentTasks.map(t=>t.id),['latest','second','third']);
  assert.ok(recentTasks.every(t=>t.state==='completed' && !t.detail.includes('正在')));
  assert.equal(recentTasks[0].progress,'已经完成。 验证通过。');
  assert.equal(recentTasks[1].detail,'上次执行失败');
  const view=present(feed.tasks(),null,true,{lastTask:feed.lastTask(),recentTasks});
  assert.equal(view.running,1);
  assert.deepEqual(view.tasks.map(t=>t.id),['active']);
  assert.deepEqual(view.recentTasks.map(t=>t.id),['latest','second','third']);
  snapshot('latest',7000,{runtime:'active',status:'inProgress'});
  assert.deepEqual(feed.recentTasks().map(t=>t.id),['second','third','old']);
  assert.deepEqual(present(feed.tasks(),null,false,{recentTasks:feed.recentTasks()}).recentTasks,[]);
});

test('unloaded history requires a final public lifecycle event and yields to live Desktop state', () => {
  const folder=mkdtempSync(path.join(tmpdir(),'codex-peek-history-'));
  mkdirSync(path.join(folder,'sessions'));
  const records=[];
  const rollout=(id,events)=>{const rollout_path=path.join(folder,'sessions',id+'.jsonl');writeFileSync(rollout_path,events.map(payload=>JSON.stringify({type:'event_msg',payload})).join('\n')+'\n');records.push({id,title:id,rollout_path});};
  try {
    rollout('older',[{type:'task_complete',started_at:1,last_agent_message:'上次回复'}]);
    rollout('latest',[{type:'task_started',turn_id:'new',started_at:4},{type:'task_complete',turn_id:'new',started_at:4,last_agent_message:'已结束。\n  验证通过。'}]);
    rollout('unfinished',[{type:'task_complete',started_at:6},{type:'task_started',started_at:7}]);
    rollout('stopped',[{type:'task_started',turn_id:'stop',started_at:3},{type:'turn_aborted',turn_id:'stop'}]);
    rollout('live',[{type:'task_complete',started_at:8}]);
    rollout('late-old-completion',[{type:'task_started',turn_id:'old',started_at:1},{type:'task_complete',turn_id:'old',started_at:1},{type:'task_started',turn_id:'new',started_at:9},{type:'task_complete',turn_id:'old',started_at:1}]);
    rollout('missing-end-id',[{type:'task_started',turn_id:'new',started_at:10},{type:'task_complete',started_at:1}]);
    const history=readRecentHistory(records,folder);
    assert.deepEqual(history.map(t=>t.id),['older','latest','stopped','live']);
    assert.equal(history.find(t=>t.id==='latest').startedAt,4000);
    assert.equal(history.find(t=>t.id==='latest').progress,'已结束。 验证通过。');
    assert.equal(history.find(t=>t.id==='stopped').detail,'上次已停止');
    const feed=new TaskFeed();
    const snapshot=(id,runtime,title,startedAt)=>feed.accept({version:11,params:{hostId:'local',conversationId:id,change:{type:'snapshot',revision:1,conversationState:{title,source:'vscode',threadRuntimeStatus:{type:runtime,activeFlags:runtime==='active'?['waitingOnApproval']:[]},turns:[{turnStartedAtMs:startedAt,status:runtime==='active'?'inProgress':'completed',items:[]}]}}}});
    snapshot('live','active','正在等待确认',9000);
    snapshot('latest','idle','权威的新标题',5000);
    const recent=feed.recentTasks(history);
    assert.deepEqual(recent.map(t=>t.id),['latest','stopped','older']);
    assert.equal(recent[0].title,'权威的新标题');
    assert.equal(recent[0].startedAt,5000);
    assert.equal(present(feed.tasks(),null,true,{recentTasks:recent}).waiting,1);
    assert.deepEqual(feed.recentTasks([...history,history.find(t=>t.id==='stopped')]).map(t=>t.id),['latest','stopped','older']);
  } finally {rmSync(folder,{recursive:true,force:true});}
});

test('history reads a bounded complete tail and rejects private, invalid, or escaped records', () => {
  const folder=mkdtempSync(path.join(tmpdir(),'codex-peek-history-'));
  mkdirSync(path.join(folder,'sessions'));
  mkdirSync(path.join(folder,'archived_sessions'));
  const rows=[];
  const file=(id,location,text)=>{const rollout_path=path.join(folder,location,id+'.jsonl');writeFileSync(rollout_path,text);rows.push({id,title:id,rollout_path});};
  const complete=JSON.stringify({type:'event_msg',payload:{type:'task_complete',started_at:10,last_agent_message:'公开的结果'}})+'\n';
  try {
    file('tail','sessions','x'.repeat(300*1024)+'\n'+complete);
    file('archived','archived_sessions',complete);
    file('outside','.',complete);
    file('malformed','sessions','broken\n'+complete+'{unfinished');
    file('no-time','sessions',JSON.stringify({type:'event_msg',payload:{type:'task_complete'}}));
    file('private','sessions',JSON.stringify({type:'response_item',payload:{type:'task_complete',started_at:20,last_agent_message:'INTERNAL_NOT_FOR_DISPLAY'}}));
    rows.push({id:'missing',rollout_path:path.join(folder,'sessions','missing.jsonl')});
    assert.deepEqual(readRecentHistory(rows,folder).map(t=>t.id),['tail','archived']);
    assert.deepEqual(readRecentHistory(Array.from({length:12},()=>rows.at(-1)).concat(rows[0]),folder),[]);
  } finally {rmSync(folder,{recursive:true,force:true});}
});

test('completed operations are not presented as running; unread failed turns remain actionable', () => {
  const feed=new TaskFeed();
  const state={title:'任务',source:'vscode',threadRuntimeStatus:{type:'active',activeFlags:[]},turns:[{turnStartedAtMs:1,status:'inProgress',items:[{type:'fileChange',status:'completed',changes:[{path:'a.txt'}]}]}]};
  const update=s=>feed.accept({version:11,params:{hostId:'local',conversationId:'a',change:{type:'snapshot',revision:1,conversationState:s}}});
  update(state);
  assert.equal(feed.tasks()[0].detail,'正在执行任务 · 任务');
  update({...state,hasUnreadTurn:true,threadRuntimeStatus:{type:'idle'},turns:[{status:'failed',error:{message:'test failure'}}]});
  assert.equal(feed.tasks()[0].state,'failed');
  update({...state,hasUnreadTurn:false,threadRuntimeStatus:{type:'idle'},turns:[{status:'failed'}]});
  assert.equal(feed.tasks().length,0);
});

test('waiting duration starts on the observed transition and clears when work resumes', () => {
  const feed=new TaskFeed();
  const set=(flags,time)=>feed.accept({version:11,params:{hostId:'local',conversationId:'a',change:{type:'snapshot',revision:time,conversationState:{threadRuntimeStatus:{type:'active',activeFlags:flags}}}}},time);
  set(['waitingOnApproval'],1000);assert.equal(feed.tasks()[0].waitingSince,1000);
  set(['waitingOnApproval'],5000);assert.equal(feed.tasks()[0].waitingSince,1000);
  set([],6000);assert.equal(feed.tasks()[0].waitingSince,null);
  set(['waitingOnUserInput'],7000);assert.equal(feed.tasks()[0].waitingSince,7000);
});

test('real framed transport discovers catalog tasks, keeps ended history when idle, and clears disconnected state', {timeout:8000}, async () => {
  const folder=mkdtempSync(path.join(tmpdir(),'codex-peek-test-'));
  mkdirSync(path.join(folder,'sessions'));
  const db=new DatabaseSync(path.join(folder,'state_5.sqlite'));
  db.exec("CREATE TABLE threads(id TEXT,updated_at_ms INTEGER,archived INTEGER,source TEXT,originator TEXT,thread_source TEXT,title TEXT,rollout_path TEXT); INSERT INTO threads(id,updated_at_ms,archived,source,originator,thread_source) VALUES('test-task',10,0,'vscode','Codex Desktop','user')");
  const add=(id,events,inside=true)=>{const rollout=path.join(folder,inside?'sessions':'.',id+'.jsonl');writeFileSync(rollout,events.map(payload=>JSON.stringify({type:'event_msg',payload})).join('\n')+'\n');db.prepare("INSERT INTO threads VALUES(?,1,0,'vscode','Codex Desktop','user',?,?)").run(id,id,rollout);};
  add('unloaded-new',[{type:'task_complete',started_at:3,last_agent_message:'最近一次公开回复'}]);
  add('unloaded-middle',[{type:'task_complete',started_at:2}]);
  add('unloaded-old',[{type:'task_complete',started_at:1}]);
  add('unloaded-running',[{type:'task_complete',started_at:4},{type:'task_started',started_at:5}]);
  add('outside',[{type:'task_complete',started_at:6}],false);
  const previousRollout=path.join(folder,'sessions','test-task.jsonl');
  writeFileSync(previousRollout,JSON.stringify({type:'event_msg',payload:{type:'task_complete',started_at:9,last_agent_message:'旧轮回复'}})+'\n');
  db.prepare('UPDATE threads SET title=?,rollout_path=? WHERE id=?').run('旧的当前任务',previousRollout,'test-task');
  db.close();
  const pipe='\\\\.\\pipe\\codex-peek-test-'+process.pid;
  const sockets=new Set();
  let finishTask,loseRevision,waitingSeen=false,lostView=null;
  const server=net.createServer(socket=>{
    sockets.add(socket);socket.on('close',()=>sockets.delete(socket));let buffer=Buffer.alloc(0),sentInitial=false;
    const send=message=>{const data=Buffer.from(JSON.stringify(message)),head=Buffer.alloc(4);head.writeUInt32LE(data.length);const frame=Buffer.concat([head,data]);socket.write(frame.subarray(0,3));setTimeout(()=>{if(!socket.destroyed)socket.write(frame.subarray(3));},5);};
    finishTask=()=>send({type:'broadcast',method:'thread-stream-state-changed',version:11,sourceClientId:'desktop',params:{hostId:'local',conversationId:'test-task',change:{type:'snapshot',revision:2,conversationState:{source:'vscode',title:'测试任务',threadRuntimeStatus:{type:'idle'},turns:[{turnStartedAtMs:10000,status:'completed',items:[]}]}}}});
    loseRevision=()=>send({type:'broadcast',method:'thread-stream-state-changed',version:11,sourceClientId:'desktop',params:{hostId:'local',conversationId:'test-task',change:{type:'patches',baseRevision:99,revision:100,patches:[]}}});
    socket.on('data',chunk=>{buffer=Buffer.concat([buffer,chunk]);while(buffer.length>=4&&buffer.length>=buffer.readUInt32LE()+4){const size=buffer.readUInt32LE();const m=JSON.parse(buffer.subarray(4,size+4));buffer=buffer.subarray(size+4);
      if(m.method==='initialize')send({type:'response',method:'initialize',resultType:'success',result:{clientId:'test-client'}});
      if(m.method==='thread-stream-following-changed'&&m.params.conversationId==='test-task'&&m.params.following&&!sentInitial){sentInitial=true;send({type:'broadcast',method:'thread-stream-state-changed',version:11,sourceClientId:'desktop',params:{hostId:'local',conversationId:'test-task',change:{type:'snapshot',revision:1,conversationState:{source:'vscode',title:'测试任务',threadRuntimeStatus:{type:'active',activeFlags:['waitingOnApproval']}}}}});}
    }});
  });
  await new Promise(resolve=>server.listen(pipe,resolve));
  const child=spawn(process.execPath,['--no-warnings','backend.mjs','--no-quota'],{cwd:import.meta.dirname,env:{...process.env,CODEX_HOME:folder,CODEX_PEEK_PIPE:pipe}});
  const views=[];let pending='';
  try {
    await new Promise((resolve,reject)=>{
      const timer=setTimeout(()=>reject(Error('No approval/idle/disconnect transition from test pipe')),5000);
      child.stdout.on('data',chunk=>{pending+=chunk;const lines=pending.split('\n');pending=lines.pop();for(const line of lines){if(!line)continue;const view=JSON.parse(line);views.push(view);if(view.waiting===1&&!waitingSeen){waitingSeen=true;loseRevision();}else if(view.connected&&waitingSeen&&!lostView&&view.waiting===0&&view.tasks.length===0){lostView=view;finishTask();}if(view.headline==='上次：测试任务')for(const socket of sockets)socket.destroy();if(views.some(v=>v.headline==='上次：测试任务')&&!view.connected){clearTimeout(timer);resolve();}}});
      child.on('error',reject);
    });
    assert.equal(views.find(v=>v.waiting===1).tasks[0].title,'测试任务');
    assert.deepEqual(views.find(v=>v.waiting===1).recentTasks.map(t=>t.id),['unloaded-new','unloaded-middle','unloaded-old']);
    assert.deepEqual(lostView.recentTasks.map(t=>t.id),['unloaded-new','unloaded-middle','unloaded-old']);
    assert.equal(views.find(v=>v.headline==='上次：测试任务').running,0);
    assert.deepEqual(views.find(v=>v.headline==='上次：测试任务').recentTasks.map(t=>[t.id,t.state]),[['test-task','completed'],['unloaded-new','completed'],['unloaded-middle','completed']]);
    assert.equal(views.at(-1).tasks.length,0);
    assert.deepEqual(views.at(-1).recentTasks,[]);
  } finally {child.stdin.end();await once(child,'exit');for(const socket of sockets)socket.destroy();await new Promise(resolve=>server.close(resolve));if(path.dirname(folder)===tmpdir())rmSync(folder,{recursive:true,force:true});}
});
