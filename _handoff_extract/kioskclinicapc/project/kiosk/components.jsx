/* global React */
/* ===========================================================
   ClinicaPC — Shared HUD primitives
   =========================================================== */
{
const { useEffect, useState, useRef } = React;

/* ---- Logo ---- */
function Logo({ size = 36 }) {
  // size = lockup height in px
  return (
    <img
      src="kiosk/clinicapc-logo.png"
      alt="ClínicaPC"
      style={{
        height: size,
        width: "auto",
        display: "block",
        filter: "drop-shadow(0 0 14px color-mix(in oklab, var(--cyan) 60%, transparent))",
      }}
    />
  );
}

/* ---- Corner brackets ---- */
function Brackets() {
  return <div className="brackets"><i /></div>;
}

/* ---- Floating particles ---- */
function Particles({ count = 26 }) {
  const dots = React.useMemo(() => {
    return Array.from({ length: count }).map((_, i) => {
      const left = Math.random() * 100;
      const delay = Math.random() * -22;
      const dur = 14 + Math.random() * 18;
      const size = 1 + Math.random() * 2.5;
      const hue = Math.random() > 0.5 ? "var(--cyan)" : "var(--magenta)";
      return { left, delay, dur, size, hue, key: i };
    });
  }, [count]);
  return (
    <div className="particles">
      {dots.map((d) => (
        <span key={d.key} style={{
          left: d.left + "%",
          bottom: -10,
          width: d.size, height: d.size,
          background: d.hue,
          boxShadow: `0 0 8px ${d.hue}, 0 0 18px ${d.hue}`,
          animationDuration: d.dur + "s",
          animationDelay: d.delay + "s",
        }} />
      ))}
    </div>
  );
}

/* ---- HUD top + bottom chrome ---- */
function HudChrome({ unitId, screenLabel }) {
  const [t, setT] = useState(() => new Date());
  useEffect(() => {
    const i = setInterval(() => setT(new Date()), 30000);
    return () => clearInterval(i);
  }, []);
  const hh = String(t.getHours()).padStart(2, "0");
  const mm = String(t.getMinutes()).padStart(2, "0");
  return (
    <div className="chrome">
      <div className="top">
        <div style={{ display: "flex", alignItems: "center", gap: 28 }}>
          <Logo />
          <span style={{ width: 1, height: 24, background: "var(--line-2)" }} />
          <span className="t-mono-tag" style={{ color: "var(--cyan)" }}>
            <span className="dot" />Análisis en directo
          </span>
        </div>
        <div className="right">
          <span>Unidad <strong style={{ color: "var(--t-0)" }}>#{unitId}</strong></span>
          <span>{screenLabel}</span>
          <span>{hh}:{mm}</span>
        </div>
      </div>
      <div className="bottom">
        <div className="right" style={{ marginLeft: 0 }}>
          <span>v 3.2.1 · build 7741</span>
          <span style={{ color: "var(--lime)" }}>● Sistema OK</span>
        </div>
        <div className="right">
          <span>Toca un nodo para más info</span>
          <span style={{ color: "var(--t-0)" }}>ClinicaPC · Madrid Centro</span>
        </div>
      </div>
    </div>
  );
}

/* ---- Glitch text ---- */
function Glitch({ children, as = "span", style }) {
  const Tag = as;
  return (
    <Tag className="glitch" data-text={typeof children === "string" ? children : ""} style={style}>
      {children}
    </Tag>
  );
}

/* ---- QR placeholder (decorative) ---- */
function QrPlaceholder() {
  // 9x9 deterministic pattern with finder squares
  const cells = React.useMemo(() => {
    const grid = Array.from({ length: 81 }).map((_, i) => {
      const x = i % 9, y = Math.floor(i / 9);
      const corner = (x < 3 && y < 3) || (x > 5 && y < 3) || (x < 3 && y > 5);
      if (corner) {
        // finder ring
        const inX = x === 0 || x === 2 || x === 6 || x === 8;
        const inY = y === 0 || y === 2 || y === 6 || y === 8;
        if (x === 1 && y === 1) return "off";
        if (x === 7 && y === 1) return "off";
        if (x === 1 && y === 7) return "off";
        return "on";
      }
      // deterministic noise
      const n = ((x * 31 + y * 17 + x * y) % 7);
      return n < 3 ? "on" : "off";
    });
    return grid;
  }, []);
  return (
    <div className="qr">
      {cells.map((c, i) => <i key={i} className={c === "off" ? "off" : ""} />)}
    </div>
  );
}

/* ---- Laptop hero: SVG wireframe placeholder ---- */
function LaptopHero({ size = 1 }) {
  // simple stylized open-laptop shape. Replace with real product PNG render slot.
  return (
    <svg viewBox="0 0 600 400" width={600 * size} height={400 * size} style={{ overflow: "visible" }}>
      <defs>
        <linearGradient id="screenG" x1="0" x2="0" y1="0" y2="1">
          <stop offset="0%" stopColor="#0e1a2c" />
          <stop offset="100%" stopColor="#1a0830" />
        </linearGradient>
        <linearGradient id="bezelG" x1="0" x2="0" y1="0" y2="1">
          <stop offset="0%" stopColor="#1b1f2c" />
          <stop offset="100%" stopColor="#0a0b14" />
        </linearGradient>
        <linearGradient id="baseG" x1="0" x2="0" y1="0" y2="1">
          <stop offset="0%" stopColor="#13141d" />
          <stop offset="100%" stopColor="#05060b" />
        </linearGradient>
        <radialGradient id="glowG" cx="0.5" cy="0.5" r="0.5">
          <stop offset="0%" stopColor="oklch(0.85 0.20 200 / 0.55)" />
          <stop offset="100%" stopColor="oklch(0.85 0.20 200 / 0)" />
        </radialGradient>
      </defs>

      {/* glow behind screen */}
      <ellipse cx="300" cy="170" rx="280" ry="120" fill="url(#glowG)" />

      {/* laptop screen */}
      <g transform="translate(80,30)">
        <rect x="0" y="0" width="440" height="270" rx="12" fill="url(#bezelG)" stroke="rgba(255,255,255,0.08)" />
        <rect x="14" y="14" width="412" height="232" rx="4" fill="url(#screenG)" />
        {/* on-screen HUD */}
        <g opacity="0.9">
          <text x="32" y="48" fontFamily="JetBrains Mono, monospace" fontSize="11" fill="oklch(0.85 0.20 200)" letterSpacing="3">SYSTEM SCAN · 100%</text>
          <text x="32" y="120" fontFamily="Chakra Petch, sans-serif" fontSize="42" fontWeight="700" fill="#fff" letterSpacing="2">ROG STRIX G16</text>
          <text x="32" y="146" fontFamily="JetBrains Mono, monospace" fontSize="11" fill="oklch(0.72 0.27 330)" letterSpacing="3">VERIFICADO · GRADO A+</text>
          {/* faux bars */}
          <g transform="translate(32,170)">
            {[78, 92, 88, 95].map((v, i) => (
              <g key={i} transform={`translate(${i * 92},0)`}>
                <rect width="78" height="3" fill="rgba(255,255,255,0.1)" />
                <rect width={(v / 100) * 78} height="3" fill="oklch(0.85 0.20 200)" />
                <text y="20" fontFamily="JetBrains Mono, monospace" fontSize="10" fill="rgba(255,255,255,0.5)">{["CPU","GPU","RAM","SSD"][i]}</text>
                <text y="36" fontFamily="JetBrains Mono, monospace" fontSize="10" fill="#fff">{v}%</text>
              </g>
            ))}
          </g>
          {/* webcam dot */}
          <circle cx="220" cy="6" r="2" fill="rgba(255,255,255,0.3)" />
        </g>
      </g>

      {/* hinge / base */}
      <rect x="50" y="300" width="500" height="22" rx="6" fill="url(#baseG)" stroke="rgba(255,255,255,0.08)" />
      <rect x="240" y="300" width="120" height="6" rx="3" fill="rgba(0,0,0,0.5)" />
      {/* shadow */}
      <ellipse cx="300" cy="340" rx="260" ry="14" fill="rgba(0,0,0,0.7)" />
    </svg>
  );
}

/* ---- Animated bar ---- */
function ScoreBar({ value, label, color = "var(--cyan)", delay = 0 }) {
  const [v, setV] = useState(0);
  useEffect(() => {
    const t = setTimeout(() => setV(value), 120 + delay);
    return () => clearTimeout(t);
  }, [value, delay]);
  return (
    <div style={{ width: "100%" }}>
      <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 6 }}>
        <span className="t-mono-tag">{label}</span>
        <span className="h-mono" style={{ fontSize: 14, color }}>{Math.round(v)}%</span>
      </div>
      <div style={{ position: "relative", height: 8, background: "rgba(255,255,255,0.06)" }}>
        <div style={{
          position: "absolute", inset: 0, width: `${v}%`,
          background: color,
          boxShadow: `0 0 18px ${color}`,
          transition: "width 1.6s cubic-bezier(.2,.7,.2,1)",
        }} />
        {/* tick marks */}
        <div style={{
          position: "absolute", inset: 0,
          backgroundImage: "linear-gradient(to right, rgba(255,255,255,0.0) calc(20% - 1px), rgba(255,255,255,0.18) 20%, rgba(255,255,255,0.0) calc(20% + 1px))",
          backgroundSize: "20% 100%",
          pointerEvents: "none",
        }} />
      </div>
    </div>
  );
}

