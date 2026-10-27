const states = new WeakMap();
export function mount(element, start) {
  dispose(element);
  const state = {value:start,start};
  state.listener = event => { const button=event.target.closest('[data-action]'); if(!button)return; state.value += button.dataset.action==='add'?1:-1; element.querySelector('output').textContent=state.value; };
  element.addEventListener('click',state.listener); states.set(element,state); element.querySelector('output').textContent=start;
}
export function update(element,start) { const state=states.get(element); if(state && state.start!==start){state.start=start;state.value=start;element.querySelector('output').textContent=start;} }
export function dispose(element) { const state=states.get(element); if(state) element.removeEventListener('click',state.listener); states.delete(element); }
