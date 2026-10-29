// Requires the isolated --browser-host, local Blazor frontend and an isolated Chrome DevTools port 19223.
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import { tmpdir } from 'node:os';
const tabs = await (await fetch('http://127.0.0.1:19223/json/list')).json();
const tab = tabs.find(x=>x.type==='page');
export const ws = new WebSocket(tab.webSocketDebuggerUrl);
await new Promise((resolve,reject)=>{ws.addEventListener('open',resolve,{once:true});ws.addEventListener('error',reject,{once:true});});
let seq=0; const pending=new Map();
ws.addEventListener('message',event=>{const msg=JSON.parse(event.data);if(msg.id&&pending.has(msg.id)){const {resolve,reject,timer}=pending.get(msg.id);clearTimeout(timer);pending.delete(msg.id);msg.error?reject(new Error(JSON.stringify(msg.error))):resolve(msg.result);}});
export function send(method,params={}) {return new Promise((resolve,reject)=>{const id=++seq;const timer=setTimeout(()=>{pending.delete(id);reject(new Error('CDP timeout '+method));},15000);pending.set(id,{resolve,reject,timer});ws.send(JSON.stringify({id,method,params}));});}
export async function ev(expression){const r=await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(r.exceptionDetails)throw new Error(r.exceptionDetails.text+': '+JSON.stringify(r.exceptionDetails.exception));return r.result?.value;}
export const pause = ms=>new Promise(r=>setTimeout(r,ms));
export async function wait(expression,timeout=15000){const start=Date.now();while(Date.now()-start<timeout){if(await ev(expression))return;await pause(100);}throw new Error('Wait failed: '+expression+'\n'+await ev('document.body.innerText'));}
export async function shot(path){const r=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});await fs.writeFile(path,Buffer.from(r.data,'base64'));}
await send('Page.enable'); await send('Runtime.enable');

