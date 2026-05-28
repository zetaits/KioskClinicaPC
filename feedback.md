# Feedback de diseño v4 · KioskClinicaPC (WPF) — Attract

> Continuación de `WPF-design-feedback-v3.md`. Pasa este archivo a Claude Code.
>
> **Estado**: v1 + v2 + v3 aplicados. La esfera wireframe ya se ve, el CTA tiene brackets y el glow del título está más intenso. **Pero el Attract todavía no respira como el mockup**, por tres causas concretas que sí podemos arreglar sin tocar más arquitectura:
>
> 1. El "halo ovalado" que aparece en mitad de la pantalla y que **no existe en el mockup**.
> 2. Los títulos están en **minúsculas** cuando el mockup los lleva en **MAYÚSCULAS**.
> 3. El título 2 está en naranja sólido, pero el mockup tiene un **gradiente vertical naranja → melocotón → amarillo** que es 70 % del “punch” visual de esa palabra.
>
> Esto va sólo de estos tres puntos. No tocar nada más.

---

## 🔥 PRIORIDAD A — Eliminar el vignette interior (el "halo ovalado" feo)

En `Screen0_Attract`, justo después del `<Viewbox>` de la esfera, hay un `<Rectangle>` con un `RadialGradientBrush` muy oscuro (`#E6000000` en el centro, `#73000000` al 55 %). **Ese rectángulo es el "halo ovalado"** que se ve en el render actual: oscurece un óvalo de ~26 % × 32 % en el centro de la pantalla, y al estar sobre el wash de luz cálida del fondo, el contraste lo dibuja como una mancha gris ovalada bien marcada.

En el mockup **no hay ese vignette interior**. Las líneas del wireframe atraviesan el centro de la composición de borde a borde, y la legibilidad del título se consigue sólo por el peso del tipo blanco + el glow del título 2.

**Eliminar entero** este bloque (queda comentado por si después quieres volver, pero el por defecto debe ser sin él):

```xml
<!-- ❌ ELIMINAR — éste es el "halo ovalado" que NO está en el mockup
<Rectangle IsHitTestVisible="False" Opacity="0.85">
    <Rectangle.Fill>
        <RadialGradientBrush Center="0.5,0.5" GradientOrigin="0.5,0.5" RadiusX="0.32" RadiusY="0.26">
            <GradientStop Color="#E6000000" Offset="0"/>
            <GradientStop Color="#73000000" Offset="0.55"/>
            <GradientStop Color="Transparent" Offset="1"/>
        </RadialGradientBrush>
    </Rectangle.Fill>
</Rectangle>
-->
```

> **Sobre la legibilidad del título**: lo que en v3 vendíamos como "necesario para que el texto blanco destaque" no se sostiene viendo el mockup — ahí no hay vignette y el texto se lee perfecto. La razón es que (a) "ESTE EQUIPO" es **blanco puro y muy grueso**, y (b) "TE ESTÁ OBSERVANDO" lleva un glow tan ancho que el propio glow oscurece visualmente las líneas por detrás. Con eso basta.
>
> Si tras eliminar el vignette ves que las líneas del wireframe compiten con el texto, **bajar la `Opacity` del Viewbox de 0.85 a 0.70**, pero NO volver a añadir el vignette.

---

## 🔥 PRIORIDAD B — Títulos en MAYÚSCULAS

En `MainWindow.xaml.cs` los strings están en mixed case (`"Este equipo"`, `"te está observando."`). El mockup va todo en mayúsculas — es lo que le da el peso “tipográfico póster” de la composición. Cambia los tres pares:

```csharp
// línea ~79
private string _attractTitle1 = "ESTE EQUIPO";
// línea ~82
private string _attractTitle2 = "TE ESTÁ OBSERVANDO.";

// línea ~710-711 (rotador)
AttractTitle1 = "ESTE EQUIPO";
AttractTitle2 = "TE ESTÁ OBSERVANDO.";

// línea ~717-718
AttractTitle1 = "LO ENTIENDES";
AttractTitle2 = "AUNQUE NO SEAS TÉCNICO.";

// línea ~724-725
AttractTitle1 = "HASTA 60% MENOS";
AttractTitle2 = "QUE COMPRARLO NUEVO.";
```

> **No** uses un `IValueConverter` para hacer `.ToUpper()` en el binding: el rotador ya re-asigna el string entero, así que es ruido innecesario. Cambia el string directamente.

---

## 🔥 PRIORIDAD C — Gradient vertical en el título 2 (naranja → melocotón → amarillo)

Mira el mockup: la palabra "TE ESTÁ" sale rojo-melocotón arriba y "OBSERVANDO." termina en amarillo cálido abajo. Es un **gradient lineal vertical** sobre el texto del título 2, no un color sólido.

En la sección del Title 2 (`Grid HorizontalAlignment="Center"` con las 4 capas: Cyan glitch, Magenta glitch, Neon core, Base), sustituye **sólo** el `Foreground` de la "Base title (wide halo)" — la última capa, la que está debajo del todo — por un `LinearGradientBrush` vertical. Las otras tres capas (las dos de glitch y el core) **se quedan iguales**, porque su papel es dar densidad/animación, no color.

