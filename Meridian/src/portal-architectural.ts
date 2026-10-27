type ArchitecturalKind = 'doorway' | 'passage';

/**
 * Native vector studies for the portal comparison. The comparison runtime owns
 * animation clocks, speed changes, focus behavior, and reduced-motion handling.
 * Each depth group describes one trip from the vanishing point to the viewer.
 */
export function architecturalPortal(kind: ArchitecturalKind, prefix: string): SVGSVGElement {
  if (!/^[A-Za-z][A-Za-z0-9_-]*$/.test(prefix)) throw new Error('A safe SVG ID prefix is required.');
  const markup = kind === 'doorway' ? doorway(prefix) : passage(prefix);
  return new DOMParser().parseFromString(markup, 'image/svg+xml').documentElement as unknown as SVGSVGElement;
}

function depthLayers(kind: ArchitecturalKind, p: string): string {
  return Array.from({ length: 8 }, (_, index) => {
    const phase = (index + 0.45) / 8;
    // A static perspective stack remains useful when motion is unavailable.
    const scale = 0.09 + phase * 0.98;
    const opacity = 0.14 + Math.sin(phase * Math.PI) * 0.74;
    const art = kind === 'doorway'
      ? `<path class="arch-depth-frame" d="M43 140V25a5 5 0 0 1 5-5h64a5 5 0 0 1 5 5v115" fill="none" stroke="url(#${p}-frame)" stroke-width="1.65"/>
         <path d="M47 136V27a3 3 0 0 1 3-3h60a3 3 0 0 1 3 3v109" fill="none" stroke="#9ddfc9" stroke-opacity=".23" stroke-width=".65"/>
         <path d="M43 140h74" fill="none" stroke="url(#${p}-threshold)" stroke-width="1.3"/>
         <path d="M43 115v13m74-13v13" stroke="#e8fff3" stroke-opacity=".75" stroke-width="2.2"/>`
      : `<path class="arch-depth-frame" d="M50 23h60l28 28v58l-28 28H50l-28-28V51Z" fill="none" stroke="url(#${p}-frame)" stroke-width="1.55"/>
         <path d="M52 27h56l26 26v54l-26 26H52l-26-26V53Z" fill="none" stroke="#ffd599" stroke-opacity=".19" stroke-width=".65"/>
         <path d="m27 47 19-19m68 0 19 19M27 113l19 19m68 0 19-19" stroke="#fff1d5" stroke-opacity=".82" stroke-width="2.3"/>
         <path d="M50 137h60" stroke="#d89b54" stroke-opacity=".42" stroke-width="1.8"/>`;
    return `<g class="architectural-depth architectural-depth--${kind}" data-portal-motion="depth" data-delay="${phase.toFixed(5)}" data-duration="${kind === 'doorway' ? 8 : 9}" data-from-scale="0.09" data-to-scale="1.08" style="transform:scale(${scale.toFixed(4)});opacity:${opacity.toFixed(4)}">${art}</g>`;
  }).join('');
}

function doorway(p: string): string {
  return `<svg xmlns="http://www.w3.org/2000/svg" class="portal-art portal-art--doorway" viewBox="0 0 160 160" aria-hidden="true" focusable="false">
    <defs>
      <radialGradient id="${p}-void" cx="50%" cy="47%" r="69%">
        <stop offset="0" stop-color="#08211f"/>
        <stop offset=".42" stop-color="#0d2c28"/>
        <stop offset="1" stop-color="#09201d" stop-opacity=".35"/>
      </radialGradient>
      <linearGradient id="${p}-frame" x1="43" y1="20" x2="117" y2="140" gradientUnits="userSpaceOnUse">
        <stop stop-color="#f2fff5"/><stop offset=".32" stop-color="#c7e9d9"/>
        <stop offset=".7" stop-color="#6bc5ad"/><stop offset="1" stop-color="#ddf8df"/>
      </linearGradient>
      <linearGradient id="${p}-threshold" x1="43" y1="140" x2="117" y2="140" gradientUnits="userSpaceOnUse">
        <stop stop-color="#8bd0b6" stop-opacity=".12"/><stop offset=".5" stop-color="#e7ffdb"/>
        <stop offset="1" stop-color="#8bd0b6" stop-opacity=".12"/>
      </linearGradient>
      <radialGradient id="${p}-halo">
        <stop stop-color="#c5ffe1" stop-opacity=".48"/>
        <stop offset=".3" stop-color="#80e6bb" stop-opacity=".13"/>
        <stop offset="1" stop-color="#65d5ae" stop-opacity="0"/>
      </radialGradient>
      <clipPath id="${p}-aperture"><path d="M39 143V24a8 8 0 0 1 8-8h66a8 8 0 0 1 8 8v119Z"/></clipPath>
    </defs>
    <ellipse cx="80" cy="81" rx="43" ry="62" fill="url(#${p}-halo)" opacity=".45"/>
    <path d="M39 143V24a8 8 0 0 1 8-8h66a8 8 0 0 1 8 8v119Z" fill="url(#${p}-void)"/>
    <g clip-path="url(#${p}-aperture)">
      <path d="M43 20 80 80 117 20M43 140 80 80 117 140" fill="none" stroke="#9adbbf" stroke-opacity=".14" stroke-width=".65"/>
      <path d="M43 20h74L80 80Z" fill="#cfffe5" fill-opacity=".018"/>
      <path d="m43 140 37-60 37 60Z" fill="#b7f7d7" fill-opacity=".035"/>
      <ellipse cx="80" cy="80" rx="16" ry="25" fill="url(#${p}-halo)"/>
      ${depthLayers('doorway', p)}
      <path d="M77.5 84v-8h5v8" fill="#ceffde" fill-opacity=".28" stroke="#e1ffed" stroke-opacity=".75" stroke-width=".7"/>
      <path d="M77.5 84h5" stroke="#f4ffe6" stroke-opacity=".95" stroke-width=".7"/>
    </g>
    <path d="M39 143V24a8 8 0 0 1 8-8h66a8 8 0 0 1 8 8v119" fill="none" stroke="#91bfad" stroke-opacity=".3" stroke-width=".7"/>
    <path d="M42 26v-2a5 5 0 0 1 5-5h17m32 0h17a5 5 0 0 1 5 5v2" fill="none" stroke="#edfff2" stroke-opacity=".8" stroke-width="1.15"/>
    <path d="M36 144h88" stroke="url(#${p}-threshold)" stroke-width="1"/>
  </svg>`;
}