/* ---- Spec icon (geometric placeholders, scoped per family) ---- */
function SpecIcon({ id, size = 28, color = "var(--cyan)" }) {
  const s = size;
  const stroke = color;
  const sw = 1.6;
  const props = { width: s, height: s, viewBox: "0 0 32 32", fill: "none", stroke, strokeWidth: sw, strokeLinecap: "round", strokeLinejoin: "round" };
  switch (id) {
    case "cpu": return (
      <svg {...props}>
        <rect x="9" y="9" width="14" height="14" rx="1" />
        <rect x="13" y="13" width="6" height="6" />
        {[0,1,2,3].map(i => <line key={"t"+i} x1={12+i*3} y1="9" x2={12+i*3} y2="6" />)}
        {[0,1,2,3].map(i => <line key={"b"+i} x1={12+i*3} y1="23" x2={12+i*3} y2="26" />)}
        {[0,1,2,3].map(i => <line key={"l"+i} y1={12+i*3} x1="9" y2={12+i*3} x2="6" />)}
        {[0,1,2,3].map(i => <line key={"r"+i} y1={12+i*3} x1="23" y2={12+i*3} x2="26" />)}
      </svg>
    );
    case "gpu": return (
      <svg {...props}>
        <rect x="4" y="11" width="24" height="11" rx="1" />
        <circle cx="11" cy="16.5" r="2.6" />
        <circle cx="21" cy="16.5" r="2.6" />
        <line x1="4" y1="22" x2="2" y2="25" />
        <line x1="28" y1="22" x2="30" y2="25" />
      </svg>
    );
    case "ram": return (
      <svg {...props}>
        <rect x="4" y="10" width="24" height="10" rx="1" />
        <line x1="9" y1="10" x2="9" y2="20" />
        <line x1="14" y1="10" x2="14" y2="20" />
        <line x1="19" y1="10" x2="19" y2="20" />
        <line x1="24" y1="10" x2="24" y2="20" />
        <line x1="6" y1="22" x2="6" y2="25" />
        <line x1="26" y1="22" x2="26" y2="25" />
      </svg>
    );
    case "storage": return (
      <svg {...props}>
        <rect x="5" y="6" width="22" height="6" rx="1" />
        <rect x="5" y="14" width="22" height="6" rx="1" />
        <rect x="5" y="22" width="22" height="6" rx="1" />
        <circle cx="23" cy="9"  r="0.6" fill={stroke} />
        <circle cx="23" cy="17" r="0.6" fill={stroke} />
        <circle cx="23" cy="25" r="0.6" fill={stroke} />
      </svg>
    );
    case "screen": return (
      <svg {...props}>
        <rect x="3" y="6" width="26" height="16" rx="1" />
        <line x1="11" y1="26" x2="21" y2="26" />
        <line x1="16" y1="22" x2="16" y2="26" />
      </svg>
    );
    case "battery": return (
      <svg {...props}>
        <rect x="3" y="10" width="24" height="12" rx="2" />
        <rect x="27" y="13" width="2" height="6" />
        <rect x="6" y="13" width="14" height="6" fill={stroke} stroke="none" />
      </svg>
    );
    case "wifi": return (
      <svg {...props}>
        <path d="M5 13a17 17 0 0 1 22 0" />
        <path d="M9 17a11 11 0 0 1 14 0" />
        <path d="M13 21a5 5 0 0 1 6 0" />
        <circle cx="16" cy="25" r="1" fill={stroke} />
      </svg>
    );
    case "camera": return (
      <svg {...props}>
        <rect x="3" y="8" width="26" height="16" rx="2" />
        <circle cx="16" cy="16" r="4.5" />
        <circle cx="16" cy="16" r="1.5" fill={stroke} />
        <rect x="20" y="5" width="6" height="3" rx="1" />
      </svg>
    );
    case "ports": return (
      <svg {...props}>
        <rect x="3" y="13" width="9" height="6" rx="1" />
        <rect x="14" y="11" width="6" height="10" rx="1" />
        <rect x="22" y="14" width="7" height="4" rx="2" />
      </svg>
    );
    case "os": return (
      <svg {...props}>
        <rect x="4" y="5" width="11" height="10" />
        <rect x="17" y="5" width="11" height="10" />
        <rect x="4" y="17" width="11" height="10" />
        <rect x="17" y="17" width="11" height="10" />
      </svg>
    );
    default: return <svg {...props}><circle cx="16" cy="16" r="10" /></svg>;
  }
}

Object.assign(window, { Logo, Brackets, Particles, HudChrome, Glitch, QrPlaceholder, LaptopHero, ScoreBar, SpecIcon });
}