```xml
<!-- Base title (wide halo) — REEMPLAZAR el Foreground sólido por un gradient -->
<TextBlock Text="{Binding AttractTitle2}" Style="{StaticResource DisplayFont}"
           FontSize="140" TextAlignment="Center" LineHeight="132">
    <TextBlock.Foreground>
        <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
            <!-- top: melocotón-rojo, mismo tono que CyanColor pero más claro -->
            <GradientStop Color="#FF8C6E" Offset="0"/>
            <!-- mid: el naranja base -->
            <GradientStop Color="#F37A4A" Offset="0.45"/>
            <!-- bottom: amarillo cálido, casi gold -->
            <GradientStop Color="#FFC857" Offset="1"/>
        </LinearGradientBrush>
    </TextBlock.Foreground>
    <TextBlock.Effect>
        <DropShadowEffect Color="{StaticResource CyanColor}" BlurRadius="48"
                          ShadowDepth="0" Opacity="0.85"/>
    </TextBlock.Effect>
</TextBlock>
```

> **Por qué funciona**: en el mockup el glow saliendo del texto **no es plano**, es cálido-arriba/dorado-abajo porque la fuente de luz del texto cambia de hue. Replicarlo con un `LinearGradientBrush` en el `Foreground` (y dejando el `DropShadowEffect` sólo en naranja base) consigue ese efecto sin necesidad de capas de blur adicionales.
>
> Si después de aplicarlo el amarillo se ve “demasiado”, mueve el `Offset` del último stop de `1` a `0.85` (deja un trocito del naranja al final) o cambia `#FFC857` por algo más anaranjado como `#F5A85A`.

---

## 🌌 PRIORIDAD D (opcional pero recomendado) — Wash de fondo un punto más cálido

En el mockup el fondo no es negro plano: el cuadrante superior-derecho tiene un wash muy sutil de naranja/ámbar y el inferior-izquierdo un azul-violáceo apenas perceptible. Tu render actual ya tiene los `RadialGradientBrush` de fondo (`bg-mesh` en lo alto del archivo), pero la mezcla actual carga demasiado de morado/magenta-violet (`#331A66` al 60 %) y poco del cálido.

Cambios mínimos en el bloque `<!-- ================= AMBIENT BACKGROUNDS ================= -->`:

1. **El primer rectángulo** (radial 70% 55% at 82% 12%) — subir `Opacity` de `0.24` a **`0.35`**. Es el wash naranja arriba-derecha.
2. **El cuarto rectángulo** (radial purple at 95% 78%) — bajar `Opacity` de `0.60` a **`0.30`**. Ese morado oscuro está apagando todo lo demás.
3. **Dejar igual** los otros tres.

```xml
<!-- 1) Wash cálido arriba-derecha → subir opacity -->
<Rectangle Opacity="0.35"> <!-- antes 0.24 -->
    <Rectangle.Fill>
        <RadialGradientBrush Center="0.82,0.12" GradientOrigin="0.82,0.12" RadiusX="0.70" RadiusY="0.55">
            <GradientStop Color="{StaticResource MagentaColor}" Offset="0"/>
            <GradientStop Color="Transparent" Offset="0.65"/>
        </RadialGradientBrush>
    </Rectangle.Fill>
</Rectangle>

<!-- ... -->

<!-- 4) Morado abajo-derecha → bajar opacity -->
<Rectangle Opacity="0.30"> <!-- antes 0.60 -->
    <Rectangle.Fill>
        <RadialGradientBrush Center="0.95,0.78" GradientOrigin="0.95,0.78" RadiusX="0.45" RadiusY="0.40">
            <GradientStop Color="#331A66" Offset="0"/>
            <GradientStop Color="Transparent" Offset="0.70"/>
        </RadialGradientBrush>
    </Rectangle.Fill>
</Rectangle>
```

---

## 📌 Orden de ataque

1. **A** — Quitar el vignette interior. Cambio de 1 línea, ganancia enorme (es la causa principal del “halo ovalado”).
2. **B** — Mayúsculas en los tres pares de strings. 30 segundos.
3. **C** — Gradient vertical en el título 2. Es lo que vende el "póster".
4. **D** (opcional) — Subir wash cálido / bajar wash morado del fondo.

Tras A + B + C el Attract debería ser casi idéntico al mockup. D es el último 5 %.

---

## 📎 Notas

- **No tocar** otros vignettes ni el `bg-vignette` externo (el que oscurece esquinas) — ése sí está bien.
- **No tocar** la esfera wireframe — ya está donde tiene que estar tras v3.
- **No tocar** las capas de glitch del título 2 (cyan/magenta clip). Sólo la capa base (la del fondo, sin clip).
- Si quieres validar visualmente: tras aplicar A + B + C, debería desaparecer la mancha gris ovalada del centro y los títulos deberían pasar a tener un perfil “rock poster” con la palabra inferior haciendo degradado vertical.
