const states = new WeakMap();
export function mount(element, mode, initial, items) {
  const old = states.get(element), signature = JSON.stringify([mode,initial,items]);
  if (old?.signature === signature) return;
  dispose(element);
  const state = {signature,remaining:initial,interval:null,deadline:0};
  const timerOutput = () => { const out=element.querySelector('.hl-timer-output'); if(out) out.textContent=String(Math.floor(state.remaining/60)).padStart(2,'0')+':'+String(state.remaining%60).padStart(2,'0'); };
  const stop = () => { clearInterval(state.interval);state.interval=null; const button=element.querySelector('[data-action="toggle"]');if(button)button.textContent='Start'; };
  state.listener = event => {
    if(mode==='calculator') {
      const first=element.querySelector('[data-number="a"]'),second=element.querySelector('[data-number="b"]'),output=element.querySelector('.hl-tool-result');
      const a=Number(first.value),b=Number(second.value),op=element.querySelector('[data-operation]').value;
      if(first.value===''||second.value===''||!Number.isFinite(a)||!Number.isFinite(b)){output.textContent='Enter two finite numbers.';return;}
      if(op==='divide'&&b===0){output.textContent='Cannot divide by zero.';return;}
      const result=op==='subtract'?a-b:op==='multiply'?a*b:op==='divide'?a/b:a+b;
      output.textContent=Number.isFinite(result)?new Intl.NumberFormat(undefined,{maximumSignificantDigits:12}).format(result):'Result is too large.';
    } else if(mode==='checklist') {
      const all=[...element.querySelectorAll('input[type="checkbox"]')];element.querySelector('.hl-tool-status').textContent=all.filter(x=>x.checked).length+' of '+all.length+' complete';
    } else if(event.type==='click') {
      const action=event.target.closest('[data-action]')?.dataset.action,status=element.querySelector('.hl-tool-status');
      if(action==='reset'){stop();state.remaining=initial;timerOutput();status.textContent='Ready';}
      if(action==='toggle') {
        if(state.interval){state.remaining=Math.max(0,Math.ceil((state.deadline-Date.now())/1000));stop();timerOutput();status.textContent='Paused';return;}
        if(state.remaining<=0)state.remaining=initial;
        if(state.remaining<=0){status.textContent='Choose a duration greater than zero.';return;}
        state.deadline=Date.now()+state.remaining*1000;event.target.closest('button').textContent='Pause';status.textContent='Running';
        state.interval=setInterval(()=>{state.remaining=Math.max(0,Math.ceil((state.deadline-Date.now())/1000));timerOutput();if(state.remaining===0){stop();status.textContent='Timer complete.';}},200);
      }
    }
  };
  element.addEventListener('input',state.listener);element.addEventListener('change',state.listener);element.addEventListener('click',state.listener);
  state.stop=stop;states.set(element,state);
}
export function dispose(element) {const state=states.get(element);if(!state)return;state.stop();for(const event of ['input','change','click'])element.removeEventListener(event,state.listener);states.delete(element);}