function passage(p: string): string {
  return `<svg xmlns="http://www.w3.org/2000/svg" class="portal-art portal-art--passage" viewBox="0 0 160 160" aria-hidden="true" focusable="false">
    <defs>
      <radialGradient id="${p}-void" cx="50%" cy="50%" r="62%">
        <stop stop-color="#46352b"/><stop offset=".22" stop-color="#1c2927"/>
        <stop offset=".65" stop-color="#15231f"/><stop offset="1" stop-color="#111d19" stop-opacity=".3"/>
      </radialGradient>
      <radialGradient id="${p}-halo">
        <stop stop-color="#fff2ce" stop-opacity=".8"/>
        <stop offset=".18" stop-color="#f6cc82" stop-opacity=".35"/>
        <stop offset=".52" stop-color="#d29b61" stop-opacity=".07"/>
        <stop offset="1" stop-color="#d29b61" stop-opacity="0"/>
      </radialGradient>
      <linearGradient id="${p}-frame" x1="32" y1="27" x2="128" y2="134" gradientUnits="userSpaceOnUse">
        <stop stop-color="#f9e5bd"/><stop offset=".3" stop-color="#d9b57f"/>
        <stop offset=".53" stop-color="#fff1d0"/><stop offset=".78" stop-color="#bc9866"/>
        <stop offset="1" stop-color="#dfcfa8"/>
      </linearGradient>
      <clipPath id="${p}-aperture"><path d="M48 19h64l30 30v62l-30 30H48l-30-30V49Z"/></clipPath>
    </defs>
    <path d="M48 19h64l30 30v62l-30 30H48l-30-30V49Z" fill="url(#${p}-void)"/>
    <g clip-path="url(#${p}-aperture)">
      <path d="m48 19 32 61 32-61M18 49l62 31 62-31M18 111l62-31 62 31M48 141l32-61 32 61" fill="none" stroke="#e1c59b" stroke-opacity=".12" stroke-width=".65"/>
      <path d="M48 19h64L80 80ZM18 111l30 30 32-61ZM142 111l-30 30-32-61Z" fill="#efc18c" fill-opacity=".025"/>
      <circle cx="80" cy="80" r="42" fill="url(#${p}-halo)" opacity=".58"/>
      ${depthLayers('passage', p)}
      <path d="M77 74h6l3 3v6l-3 3h-6l-3-3v-6Z" fill="#f9e6bc" fill-opacity=".12" stroke="#fff1d3" stroke-opacity=".7" stroke-width=".65"/>
      <circle cx="80" cy="80" r="1.1" fill="#fff6db"/>
    </g>
    <path d="M48 19h64l30 30v62l-30 30H48l-30-30V49Z" fill="none" stroke="url(#${p}-frame)" stroke-opacity=".3" stroke-width=".7"/>
    <path d="m23 46 23-23m68 0 23 23M23 114l23 23m68 0 23-23" fill="none" stroke="#f8e2ba" stroke-opacity=".62" stroke-width="1.15"/>
  </svg>`;
}
