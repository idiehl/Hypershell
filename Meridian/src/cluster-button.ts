/** Decoration for the collapsed Cluster's real disclosure control. */
export function clusterEmblem(name: string): HTMLElement {
  const content = document.createElement('span'); content.className = 'ms-cluster-emblem';
  content.setAttribute('aria-hidden', 'true');
  const namespace = 'http://www.w3.org/2000/svg';
  const artwork = document.createElementNS(namespace, 'svg');
  artwork.setAttribute('viewBox', '0 0 64 64'); artwork.classList.add('ms-cluster-artwork');
  const make = (tag: 'g' | 'path' | 'circle', attributes: Record<string, string>) => {
    const element = document.createElementNS(namespace, tag);
    for (const [key, value] of Object.entries(attributes)) element.setAttribute(key, value);
    return element;
  };
  const orbit = make('g', { class: 'ms-cluster-orbit' });
  orbit.append(make('circle', { cx: '32', cy: '32', r: '22', class: 'ms-cluster-orbit-track' }));
  for (let index = 0; index < 4; index++) {
    const satellite = make('g', { transform: `rotate(${index * 90} 32 32)` });
    satellite.append(make('path', { d: 'M32 6L36 10L32 14L28 10Z', class: 'ms-cluster-satellite' })); orbit.append(satellite);
  }
  const core = make('g', { class: 'ms-cluster-core' });
  core.append(make('path', { d: 'M32 17L47 32L32 47L17 32Z', class: 'ms-cluster-core-outline' }));
  core.append(make('path', { d: 'M32 24L40 32L32 40L24 32Z', class: 'ms-cluster-core-fill' }));
  core.append(make('circle', { cx: '32', cy: '32', r: '2', class: 'ms-cluster-core-light' }));
  artwork.append(orbit, core);
  const tooltip = document.createElement('span'); tooltip.className = 'ms-cluster-name'; tooltip.textContent = name;
  content.append(artwork, tooltip); return content;
}
