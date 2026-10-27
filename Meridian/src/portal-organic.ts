type OrganicKind = 'wormhole' | 'vortex';

const tau = Math.PI * 2;

function spiralPath(turns: number, startAngle: number, radius = 56): string {
  const count = 140;
  const points: string[] = [];
  for (let i = 0; i <= count; i += 1) {
    const t = i / count;
    const r = 6 + (radius - 6) * Math.pow(1 - t, 1.16);
    const angle = startAngle + t * turns * tau;
    const x = 80 + Math.cos(angle) * r;
    const y = 80 + Math.sin(angle) * r * .91;
    points.push(`${i === 0 ? 'M' : 'L'}${x.toFixed(2)},${y.toFixed(2)}`);
  }
  return points.join(' ');
}

function depthLayers(markup: string, count: number, duration: number, from = .1, to = 1): string {
  return Array.from({ length: count }, (_, i) => `
    <g class="organic-depth" data-portal-motion="depth" data-delay="${(i / count).toFixed(4)}"
      data-duration="${duration}" data-from-scale="${from}" data-to-scale="${to}">
      ${markup}
    </g>`).join('');
}

function wormhole(id: string): string {
  const mouth = 'M46 31C75 15 110 23 129 49C146 75 132 111 104 129C76 146 42 134 28 108C15 83 20 47 46 31Z';
  const rim = `<path d="${mouth}" fill="none" stroke="url(#${id}-rim)" stroke-width="1.2"/>`;
  const traces = [
    'M46 31C53 43 70 61 85 66C90 68 92 68 93 67',
    'M91 23C99 38 99 50 96 60C95 64 94 66 93 67',
    'M129 49C116 48 105 54 99 61C96 65 94 67 93 67',
    'M137 83C119 74 110 68 100 66C96 66 94 67 93 67',
    'M104 129C108 107 109 87 101 75C99 70 95 68 93 67',
    'M60 137C71 117 94 96 96 78C96 71 94 68 93 67',
    'M28 108C48 99 70 77 87 69C90 68 92 68 93 67',
    'M24 66C48 68 67 63 86 65C90 66 92 67 93 67',
  ].map((d, i) => `<path d="${d}" fill="none" stroke="${i % 3 === 0 ? `url(#${id}-violet)` : `url(#${id}-filament)`}" stroke-width="${i % 3 === 0 ? '.7' : '.55'}"/>`).join('');
  const staticDepth = [.84, .69, .55, .43, .33, .24, .17].map((scale, index) => `
    <g transform="translate(93 67) scale(${scale}) translate(-93 -67)" opacity="${.32 - index * .028}">${rim}</g>`).join('');
  const animatedRing = `<path d="${mouth}" fill="none" stroke="url(#${id}-travelling)" stroke-width="1.6"/>`;
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 160 160" class="organic-portal organic-wormhole" aria-hidden="true" focusable="false">
    <defs>
      <radialGradient id="${id}-well" cx="58%" cy="42%" r="61%">
        <stop offset="0" stop-color="#080c20"/>
        <stop offset=".3" stop-color="#10162e"/>
        <stop offset=".69" stop-color="#141d38"/>
        <stop offset="1" stop-color="#0b1524"/>
      </radialGradient>
      <linearGradient id="${id}-rim" x1="22" y1="112" x2="127" y2="33" gradientUnits="userSpaceOnUse">
        <stop stop-color="#6b66d8" stop-opacity=".45"/>
        <stop offset=".42" stop-color="#8fb1ef" stop-opacity=".7"/>
        <stop offset=".72" stop-color="#8fe8e5"/>
        <stop offset="1" stop-color="#aca0ed" stop-opacity=".7"/>
      </linearGradient>
      <linearGradient id="${id}-travelling" x1="29" y1="117" x2="124" y2="39" gradientUnits="userSpaceOnUse">
        <stop stop-color="#796ee9" stop-opacity=".38"/>
        <stop offset=".42" stop-color="#7fb6ee" stop-opacity=".88"/>
        <stop offset=".77" stop-color="#a3f5ec"/>
        <stop offset="1" stop-color="#8779dc" stop-opacity=".54"/>
      </linearGradient>
      <linearGradient id="${id}-filament" x1="80" y1="80" x2="129" y2="31" gradientUnits="userSpaceOnUse">
        <stop stop-color="#98f1e7" stop-opacity=".04"/>
        <stop offset=".68" stop-color="#85bfde" stop-opacity=".32"/>
        <stop offset="1" stop-color="#8de2e0" stop-opacity=".56"/>
      </linearGradient>
      <linearGradient id="${id}-violet" x1="80" y1="80" x2="30" y2="120" gradientUnits="userSpaceOnUse">
        <stop stop-color="#a9a2ff" stop-opacity=".04"/>
        <stop offset="1" stop-color="#a59aee" stop-opacity=".42"/>
      </linearGradient>
      <radialGradient id="${id}-core">
        <stop stop-color="#070a16"/>
        <stop offset=".68" stop-color="#0a0d1f"/>
        <stop offset="1" stop-color="#17203c" stop-opacity="0"/>
      </radialGradient>
      <clipPath id="${id}-mouth"><path d="${mouth}"/></clipPath>
    </defs>
    <path d="${mouth}" fill="url(#${id}-well)"/>
    <g clip-path="url(#${id}-mouth)">
      <g class="organic-filaments">${traces}</g>
      <g class="organic-static-depth">${staticDepth}</g>
      ${depthLayers(animatedRing, 7, 7.4, .08, 1.04)}
      <ellipse cx="93" cy="67" rx="12" ry="11" fill="url(#${id}-core)"/>
      <g class="organic-stars" fill="#c1f1ef">
        <circle cx="55" cy="43" r=".62" opacity=".7"/>
        <circle cx="116" cy="67" r=".5" opacity=".75"/>
        <circle cx="103" cy="112" r=".58" opacity=".55"/>
        <circle cx="43" cy="101" r=".45" opacity=".6"/>
      </g>
    </g>
    ${rim}
    <path d="M49 31C66 23 83 22 98 26" fill="none" stroke="#cdfcf3" stroke-width=".85" opacity=".6" stroke-linecap="round"/>
  </svg>`;
}

function vortex(id: string): string {
  const arms = Array.from({ length: 4 }, (_, index) => {
    const path = spiralPath(1.08, index * tau / 4 - .35);
    return `<path d="${path}" fill="none" stroke="url(#${id}-${index % 2 ? 'violet' : 'jade'})" stroke-width="${index % 2 ? '1.35' : '1.8'}" stroke-linecap="round"/>
      <path d="${spiralPath(1.08, index * tau / 4 - .26, 53)}" fill="none" stroke="url(#${id}-${index % 2 ? 'violet' : 'jade'})" stroke-width=".5" opacity=".45"/>`;
  }).join('');
  const aperture = 'M80 24C104 20 123 38 129 58C139 81 123 103 105 117C88 133 62 134 44 119C23 103 22 80 31 61C40 40 56 29 80 24Z';
  const depthShape = `<path d="${aperture}" fill="none" stroke="url(#${id}-flow)" stroke-width="1.2"/>`;
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 160 160" class="organic-portal organic-vortex" aria-hidden="true" focusable="false">
    <defs>
      <radialGradient id="${id}-well" cx="50%" cy="49%" r="54%">
        <stop stop-color="#070e19"/>
        <stop offset=".25" stop-color="#0b1721"/>
        <stop offset=".67" stop-color="#102c2c"/>
        <stop offset="1" stop-color="#112322" stop-opacity="0"/>
      </radialGradient>
      <linearGradient id="${id}-jade" x1="31" y1="120" x2="127" y2="36" gradientUnits="userSpaceOnUse">
        <stop stop-color="#419e87" stop-opacity=".15"/>
        <stop offset=".35" stop-color="#64d8b6" stop-opacity=".72"/>
        <stop offset=".65" stop-color="#c0f4da"/>
        <stop offset="1" stop-color="#5eafc0" stop-opacity=".16"/>
      </linearGradient>
      <linearGradient id="${id}-violet" x1="125" y1="116" x2="36" y2="31" gradientUnits="userSpaceOnUse">
        <stop stop-color="#8b78bc" stop-opacity=".16"/>
        <stop offset=".35" stop-color="#9e8bdf" stop-opacity=".65"/>
        <stop offset=".65" stop-color="#d2c5f5" stop-opacity=".87"/>
        <stop offset="1" stop-color="#645d92" stop-opacity=".12"/>
      </linearGradient>
      <linearGradient id="${id}-flow" x1="37" y1="112" x2="117" y2="41" gradientUnits="userSpaceOnUse">
        <stop stop-color="#7a6bb1" stop-opacity=".25"/>
        <stop offset=".4" stop-color="#68c9b4" stop-opacity=".6"/>
        <stop offset=".66" stop-color="#b0f7dd" stop-opacity=".9"/>
        <stop offset="1" stop-color="#a18bd8" stop-opacity=".34"/>
      </linearGradient>
      <radialGradient id="${id}-core">
        <stop stop-color="#040c15"/>
        <stop offset=".5" stop-color="#06101a"/>
        <stop offset="1" stop-color="#0e2227" stop-opacity="0"/>
      </radialGradient>
      <clipPath id="${id}-clip"><circle cx="80" cy="80" r="59"/></clipPath>
    </defs>
    <circle cx="80" cy="80" r="60" fill="url(#${id}-well)"/>
    <g clip-path="url(#${id}-clip)">
      <g class="organic-vortex-arms" data-portal-motion="rotate" data-duration="52" data-direction="-1" data-delay="0">${arms}</g>
      <g class="organic-static-depth" fill="none" stroke="url(#${id}-flow)" opacity=".18">
        <path d="${aperture}" transform="translate(80 80) scale(.72) rotate(-18) translate(-80 -80)" stroke-width=".8"/>
        <path d="${aperture}" transform="translate(80 80) scale(.46) rotate(-36) translate(-80 -80)" stroke-width=".85"/>
        <path d="${aperture}" transform="translate(80 80) scale(.26) rotate(-54) translate(-80 -80)" stroke-width=".9"/>
      </g>
      <g data-portal-motion="rotate" data-duration="41" data-direction="1" data-delay=".12">
        ${depthLayers(depthShape, 6, 8.2, .1, 1.06)}
      </g>
      <circle cx="80" cy="80" r="12" fill="url(#${id}-core)"/>
      <circle cx="80" cy="80" r="3.7" fill="#07131c" stroke="#82c7c2" stroke-width=".4" stroke-opacity=".3"/>
    </g>
    <path d="M45 39C58 28 74 23 91 26" fill="none" stroke="#9ad7cb" stroke-width=".65" stroke-linecap="round" opacity=".35"/>
    <path d="M117 117C105 128 88 133 73 130" fill="none" stroke="#b19ad7" stroke-width=".65" stroke-linecap="round" opacity=".3"/>
  </svg>`;
}

/** Vector art only. The comparison preview owns animation timing and hover behavior. */
export function organicPortal(kind: OrganicKind, prefix: string): SVGSVGElement {
  const id = prefix.replace(/[^a-zA-Z0-9_-]/g, '-');
  const markup = kind === 'wormhole' ? wormhole(id) : vortex(id);
  return new DOMParser().parseFromString(markup, 'image/svg+xml').documentElement as unknown as SVGSVGElement;
}