const artifacts = process.env.INTERVIEW_E2E_ARTIFACT_DIR ?? tmpdir();
await fs.mkdir(artifacts, {recursive:true});
const api='http://127.0.0.1:18555';
const ui=process.env.INTERVIEW_E2E_UI ?? 'http://127.0.0.1:18665/';
async function apiCall(path,method='GET'){const r=await fetch(api+path,{method});assert.equal(r.status,200,path);return r;}
async function click(expr){const p=await ev(`(()=>{const e=${expr};if(!e||e.disabled)throw new Error('Missing/disabled target');e.scrollIntoView({block:'center'});const r=e.getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})()`);await send('Input.dispatchMouseEvent',{type:'mousePressed',...p,button:'left',clickCount:1});await send('Input.dispatchMouseEvent',{type:'mouseReleased',...p,button:'left',clickCount:1});}
const button=text=>`[...document.querySelectorAll('button')].find(e=>e.textContent.trim()===${JSON.stringify(text)})`;
async function fill(expr,text){await ev(`(()=>{const e=${expr};e.focus();e.select()})()`);await send('Input.insertText',{text});await pause(80);}
const answers=[
 'Σπουδάζω πληροφορική. Γνωρίζω Python. Με ενδιαφέρει η τεχνολογία.',
 'Με ενδιαφέρει η συνεργασία. Οι δεξιότητές μου ταιριάζουν με τη θέση. Θέλω να αποκτήσω εμπειρία.',
 'Αντιμετώπισα ένα πρόβλημα. Έλεγξα τα δεδομένα. Παραδώσαμε εγκαίρως.',
 'Συνεργάστηκα με συμφοιτητές. Ανέλαβα την οργάνωση. Παραδώσαμε εγκαίρως.',
 'Είχα στενή προθεσμία. Έβαλα προτεραιότητες. Παραδώσαμε εγκαίρως.',
 'Άλλαξαν οι απαιτήσεις. Άλλαξα το σχέδιο. Έμαθα ότι χρειάζεται ευελιξία.',
 'Θέλω να γίνω αναλυτής. Η θέση θα με βοηθήσει να αποκτήσω εμπειρία. Θα παρακολουθήσω μαθήματα για ανάλυση.'
];
const results=[];
try {
 await send('Emulation.setDeviceMetricsOverride',{width:1280,height:1000,deviceScaleFactor:1,mobile:false});
 for(const condition of ['A','B']) for(const revise of [false,true]){
  const previousIds = new Set((await (await apiCall('/test/status')).json()).sessions.map(s=>s.id));
  await apiCall('/test/prepare/'+condition,'POST');await apiCall('/test/provider/ok','POST');
  await send('Network.clearBrowserCookies');
  await ev("(()=>{try{sessionStorage.clear()}catch{}})()");
  await send('Page.navigate',{url:ui});
  await wait("document.querySelectorAll('input').length===5");await pause(600);
  await fill("document.querySelectorAll('input:not([type=checkbox])')[0]",`Browser E2E ${condition} ${revise}`);
  await fill("document.querySelectorAll('input:not([type=checkbox])')[1]",'22');
  for(const index of [2,3]){
   await click(`document.querySelectorAll('input:not([type=checkbox])')[${index}]`);
   await wait("[...document.querySelectorAll('.mud-list-item')].some(e=>e.getClientRects().length)");
   await click("[...document.querySelectorAll('.mud-list-item')].find(e=>e.getClientRects().length)");await pause(100);
  }
  await click("document.querySelector('input[type=checkbox]')");
  await wait(`!!(${button('Έναρξη Συνέντευξης')}) && !(${button('Έναρξη Συνέντευξης')}).disabled`);
  await click(button('Έναρξη Συνέντευξης'));
  await wait("document.querySelector('#original-answer-input')!==null");
  const status=await (await apiCall('/test/status')).json();
  const current=status.sessions.find(s=>s.condition===condition&&!s.finishedAt&&!previousIds.has(s.id));
  assert(current,'expected assigned condition '+condition);
  for(let stage=1;stage<=7;stage++){
   await wait("document.querySelector('#original-answer-input') && !document.querySelector('#original-answer-input').disabled");
   await fill("document.querySelector('#original-answer-input')",answers[stage-1]);
   await wait(`!!(${button('Αποστολή')}) && !(${button('Αποστολή')}).disabled`);
   await click(button('Αποστολή'));
   await wait(`!!(${button('Βελτίωση απάντησης')}) && !(${button('Βελτίωση απάντησης')}).disabled`);
   if(stage===3){
    await send('Page.reload');
    await wait(`!!(${button('Βελτίωση απάντησης')}) && !(${button('Βελτίωση απάντησης')}).disabled`);
    const resumed=(await (await apiCall('/test/status')).json()).sessions.find(s=>s.id===current.id);
    assert.equal(resumed.answers.length,3,'reload resumes existing session without duplicate submission');
    assert.equal(await ev("document.body.innerText.includes('Έναρξη Συνέντευξης')"),false,'reload must not restart registration');
   }
   if(revise){
    await click(button('Βελτίωση απάντησης'));
    await wait("document.querySelector('#revision-answer-input') && !document.querySelector('#revision-answer-input').disabled");
    await fill("document.querySelector('#revision-answer-input')",answers[stage-1]+' BROWSER_REVISED');
    if(condition==='B'&&stage===1)await apiCall('/test/provider/fail-revised','POST');
    await click(button('Υποβολή βελτιωμένης απάντησης'));
    if(condition==='B'&&stage===1){
     await wait(`!!(${button('Επανάληψη ανάκτησης ανατροφοδότησης')})`);
     const failed=await (await apiCall('/test/status')).json();
     const row=failed.sessions.find(s=>s.id===current.id).answers[0];
     assert(row.revisedAnswerText.endsWith('BROWSER_REVISED')&&!row.revisedFeedbackText&&!row.stageCompletedAt,'failure preserved revision without completion');
     await apiCall('/test/provider/ok','POST');
     await click(button('Επανάληψη ανάκτησης ανατροφοδότησης'));
    }
    await wait(`!!(${button('Συνέχεια')}) && !(${button('Συνέχεια')}).disabled`);
    assert.equal(await ev(`!!(${button('Βελτίωση απάντησης')}) || !!document.querySelector('#revision-answer-input')`),false,'no third answer');
    if(stage===7)await shot(`${artifacts}/interview-${condition}-revised-final.png`);
    await click(button('Συνέχεια'));
   }else{
    await click(`[...document.querySelectorAll('button')].find(e=>e.innerText.includes(${JSON.stringify(stage===7?'ΟΛΟΚΛΗΡΩΣΗ ΣΥΝΕΝΤΕΥΞΗΣ':'ΣΥΝΕΧΕΙΑ ΣΤΗΝ ΕΠΟΜΕΝΗ ΕΡΩΤΗΣΗ')}))`);
   }
   if(stage<7)await wait("document.querySelector('#original-answer-input') && !document.querySelector('#original-answer-input').disabled");
  }
  await wait("document.body.innerText.includes('Η προσομοίωση συνέντευξης ολοκληρώθηκε!')");
  const done=(await (await apiCall('/test/status')).json()).sessions.find(s=>s.id===current.id);
  assert(done.finishedAt&&done.answers.length===7,'seven stages completed');
  for(const [i,row]of done.answers.entries()){
   assert.equal(row.stageNumber,i+1);assert.equal(row.answerText,answers[i]);assert(row.feedbackText&&row.stageCompletedAt);
   assert(row.originalWritingMilliseconds!==null&&row.originalWritingMilliseconds>=0,'browser writing captured');
   if(i!==2) assert(row.stageElapsedMilliseconds>=row.originalWritingMilliseconds,'elapsed timing consistent'); else assert.equal(row.stageElapsedMilliseconds,null,'lost browser origin remains null after reload');
   if(revise){assert.equal(row.revisedAnswerText,answers[i]+' BROWSER_REVISED');assert(row.revisedFeedbackText);if(i!==2)assert(row.revisionWritingMilliseconds!==null&&row.revisedFeedbackWaitMilliseconds!==null);else assert.equal(row.revisionWritingMilliseconds,null,'do not invent pre-reload writing origins');}
   else assert(!row.revisedAnswerText&&!row.revisedFeedbackText);
  }
  await send('Page.reload');
  await wait("document.body.innerText.includes('Η προσομοίωση συνέντευξης ολοκληρώθηκε!')");
  results.push({condition,revise,sessionId:current.id,stages:7,status:'passed'});
  console.log('PASS browser',condition,'revision='+revise,'seven stages, UI and persisted timings');
 }
 const csvResponse=await fetch(api+'/api/Interview/export/csv',{headers:{'X-Export-Key':'t'.repeat(32)}});assert.equal(csvResponse.status,200);
 await fs.writeFile(artifacts+'/interview-browser-export.csv',await csvResponse.text());
 await fs.writeFile(artifacts+'/interview-browser-results.json',JSON.stringify({results,status:await (await apiCall('/test/status')).json()},null,2));
 console.log('PASS all four browser scenarios; B used local stub only. CSV saved.');
}catch(e){await shot(artifacts+'/interview-browser-failure.png');console.error(e.stack);console.error(await ev('document.body.innerText'));throw e;}
finally{ws.close();}
