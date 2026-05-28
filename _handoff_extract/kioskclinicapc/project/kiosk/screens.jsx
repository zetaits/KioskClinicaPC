/* global React, KIOSK_DATA, Logo, Brackets, Particles, HudChrome, Glitch, QrPlaceholder, LaptopHero, ScoreBar, SpecIcon */
/* ===========================================================
   ClinicaPC — Screens
   =========================================================== */
{
const { useEffect, useState, useRef, useMemo } = React;

/* -----------------------------------------------------------
   ATTRACT (idle loop with rotating slides)
   ----------------------------------------------------------- */
function AttractScreen({ onStart }) {
  const slides = window.KIOSK_DATA.attract;
  const [idx, setIdx] = useState(0);
  useEffect(() => {
    const i = setInterval(() => setIdx((n) => (n + 1) % slides.length), 5200);
    return () => clearInterval(i);
  }, [slides.length]);
  const s = slides[idx];

  return (
    <div className="screen screen-enter" onClick={onStart} style={{ cursor: "pointer" }}>
      <div className="bg-mesh" />
      <div className="bg-grid" />
      <Particles count={32} />
      <div className="bg-scanlines" />
      <div className="bg-noise" />
      <div className="bg-vignette" />

      {/* large wireframe orb behind */}
      <svg style={{ position: "absolute", left: "50%", top: "50%", transform: "translate(-50%,-55%)", opacity: 0.35, zIndex: 4 }}
           width="1200" height="1200" viewBox="0 0 100 100" fill="none">
        <g stroke="var(--cyan)" strokeWidth="0.1">
          {Array.from({length:18}).map((_,i)=>(
            <ellipse key={i} cx="50" cy="50" rx="48" ry={4 + i*2.6} />
          ))}
        </g>
        <g stroke="var(--magenta)" strokeWidth="0.08" opacity="0.6">
          {Array.from({length:14}).map((_,i)=>(
            <ellipse key={i} cx="50" cy="50" rx={4 + i*3.2} ry="48" />
          ))}
        </g>
        <circle cx="50" cy="50" r="48" stroke="var(--cyan)" strokeWidth="0.15" />
      </svg>

      <Brackets />

      {/* top bar lite */}
      <div className="chrome">
        <div className="top">
          <Logo />
          <span className="t-mono-tag" style={{ color: "var(--cyan)" }}>
            <span className="dot" />Esperando análisis · Idle 00:42
          </span>
        </div>
        <div className="bottom">
          <span className="t-mono-tag">Madrid · Calle Goya 12</span>
          <span className="t-mono-tag">Asistencia · Cambio · Reparación · Reacondicionado</span>
        </div>
      </div>

      {/* center content */}
      <div style={{
        position: "absolute", inset: 0, display: "grid", placeItems: "center", zIndex: 12, padding: "0 160px"
      }}>
        <div key={idx} style={{ textAlign: "center", maxWidth: 1280, animation: "screenIn .7s cubic-bezier(.2,.7,.2,1) both" }}>
          <div className="t-mono-tag" style={{ color: "var(--cyan)", marginBottom: 28 }}>
            <span style={{ display: "inline-block", width: 28, height: 1, background: "var(--cyan)", verticalAlign: "middle", marginRight: 14 }} />
            {s.eyebrow}
            <span style={{ display: "inline-block", width: 28, height: 1, background: "var(--cyan)", verticalAlign: "middle", marginLeft: 14 }} />
          </div>
          <h1 className="h-display" style={{ fontSize: 168, marginBottom: 12 }}>
            {s.title}
          </h1>
          <h1 className="h-display" style={{ fontSize: 168, color: "var(--cyan)",
            textShadow: "0 0 32px color-mix(in oklab, var(--cyan) 70%, transparent)" }}>
            <Glitch>{s.titleAccent}</Glitch>
          </h1>
          <p style={{ marginTop: 38, fontSize: 22, color: "var(--t-1)", letterSpacing: "0.04em" }}>
            {s.sub}
          </p>

          <div style={{ marginTop: 80, display: "flex", flexDirection: "column", alignItems: "center", gap: 22 }}>
            <button className="cta-pulse" onClick={onStart}>
              <span style={{ width: 12, height: 12, background: "var(--cyan)", boxShadow: "0 0 12px var(--cyan)", borderRadius: "50%" }} />
              Toca para analizar este equipo
            </button>
            <span className="t-mono-tag">o espera · arranca solo en 3 segundos</span>
          </div>
        </div>
      </div>

      {/* slide dots */}
      <div style={{ position: "absolute", left: "50%", bottom: 110, transform: "translateX(-50%)", display: "flex", gap: 10, zIndex: 14 }}>
        {slides.map((_, i) => (
          <span key={i} style={{
            width: i === idx ? 32 : 8, height: 4,
            background: i === idx ? "var(--cyan)" : "rgba(255,255,255,0.2)",
            boxShadow: i === idx ? "0 0 10px var(--cyan)" : "none",
            transition: "all .4s ease",
          }} />
        ))}
      </div>
    </div>
  );
}

/* -----------------------------------------------------------
   SCAN (radar sweep transition)
   ----------------------------------------------------------- */
function ScanScreen({ onDone }) {
  const [pct, setPct] = useState(0);
  const [log, setLog] = useState([]);
  const logLines = [
    "INIT · ClinicaPC diagnostic v3.2.1",
    "PROBE bios · OK",
    "DETECT chassis · ASUS ROG STRIX G16",
    "READ cpu  · Intel Core i7-13650HX",
    "READ gpu  · NVIDIA RTX 4060 8GB",
    "READ ram  · 32 GB DDR5 @ 4800",
    "READ ssd  · 1 TB NVMe PCIe 4.0",
    "READ disp · 16″ QHD 240 Hz",
    "READ batt · 90 Wh · ciclo 47/300",
    "READ wifi · WiFi 6E · BT 5.3",
    "READ cam  · 1080p · OK",
    "VERIFY    · grado A+",
    "COMPLETE  · 100%",
  ];

  useEffect(() => {
    let frame = 0;
    const total = 2400;
    const t0 = performance.now();
    const tick = (t) => {
      const k = Math.min(1, (t - t0) / total);
      setPct(Math.round(k * 100));
      if (k < 1) requestAnimationFrame(tick);
      else setTimeout(onDone, 220);
    };
    requestAnimationFrame(tick);

    const li = setInterval(() => {
      setLog((l) => {
        if (l.length >= logLines.length) { clearInterval(li); return l; }
        return [...l, logLines[l.length]];
      });
    }, 170);
    return () => clearInterval(li);
  }, []);

  return (
    <div className="screen screen-enter">
      <div className="bg-mesh" />
      <div className="bg-grid fine" />
      <div className="bg-scanlines" />
      <div className="bg-vignette" />
      <Brackets />

      <div className="chrome">
        <div className="top">
          <Logo />
          <span className="t-mono-tag" style={{ color: "var(--cyan)" }}>
            <span className="dot" />Escaneando…
          </span>
        </div>
      </div>

      {/* center: radar */}
      <div style={{ position: "absolute", inset: 0, display: "grid", placeItems: "center" }}>
        <div style={{ position: "relative", width: 720, height: 720 }}>
          {/* concentric */}
          {[100, 78, 56, 34, 14].map((p) => (
            <div key={p} style={{
              position: "absolute", inset: 0, margin: "auto",
              width: `${p}%`, height: `${p}%`,
              border: "1px solid color-mix(in oklab, var(--cyan) 30%, transparent)",
              borderRadius: "50%",
            }} />
          ))}
          {/* crosshair */}
          <div style={{ position: "absolute", left: 0, right: 0, top: "50%", height: 1, background: "color-mix(in oklab, var(--cyan) 30%, transparent)" }} />
          <div style={{ position: "absolute", top: 0, bottom: 0, left: "50%", width: 1, background: "color-mix(in oklab, var(--cyan) 30%, transparent)" }} />
          {/* sweep */}
          <div style={{
            position: "absolute", inset: 0,
            background: "conic-gradient(from 0deg, color-mix(in oklab, var(--cyan) 55%, transparent) 0deg, transparent 70deg)",
            borderRadius: "50%",
            animation: "spin 1.6s linear infinite",
            mixBlendMode: "screen",
          }} />
          {/* center dot */}
          <div style={{
            position: "absolute", left: "50%", top: "50%", width: 18, height: 18,
            transform: "translate(-50%,-50%)",
            background: "var(--cyan)", borderRadius: "50%",
            boxShadow: "0 0 24px var(--cyan), 0 0 60px var(--cyan)",
          }} />
          <style>{`@keyframes spin { to { transform: rotate(360deg); } }`}</style>

          {/* pinged blips */}
          {[[30,40],[68,28],[20,72],[78,80],[52,18]].map(([x,y],i)=>(
            <span key={i} style={{
              position: "absolute", left: `${x}%`, top: `${y}%`,
              width: 8, height: 8, borderRadius: "50%",
              background: "var(--magenta)",
              boxShadow: "0 0 12px var(--magenta)",
              animation: `nodePop .4s ${i*0.15+0.3}s both`,
            }} />
          ))}
        </div>
      </div>

      {/* bottom: log */}
      <div style={{
        position: "absolute", left: 120, bottom: 120, width: 720,
        zIndex: 20,
      }}>
        <div className="t-mono-tag" style={{ color: "var(--cyan)", marginBottom: 16 }}>// CLINICAPC :: scan log</div>
        <div style={{ fontFamily: "var(--f-mono)", fontSize: 14, lineHeight: 1.8, color: "var(--t-1)" }}>
          {log.map((l, i) => (
            <div key={i} style={{ opacity: 0, animation: "screenIn .3s forwards" }}>
              <span style={{ color: "var(--t-3)" }}>{String(i+1).padStart(2,"0")}</span>
              <span style={{ marginLeft: 14, color: l.startsWith("COMPLETE") ? "var(--lime)" : "var(--t-1)" }}>{l}</span>
            </div>
          ))}
        </div>
      </div>

      {/* right: % counter */}
      <div style={{ position: "absolute", right: 120, bottom: 120, textAlign: "right" }}>
        <div className="t-mono-tag" style={{ color: "var(--cyan)", marginBottom: 12 }}>Progreso</div>
        <div className="h-display" style={{ fontSize: 220, color: "var(--t-0)", lineHeight: 0.9 }}>
          {String(pct).padStart(3, "0")}<span style={{ color: "var(--cyan)" }}>%</span>
        </div>
      </div>
    </div>
  );
}

/* -----------------------------------------------------------
   MAIN HUD (laptop + orbital spec nodes)
   ----------------------------------------------------------- */
function MainHudScreen({ onSpec, onPrice, autoCycle }) {
  const data = window.KIOSK_DATA;
  const specs = data.specs.filter(s => s.present);

  // auto-cycle highlight
  const [hi, setHi] = useState(0);
  useEffect(() => {
    if (!autoCycle) return;
    const i = setInterval(() => setHi((n) => (n + 1) % specs.length), 3000);
    return () => clearInterval(i);
  }, [autoCycle, specs.length]);

  return (
    <div className="screen screen-enter">
      <div className="bg-mesh" />
      <div className="bg-grid" />
      <Particles count={22} />
      <div className="bg-scanlines" />
      <div className="bg-noise" />
      <div className="bg-vignette" />
      <Brackets />
      <HudChrome unitId={data.unit.id} screenLabel="Resumen del equipo" />

      {/* LEFT: identity block */}
      <div style={{ position: "absolute", left: 120, top: 180, width: 560, zIndex: 12 }}>
        <div className="t-mono-tag" style={{ color: "var(--cyan)", marginBottom: 22 }}>
          <span style={{ display: "inline-block", width: 28, height: 1, background: "var(--cyan)", verticalAlign: "middle", marginRight: 14 }} />
          Equipo detectado
        </div>
        <h2 className="h-display" style={{ fontSize: 76, lineHeight: 1, marginBottom: 14 }}>
          {data.unit.name.split(" ").slice(0, 2).join(" ")}
        </h2>
        <h2 className="h-display" style={{ fontSize: 76, lineHeight: 1, color: "var(--cyan)",
          textShadow: "0 0 24px color-mix(in oklab, var(--cyan) 60%, transparent)" }}>
          {data.unit.name.split(" ").slice(2).join(" ")}
        </h2>
        <div style={{ marginTop: 24, color: "var(--t-1)", fontSize: 18, letterSpacing: "0.04em" }}>
          {data.unit.family} · <span style={{ color: "var(--t-2)" }}>SKU {data.unit.sku}</span>
        </div>

        <div style={{ marginTop: 36, display: "flex", gap: 14, flexWrap: "wrap" }}>
          <span className="t-mono-tag" style={{
            padding: "10px 14px", border: "1px solid var(--line-2)",
            color: "var(--lime)", display: "inline-flex", alignItems: "center", gap: 10,
          }}>
            <span className="dot" style={{ background: "var(--lime)", boxShadow: "0 0 8px var(--lime)" }} />
            {data.unit.state}
          </span>
          <span className="t-mono-tag" style={{
            padding: "10px 14px", border: "1px solid var(--line-2)", color: "var(--t-1)",
          }}>
            {data.unit.warranty}
          </span>
        </div>

        {/* quick stats row */}
        <div style={{ marginTop: 56, display: "grid", gridTemplateColumns: "1fr 1fr", gap: 18 }}>
          {[
            { k: "Puntuación global", v: "92", suffix: "/100", color: "var(--cyan)" },
            { k: "Gen. componentes", v: "2023", suffix: "", color: "var(--t-0)" },
            { k: "Ciclos batería", v: "47", suffix: "/300", color: "var(--lime)" },
            { k: "Pruebas pasadas", v: "38", suffix: "/38", color: "var(--lime)" },
          ].map((s, i) => (
            <div key={i} className="panel" style={{ padding: "18px 22px" }}>
              <div className="t-mono-tag">{s.k}</div>
              <div className="h-display" style={{ fontSize: 38, color: s.color, marginTop: 4 }}>
                {s.v}<span style={{ color: "var(--t-2)", fontSize: 16, marginLeft: 6 }}>{s.suffix}</span>
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* CENTER: laptop + orbital nodes */}
      <div style={{
        position: "absolute", left: "50%", top: "50%",
        transform: "translate(-50%, -50%)", width: 900, height: 900, zIndex: 11,
      }}>
        {/* orbit rings */}
        <svg width="900" height="900" viewBox="0 0 100 100" fill="none"
             style={{ position: "absolute", inset: 0 }}>
          <g stroke="color-mix(in oklab, var(--cyan) 24%, transparent)" strokeWidth="0.08" strokeDasharray="0.6 0.6">
            <circle cx="50" cy="50" r="44" />
            <circle cx="50" cy="50" r="38" />
            <circle cx="50" cy="50" r="32" />
          </g>
          <g stroke="color-mix(in oklab, var(--cyan) 35%, transparent)" strokeWidth="0.12">
            <circle cx="50" cy="50" r="44" strokeDasharray="0 2 30 4 14" />
          </g>
        </svg>

        {/* laptop in center */}
        <div style={{ position: "absolute", left: "50%", top: "50%", transform: "translate(-50%,-50%)" }}>
          <LaptopHero size={0.85} />
        </div>

        {/* nodes around */}
        {specs.map((spec, i) => {
          // place at radius along ring
          const angle = (spec.angle ?? (i * 36)) * (Math.PI / 180);
          const r = 360; // px from center
          const cx = 450 + Math.cos(angle) * r;
          const cy = 450 + Math.sin(angle) * r;
          const isHi = i === hi;
          return (
            <button
              key={spec.id}
              onClick={() => onSpec(i)}
              className="spec-node"
              style={{
                position: "absolute",
                left: cx, top: cy,
                transform: "translate(-50%,-50%)",
                animationDelay: `${0.05 * i}s`,
                width: 168,
                background: isHi
                  ? "color-mix(in oklab, var(--cyan) 14%, var(--bg-2))"
                  : "color-mix(in oklab, var(--bg-2) 80%, transparent)",
                border: `1px solid ${isHi ? "var(--cyan)" : "var(--line-2)"}`,
                color: "var(--t-0)",
                cursor: "pointer",
                padding: 14,
                textAlign: "left",
                backdropFilter: "blur(10px)",
                boxShadow: isHi
                  ? "0 0 0 4px color-mix(in oklab, var(--cyan) 18%, transparent), 0 0 24px color-mix(in oklab, var(--cyan) 40%, transparent)"
                  : "none",
                transition: "all .35s ease",
                clipPath: "polygon(10px 0, 100% 0, 100% calc(100% - 10px), calc(100% - 10px) 100%, 0 100%, 0 10px)",
              }}
            >
              <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 6 }}>
                <SpecIcon id={spec.id} size={22} color={isHi ? "var(--cyan)" : "var(--t-1)"} />
                <span className="t-mono-tag" style={{ fontSize: 10, color: isHi ? "var(--cyan)" : "var(--t-3)" }}>
                  {String(i + 1).padStart(2, "0")}
                </span>
              </div>
              <div className="t-mono-tag" style={{ fontSize: 10, color: "var(--t-2)" }}>{spec.label}</div>
              <div className="h-display" style={{ fontSize: 20, marginTop: 2, lineHeight: 1.05 }}>{spec.value}</div>
              {/* connector line back to center */}
              <span style={{
                position: "absolute",
                left: cx > 450 ? -28 : "auto",
                right: cx < 450 ? -28 : "auto",
                top: "50%",
                width: 28, height: 1,
                background: isHi ? "var(--cyan)" : "var(--line-2)",
                boxShadow: isHi ? "0 0 8px var(--cyan)" : "none",
                pointerEvents: "none",
              }} />
            </button>
          );
        })}
      </div>

      {/* RIGHT: price strip */}
      <div style={{
        position: "absolute", right: 120, top: 180, width: 380, zIndex: 12,
      }}>
        <div className="panel" style={{ padding: "26px 28px" }}>
          <div className="t-mono-tag" style={{ color: "var(--magenta)" }}>// Precio en tienda</div>
          <div style={{ marginTop: 12, display: "flex", alignItems: "baseline", gap: 12 }}>
            <span className="h-display" style={{ fontSize: 88, color: "var(--t-0)", lineHeight: 1 }}>
              {data.unit.price.amount.toLocaleString("es")}
            </span>
            <span className="h-display" style={{ fontSize: 36, color: "var(--cyan)" }}>{data.unit.price.currency}</span>
          </div>
          <div style={{ marginTop: 4, color: "var(--t-2)", fontSize: 14 }}>
            <s style={{ marginRight: 12 }}>{data.unit.price.strike.toLocaleString("es")}{data.unit.price.currency}</s>
            <span style={{ color: "var(--magenta)", fontFamily: "var(--f-mono)", letterSpacing: "0.18em" }}>
              -{Math.round((1 - data.unit.price.amount / data.unit.price.strike) * 100)}%
            </span>
          </div>
          <div style={{ marginTop: 22, padding: "12px 14px", background: "rgba(255,255,255,0.04)", border: "1px solid var(--line-1)" }}>
            <div className="t-mono-tag" style={{ fontSize: 11 }}>O en cuotas</div>
            <div style={{ fontFamily: "var(--f-display)", fontSize: 22, marginTop: 2 }}>
              4 × {(data.unit.price.amount / 4).toFixed(0)}{data.unit.price.currency}
              <span style={{ fontFamily: "var(--f-mono)", fontSize: 12, color: "var(--t-2)", marginLeft: 8, letterSpacing: "0.16em" }}>SIN INTERESES</span>
            </div>
          </div>
          <button onClick={onPrice} style={{
            marginTop: 22, width: "100%",
            padding: "16px", background: "var(--cyan)", color: "#02121a",
            border: 0, cursor: "pointer",
            fontFamily: "var(--f-display)", fontWeight: 700, letterSpacing: "0.18em",
            textTransform: "uppercase", fontSize: 16,
            clipPath: "polygon(12px 0, 100% 0, 100% calc(100% - 12px), calc(100% - 12px) 100%, 0 100%, 0 12px)",
            boxShadow: "0 0 24px color-mix(in oklab, var(--cyan) 60%, transparent)",
          }}>
            Llévatelo · habla con un asesor
          </button>
          <div style={{ marginTop: 18, display: "flex", alignItems: "center", gap: 16 }}>
            <QrPlaceholder />
            <div>
              <div className="t-mono-tag" style={{ marginBottom: 6 }}>Escanea</div>
              <div style={{ fontSize: 14, color: "var(--t-1)", lineHeight: 1.4 }}>
                Llévate la ficha al móvil para enseñársela a quien te asesore.
              </div>
            </div>
          </div>
        </div>

        {/* live indicator */}
        <div style={{ marginTop: 22, display: "flex", justifyContent: "space-between", color: "var(--t-2)", fontFamily: "var(--f-mono)", fontSize: 12, letterSpacing: "0.18em", textTransform: "uppercase" }}>
          <span>Stock <span style={{ color: "var(--lime)" }}>● 3 unidades</span></span>
          <span>Reservado: <span style={{ color: "var(--t-0)" }}>1 hoy</span></span>
        </div>
      </div>
    </div>
  );
}

/* -----------------------------------------------------------
   DETAIL (single spec deep-dive)
   ----------------------------------------------------------- */
function DetailScreen({ index, onClose, onIndex }) {
  const data = window.KIOSK_DATA;
  const specs = data.specs.filter((s) => s.present);
  const spec = specs[index];
  const total = specs.length;
  const next = () => onIndex((index + 1) % total);
  const prev = () => onIndex((index - 1 + total) % total);

  // accent rotates per family
  const accents = {
    cpu: "var(--cyan)",
    gpu: "var(--magenta)",
    ram: "var(--cyan)",
    storage: "var(--lime)",
    screen: "var(--cyan)",
    battery: "var(--lime)",
    wifi: "var(--magenta)",
    camera: "var(--cyan)",
    ports: "var(--amber)",
    os: "var(--cyan)",
  };
  const accent = accents[spec.id] || "var(--cyan)";

  return (
    <div className="screen screen-enter">
      <div className="bg-mesh" />
      <div className="bg-grid" />
      <Particles count={18} />
      <div className="bg-scanlines" />
      <div className="bg-noise" />
      <div className="bg-vignette" />
      <Brackets />
      <HudChrome unitId={data.unit.id} screenLabel={`Detalle · ${spec.label}`} />

      {/* huge faded background letter (spec family) */}
      <div className="h-display" style={{
        position: "absolute",
        right: -120, top: 80, zIndex: 2,
        fontSize: 720,
        color: "rgba(255,255,255,0.03)",
        letterSpacing: "0.02em",
        lineHeight: 0.8,
        pointerEvents: "none",
      }}>
        {spec.family}
      </div>

      {/* LEFT: big value + label */}
      <div style={{ position: "absolute", left: 120, top: 200, width: 900, zIndex: 12 }}>
        <div style={{ display: "flex", alignItems: "center", gap: 18, marginBottom: 28 }}>
          <SpecIcon id={spec.id} size={42} color={accent} />
          <span className="t-mono-tag" style={{ color: accent, fontSize: 14 }}>
            {String(index + 1).padStart(2, "0")} / {String(total).padStart(2, "0")} · {spec.label}
          </span>
        </div>

        <div key={index} style={{ animation: "countIn .7s cubic-bezier(.2,.7,.2,1) both" }}>
          <h2 className="h-display" style={{ fontSize: 28, color: "var(--t-2)", marginBottom: 10 }}>
            {spec.family}
          </h2>
          <h1 className="h-display" style={{
            fontSize: 148, lineHeight: 1, color: accent,
            textShadow: `0 0 36px color-mix(in oklab, ${accent} 60%, transparent)`,
          }}>
            {spec.value}
          </h1>
          <div className="h-mono" style={{ marginTop: 22, color: "var(--t-1)", fontSize: 18 }}>
            {spec.detail}
          </div>
        </div>

        {spec.benchScore !== undefined && (
          <div style={{ marginTop: 56, maxWidth: 640 }}>
            <ScoreBar value={spec.benchScore} label={spec.benchLabel} color={accent} />
          </div>
        )}

        {/* prev / next */}
        <div style={{ marginTop: 72, display: "flex", gap: 18 }}>
          <button onClick={prev} className="btn-ghost"><span>◀</span> Anterior</button>
          <button onClick={next} className="btn-ghost">Siguiente <span>▶</span></button>
          <button onClick={onClose} className="btn-ghost">Volver al equipo</button>
        </div>
      </div>

      {/* RIGHT: explainer card */}
      <div style={{ position: "absolute", right: 120, top: 200, width: 560, zIndex: 12 }}>
        <div className="panel" style={{ padding: 36 }}>
          <div className="t-mono-tag" style={{ color: accent, marginBottom: 18 }}>// Qué significa</div>
          <p style={{ fontSize: 22, lineHeight: 1.5, color: "var(--t-0)", textWrap: "pretty" }}>
            {spec.summary}
          </p>

          {spec.pros && (
            <>
              <div className="t-mono-tag" style={{ marginTop: 38, marginBottom: 18 }}>
                // Para qué te sirve
              </div>
              <ul style={{ listStyle: "none", display: "flex", flexDirection: "column", gap: 12 }}>
                {spec.pros.map((p, i) => (
                  <li key={i} style={{ display: "flex", alignItems: "center", gap: 16, color: "var(--t-1)", fontSize: 18 }}>
                    <span style={{
                      width: 28, height: 28, display: "grid", placeItems: "center",
                      border: `1px solid ${accent}`, color: accent, fontFamily: "var(--f-mono)", fontSize: 12,
                      background: `color-mix(in oklab, ${accent} 10%, transparent)`,
                    }}>
                      {String(i + 1).padStart(2, "0")}
                    </span>
                    {p}
                  </li>
                ))}
              </ul>
            </>
          )}
        </div>

        {/* mini-thumbs of other specs */}
        <div style={{ marginTop: 30, display: "grid", gridTemplateColumns: "repeat(5, 1fr)", gap: 8 }}>
          {specs.map((s, i) => (
            <button key={s.id} onClick={() => onIndex(i)} style={{
              padding: "10px 6px",
              border: `1px solid ${i === index ? accent : "var(--line-1)"}`,
              background: i === index ? `color-mix(in oklab, ${accent} 12%, transparent)` : "rgba(255,255,255,0.02)",
              cursor: "pointer",
              display: "flex", flexDirection: "column", alignItems: "center", gap: 4,
              color: i === index ? "var(--t-0)" : "var(--t-2)",
            }}>
              <SpecIcon id={s.id} size={18} color={i === index ? accent : "var(--t-2)"} />
              <span style={{ fontFamily: "var(--f-mono)", fontSize: 9, letterSpacing: "0.16em", textTransform: "uppercase" }}>
                {s.label.slice(0, 6)}
              </span>
            </button>
          ))}
        </div>
      </div>

      {/* persistent scan line */}
      <div className="scan-line" style={{ animationDuration: "5.5s", opacity: 0.4 }} />
    </div>
  );
}

Object.assign(window, { AttractScreen, ScanScreen, MainHudScreen, DetailScreen });
}
