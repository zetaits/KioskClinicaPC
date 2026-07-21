<div align="center">

<img src="Assets/clinicapc-logo.png" alt="ClinicaPC" width="120" />

# KioskClinicaPC

**The kiosk that looks inside the computer in front of you and explains it.**

Fullscreen, touch-based, built for the counter of a computer shop.
It detects the real hardware of the display unit, shows it as a nice spec sheet with a price,
and generates a QR code so the customer can take the sheet as a PDF to their phone.

</div>

---

## What it does

You set the kiosk up on a shop machine. The app:

1. **Attracts** — a storefront-style loop of screens with your marketing.
2. **Scans** — a radar animation while it reads the real hardware.
3. **Shows** — a spec sheet with CPU, RAM, GPU, disk, display, battery, etc., with a price and a tier note per component.
4. **Hands out** — a QR code the customer scans to download the sheet as a PDF, no internet needed.

Everything editable by hand from the screen itself. No code required.

![Kiosk on counter](docs/screenshots/hero.png)

---

## In pictures

### 1. Attract screen
A loop of slides with a wireframe orb, neon and your message. Grabs attention when nobody's touching.

![Attract](docs/screenshots/01-attract.png)

### 2. Scan
Fullscreen radar while it reads the machine's real components.

<!-- SHOT: Screen1 Scan — the radar mid-animation -->
![Scan](docs/screenshots/02-scan.png)

### 3. Machine spec sheet
The meat of it: detected specs, price (with optional discount), machine identity and QR.

<!-- SHOT: Screen2 Main — full sheet with specs + price + QR -->
![Spec sheet](docs/screenshots/03-main.png)

### 4. Component detail
Tap any spec and its expanded explanation opens: what it is, why it matters and where it lands on an honest scale (from basic to top-of-the-line), with its real score and a couple of things you'll actually notice day to day.

<!-- SHOT: Screen3 Detail — a component opened with gauge/detail -->
![Detail](docs/screenshots/04-detail.png)

### Spec sheet on the customer's phone
The QR opens a page that **generates the PDF right on the phone**. The specs travel inside the QR,
so there's no need for internet at the shop nor a server to store anything. The sheet carries the shop's address,
email, phone and WhatsApp, clickable to message or call on the spot.

<!-- SHOT: the phone showing the PDF sheet generated after scanning the QR -->
![Sheet on phone](docs/screenshots/05-pdf-movil.png)

---

## Built for a shop

- **Free editing without coding** — Settings → "Enable free edit mode" → click any text and change it in place.
- **Automatic detection** — CPU, cores, RAM, GPU, storage, display, OS, battery, WiFi, camera and the real identity (manufacturer/model). Whatever the machine doesn't have isn't shown. And if something doesn't convince you, override it by hand.
- **Scores without lying** — each component gets a score and a tier ("balanced", "top-of-the-line"…) computed from the real hardware, placed on a scale from basic to top. No made-up numbers.
- **A price that sells** — price, discounted price and 6/12-month interest-free financing. The warranty (3 years new · 1 year second-hand) sets itself based on the machine's condition.
- **Your brand, your machine** — shop, address, attract slides (one deck for new, another for second-hand) and a photo of the machine itself that you drag into the kiosk.
- **A real kiosk mode** — starts by itself with Windows, hides the taskbar, blocks Task Manager and keeps the screen always on. Nobody gets out without the password.
- **Looks good on any machine** — the effects (blur, particles) scale themselves down on weak machines.

<!-- 📸 SHOT (optional): the Settings panel and/or free edit mode with the floating bar -->
![Settings](docs/screenshots/06-ajustes.png)

---

## How it's used

| Action | How |
|---|---|
| Open Settings | **3 clicks** in the top-right corner → password |
| Exit the kiosk | Settings → "Exit kiosk" |
| Free edit mode | Settings → "Enable free edit mode" |

Default password: `clinicapc2025` (changed in Settings → Security).

---

## The technical part (just out of curiosity)

None of this is needed to use it, but in case you're curious:

- **WPF + .NET 8**, home-grown MVVM, no external frameworks. A single window; 4 "screens" that swap in and out.
- **The QR uses no internet.** The specs are compressed (gzip) and put into the URL's `#hash`. The phone decodes it and builds the PDF with JavaScript (`html2pdf.js`). The server never sees the machine's data. The web page lives in `docs/` (GitHub Pages).
- **Hardware is read via WMI** in the background so the UI doesn't freeze.
- **Fixed 1920×1080 canvas** inside a `Viewbox`, so it scales cleanly to any resolution.
- **Adaptive graphics quality**: it detects software rendering / GPU without acceleration and dials down blurs and particles.
- **Persistence** in `%LOCALAPPDATA%\KioskClinicaPC\`: content, behaviour and last detected hardware, as JSON.

### Building

WPF, `net8.0-windows`. Needs the .NET SDK (not just the runtime):

```
dotnet build KioskClinicaPC.sln -c Release
```

Output: `src\Kiosk.Client\bin\Debug\net8.0-windows\KioskClinicaPC.exe`.

> ⚠️ Running it enters kiosk mode: it hides the taskbar and blocks Task Manager.
> To exit cleanly use Settings → "Exit kiosk" or `Ctrl+Shift+K`. Killing the process leaves the desktop locked.

---

<div align="center">
<sub>Made for ClinicaPC.</sub>
</div>
