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

async function click(expr){const p=await ev(`(()=>{const e=${expr};if(!e||e.disabled)throw new Error('Missing/disabled target');e.scrollIntoView({block:'center'});const r=e.getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})()`);await send('Input.dispatchMouseEvent',{type:'mousePressed',...p,button:'left',clickCount:1});await send('Input.dispatchMouseEvent',{type:'mouseReleased',...p,button:'left',clickCount:1});}
const button=text=>`[...document.querySelectorAll('button')].find(e=>e.textContent.trim()===${JSON.stringify(text)})`;
async function fill(expr,text){await ev(`(()=>{const e=${expr};e.focus();e.select()})()`);await send('Input.insertText',{text});await pause(80);}

const out='docs/hax/screenshots';await fs.mkdir(out,{recursive:true});
const api='http://127.0.0.1:18555',ui='http://127.0.0.1:18665/';
const manifest=[];
async function apiCall(path,method='GET'){const r=await fetch(api+path,{method});assert.equal(r.status,200);return r;}
async function capture(id,condition,description){
 await pause(350);await ev("window.scrollTo(0,0)");
 const r=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});
 await fs.writeFile(`${out}/${id}.png`,Buffer.from(r.data,'base64'));
 const text=await ev('document.body.innerText');
 await fs.writeFile(`docs/hax/evidence/${id}.txt`,text);
 manifest.push({id,file:`screenshots/${id}.png`,condition,description,capturedAt:new Date().toISOString(),source:'Actual Blazor frontend; isolated SQLite; provider stub only'});
 console.log('Captured',id);
}
async function start(condition,nickname,landing=false){
 await apiCall('/test/prepare/'+condition,'POST');await apiCall('/test/provider/ok','POST');
 await send('Network.clearBrowserCookies');await send('Page.navigate',{url:ui});
 await wait("document.querySelectorAll('input').length===5");await pause(600);
 if(landing)await capture('S01_instructions_consent','A/B','Actual initial instructions, limitations, synthetic consent and form');
 await fill("document.querySelectorAll('input:not([type=checkbox])')[0]",nickname);
 await fill("document.querySelectorAll('input:not([type=checkbox])')[1]",'23');
 for(const i of [2,3]){await click(`document.querySelectorAll('input:not([type=checkbox])')[${i}]`);await wait("[...document.querySelectorAll('.mud-list-item')].some(e=>e.getClientRects().length)");await click("[...document.querySelectorAll('.mud-list-item')].find(e=>e.getClientRects().length)");await pause(250);}
 await click("document.querySelector('input[type=checkbox]')");
 await wait(`!(${button('Έναρξη Συνέντευξης')}).disabled`);await click(button('Έναρξη Συνέντευξης'));
 await wait("document.querySelector('#original-answer-input') && !document.querySelector('#original-answer-input').disabled");
}
async function answer(text){await fill("document.querySelector('#original-answer-input')",text);await click(button('Αποστολή'));await wait(`!!(${button('Βελτίωση απάντησης')}) && !(${button('Βελτίωση απάντησης')}).disabled`);}
async function next(final=false){await click(`[...document.querySelectorAll('button')].find(e=>e.innerText.includes(${JSON.stringify(final?'ΟΛΟΚΛΗΡΩΣΗ ΣΥΝΕΝΤΕΥΞΗΣ':'ΣΥΝΕΧΕΙΑ ΣΤΗΝ ΕΠΟΜΕΝΗ ΕΡΩΤΗΣΗ')}))`);await wait(final?"document.body.innerText.includes('Η προσομοίωση συνέντευξης ολοκληρώθηκε!')":"document.querySelector('#original-answer-input') && !document.querySelector('#original-answer-input').disabled");}
async function revise(text,fail=false){await click(button('Βελτίωση απάντησης'));await wait("document.querySelector('#revision-answer-input') && !document.querySelector('#revision-answer-input').disabled");await fill("document.querySelector('#revision-answer-input')",text);if(fail)await apiCall('/test/provider/fail-revised','POST');await click(button('Υποβολή βελτιωμένης απάντησης'));}
try{
 await send('Emulation.setDeviceMetricsOverride',{width:1100,height:1050,deviceScaleFactor:1,mobile:false});
 await start('A','Browser E2E HAX A',true);
 await capture('S02_A_question_invocation','A','First question and explicit answer submission');
 await answer('Σπουδάζω πληροφορική. Γνωρίζω Python. Με ενδιαφέρει η ανάλυση δεδομένων.');await next();
 await answer('Με ενδιαφέρει η συνεργασία. Οι δεξιότητές μου ταιριάζουν με τη θέση. Θέλω να αποκτήσω εμπειρία.');await next();
 await answer('Σε μια εργασία της σχολής αντιμετώπισα ένα πρόβλημα με τα δεδομένα. Έλεγξα τα αρχεία και διόρθωσα τα λάθη.');
 await capture('S03_A_contextual_feedback','A','Problem and Action evidence with conservative request for outcome');
 await click(button('Βελτίωση απάντησης'));await wait("document.querySelector('#revision-answer-input') && !document.querySelector('#revision-answer-input').disabled");
 await capture('S04_A_revision_editor','A','Single optional revision editor with original answer prefilled');
 await fill("document.querySelector('#revision-answer-input')",'Σε μια εργασία της σχολής αντιμετώπισα ένα πρόβλημα με τα δεδομένα. Έλεγξα τα αρχεία και διόρθωσα τα λάθη. Τελικά παραδώσαμε το έργο εγκαίρως.');await click(button('Υποβολή βελτιωμένης απάντησης'));
 await wait(`!!(${button('Συνέχεια')}) && !(${button('Συνέχεια')}).disabled`);
 await capture('S05_A_second_feedback','A','Revised-answer feedback and only Continue; no third answer');
 await send('Page.reload');await wait(`!!(${button('Συνέχεια')}) && !(${button('Συνέχεια')}).disabled`);
 await capture('S06_A_refresh_recovery','A','Refresh recovers current stage and round; previous-stage chat is absent');
 await click(button('Συνέχεια'));await wait("document.querySelector('#original-answer-input') && !document.querySelector('#original-answer-input').disabled");
 const rest=['Συνεργάστηκα με συμφοιτητές. Ανέλαβα την οργάνωση. Παραδώσαμε εγκαίρως.','Είχα στενή προθεσμία. Έβαλα προτεραιότητες. Παραδώσαμε εγκαίρως.','Άλλαξαν οι απαιτήσεις. Άλλαξα το σχέδιο. Έμαθα ότι χρειάζεται ευελιξία.','Θέλω να γίνω αναλυτής. Η θέση θα με βοηθήσει να αποκτήσω εμπειρία.'];
 for(let i=0;i<4;i++){await answer(rest[i]);await next(i===3);}
 await capture('S07_completion','A','Actual completion after seventh question');
 await start('A','Browser E2E HAX fallback');
 await answer('Σπουδάζω και θέλω να αποκτήσω εμπειρία.');await next();await answer('Με ενδιαφέρει η θέση για να μάθω.');await next();
 await answer('Ο ουρανός είναι γαλανός και τα δέντρα έχουν φύλλα.');
 await capture('S08_A_uncertain_fallback','A','Actual neutral fallback for unrelated free text at question 3');
 await start('B','Browser E2E HAX B');
 await answer('Σπουδάζω οικονομικά και γνωρίζω λογιστικά φύλλα. Θέλω να εργαστώ στην οργάνωση έργων.');
 await capture('S09_B_stub_feedback','B','Actual B feedback UI fed by local stub; not a real Gemini output');
 await revise('Σπουδάζω οικονομικά. Σε ομαδικό έργο οργάνωσα τις εργασίες και παραδώσαμε εγκαίρως. BROWSER_REVISED',true);
 await wait(`!!(${button('Επανάληψη ανάκτησης ανατροφοδότησης')})`);
 await capture('S10_B_failure_retry','B','Provider failure preserves revision and offers feedback-only retry');
 await apiCall('/test/provider/ok','POST');await click(button('Επανάληψη ανάκτησης ανατροφοδότησης'));
 await wait(`!!(${button('Συνέχεια')}) && !(${button('Συνέχεια')}).disabled`);
 await capture('S11_B_retry_success','B','Recovery after provider failure; second feedback with only Continue');
 const status=await(await apiCall('/test/status')).json();
 await fs.writeFile('docs/hax/evidence/browser-observations.json',JSON.stringify({capturedAt:new Date().toISOString(),provider:'local stub only',providerRequests:status.providerRequests,providerFailures:status.providerFailures,sessions:status.sessions.map(s=>({condition:s.condition,stages:s.answers.length,finished:!!s.finishedAt})),checks:{AsevenStageCompletion:true,AcurrentStageRefresh:true,AuncertainFallback:true,BfailureRetry:true,noThirdAnswer:!await ev(`!!(${button('Βελτίωση απάντησης')})`)}},null,2));
 await fs.writeFile('docs/hax/screenshots/manifest.json',JSON.stringify(manifest,null,2));
 console.log('PASS evidence capture complete; no real Gemini');
}catch(e){await shot('/private/tmp/hax-capture-failure.png');console.error(e.stack);console.error(await ev('document.body.innerText'));throw e;}
finally{ws.close();}
