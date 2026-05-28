/* global React, ReactDOM, AttractScreen, ScanScreen, MainHudScreen, DetailScreen */
{
const { useState, useEffect, useRef } = React;

const STAGE = {
  ATTRACT: "attract",
  SCAN:    "scan",
  MAIN:    "main",
  DETAIL:  "detail",
};

function App() {
  const [stage, setStage] = useState(STAGE.ATTRACT);
  const [specIndex, setSpecIndex] = useState(0);
  const idleRef = useRef(null);

  // idle timeout — back to attract after inactivity
  const resetIdle = () => {
    if (idleRef.current) clearTimeout(idleRef.current);
    if (stage === STAGE.ATTRACT) return;
    idleRef.current = setTimeout(() => {
      setStage(STAGE.ATTRACT);
    }, 90_000); // 90s
  };
  useEffect(() => {
    resetIdle();
    return () => idleRef.current && clearTimeout(idleRef.current);
  }, [stage]);

  // auto-advance attract → scan after 3s of no interaction (matches CTA promise)
  useEffect(() => {
    if (stage !== STAGE.ATTRACT) return;
    // give the eye a bit longer first time, shorter on reload
    const t = setTimeout(() => setStage(STAGE.SCAN), 18_000);
    return () => clearTimeout(t);
  }, [stage]);

  return (
    <div className="stage-wrap" onPointerDown={resetIdle} onPointerMove={resetIdle}>
      <ScalableStage>
        {stage === STAGE.ATTRACT && (
          <AttractScreen onStart={() => setStage(STAGE.SCAN)} />
        )}
        {stage === STAGE.SCAN && (
          <ScanScreen onDone={() => setStage(STAGE.MAIN)} />
        )}
        {stage === STAGE.MAIN && (
          <MainHudScreen
            autoCycle
            onSpec={(i) => { setSpecIndex(i); setStage(STAGE.DETAIL); }}
            onPrice={() => alert("→ Llama al asesor (CTA stub)")}
          />
        )}
        {stage === STAGE.DETAIL && (
          <DetailScreen
            index={specIndex}
            onIndex={setSpecIndex}
            onClose={() => setStage(STAGE.MAIN)}
          />
        )}
      </ScalableStage>

      <DevControls
        stage={stage}
        setStage={setStage}
        setSpecIndex={setSpecIndex}
      />
    </div>
  );
}

/* Scale the 1920×1080 stage to whatever viewport the kiosk sits in */
function ScalableStage({ children }) {
  const wrapRef = useRef(null);
  useEffect(() => {
    const fit = () => {
      if (!wrapRef.current) return;
      const w = window.innerWidth;
      const h = window.innerHeight;
      const s = Math.min(w / 1920, h / 1080);
      wrapRef.current.style.transform = `scale(${s})`;
    };
    fit();
    window.addEventListener("resize", fit);
    return () => window.removeEventListener("resize", fit);
  }, []);
  return (
    <div ref={wrapRef} className="stage" data-screen-label="Kiosk 1920x1080">
      {children}
    </div>
  );
}

/* Dev/preview navigation strip — hidden in real kiosk via prop in production */
function DevControls({ stage, setStage, setSpecIndex }) {
  return (
    <div style={{
      position: "fixed", bottom: 12, left: 12, zIndex: 99,
      display: "flex", gap: 6, padding: 8,
      background: "rgba(0,0,0,0.7)", border: "1px solid rgba(255,255,255,0.12)",
      fontFamily: "var(--f-mono)", fontSize: 11, letterSpacing: "0.12em", textTransform: "uppercase",
      backdropFilter: "blur(8px)",
    }}>
      <span style={{ color: "var(--t-2)", padding: "4px 6px" }}>dev</span>
      {["attract", "scan", "main", "detail"].map((s) => (
        <button key={s} onClick={() => setStage(s)} style={{
          padding: "4px 10px",
          background: stage === s ? "var(--cyan)" : "transparent",
          color: stage === s ? "#000" : "var(--t-1)",
          border: "1px solid rgba(255,255,255,0.16)",
          cursor: "pointer", fontFamily: "inherit", fontSize: "inherit", letterSpacing: "inherit",
          textTransform: "inherit",
        }}>{s}</button>
      ))}
    </div>
  );
}

ReactDOM.createRoot(document.getElementById("root")).render(<App />);
}
