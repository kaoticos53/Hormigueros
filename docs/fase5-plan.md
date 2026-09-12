# Fase 5 — Plan (borrador)

Realismo, especies y escala — la fase que convierte el simulador verificado
en un mundo con contenido. Este documento es el PLAN de trabajo (no el
registro de lo hecho): hitos, decisiones abiertas y criterios de cierre.
Estado del que parte: Fase 4 cerrada
([`fase4-resumen.md`](fase4-resumen.md)) — 190/190 tests, contratos de HUD
§0–§8 implementados, determinismo fijado en CI (3 capas de pin de hash), y
un bucle de jugador completo demostrado end-to-end.

**La regla que Fase 5 hereda y no puede romper**: todo lo visible sale del
Core por el stream; toda decisión de mundo deja huella en el hash; el
determinismo (semilla + comandos ⇒ mundo) es inviolable. Cada hito de abajo
lleva su test de hash invariante cuando toca telemetría, como en F4.

## 1. El contenido que ya prometió la inspección (F5.0 — barato y vendible) — ✅ IMPLEMENTADO

**Mini-grafo MLP en la tarjeta de inspección** (aplazado explícitamente en
Fase 4, diseño UX §2.2). El cerebro es pequeño y estable — 19 sensores → 8
ocultas → 6 decisiones (`BrainSizes = {19, 8, 6}`), MLP feed-forward — así
que el grafo cabe y es legible:

- **Core (F5.0a) — HECHO**: nuevo **canal F** opt-in en el stream de juego:
  `--activ-every N --inspect <antId>` emite en cada tick múltiplo de N las
  activaciones del MLP de la hormiga inspeccionada como paquete base64 dentro
  del tick JSON (`"activ":""` formato `[total u16 LE][s8 × total]`, v = byte/128).
  `MlpBrain` registra las activaciones por Evaluate (sin evaluación extra,
  sin asignaciones por tick); la emisión re-construye sensores y re-evalúa —
  determinista, telemetría pura, **hash invariante** (test). Hormiga muerta o
  ausente ⇒ paquete vacío (la vista lo distingue por `alive` del canal A).
  Bono: fix estructural del cierre de línea del stream — el canal E (F4.5)
  añadía su paquete tras la llave de cierre y el JSONL quedaba inválido
  (los ticks con feromonas iban pegados al tick siguiente); ahora `Run`
  cierra cada objeto de tick tras los canales opt-in. Pins de CI intactos.
- **UI (F5.0b) — HECHO (v1 texto-art)**: `MlpAsciiGraph` en Core y
  `ActivationViewModel` (modelo puro de Unity, compilado headless) decodifican
  el paquete y renderizan el grafo por capas con nombres canónicos del
  contrato (sensores `FoodTrailCenter`…, salidas `Steer`…, `Interact`) y
  barra con signo por nodo. Aristas coloreadas por peso y uGUI/Texture: v2.
- **Contraste con decisiones validadas**: la tarjeta ya muestra la decisión
  tomada; el grafo muestra el POR QUÉ (qué sensor dominó).

**Criterio de cierre**: con warm-v2 sembrado, el grafo de una fundadora
muestra el canal de feromona-home dominando la decisión de volver al nido —
verificable headless contra las activaciones reales.

**Validación (10 tests nuevos, `ActivationCanalFTests`)**: hash invariante con
y sin canal; cada línea JSONL válida de principio a fin; paquete de 33 nodos
decodifica en rango; stream byte a bit determinista; hormiga ausente ⇒ paquete
vacío; `activ-every` sin `--inspect` rechazado por el CLI; render ASCII con
nombres canónicos y barra con signo; longitud incorrecta rechazada.

## 2. Pulido post-release de la capa Unity (F5.1 — deuda de presentación)

Lo que Fase 4 dejó como v1 de texto (todas las piezas existen, falta
acabado):

1. **uGUI por elemento** — ✅ HECHO (núcleo): `HudElementLayoutModel` (puro,
   headless-tested) produce un rect por toast con hit-test exacto
   (`ToastAt(px)` sustituye al «índice = mouse.y/22»), specs de barra de stock
   (fracción clampada + umbral 0.20 → color rojo) y specs de botones nativos
   (Confirmar/Cancelar/Reiniciar-con-plan con su condición de habilitación).
   `HudLayoutBehaviour` instancia un elemento POR toast (pool por key, sin
   parpadeos), pinta `fillAmount` por colonia y `BindNativeButtons` conecta
   los tres botones a las acciones ya verificadas. El bootstrapper crea
   contenedor/plantilla/barras/botones con referencias asignadas. Pendiente:
   chip de estado por runway y drag&drop.
2. **Drag & drop de `.antgenome`** — el diálogo de importación acepta ruta
   de texto; el punto de entrada prometido es soltar el archivo sobre la
   ventana (Unity `Application.openExternalFiles` / editor `DragAndDrop`).
3. **Gráficas por tarjeta** — series 1 Hz ya viajan en el canal C
   (`MetricFrame`); falta el mini-gráfico de stock/descargas por colonia.
4. **Iconos, rich text y tipografía** — el render v1 es texto plano con
   glifos emoji; fuente propia y sprites para hormiga/carga/huevo.
5. **Feromonas por colonia** — el canal E hoy emite HomeTrail de la colonia
   0; selector de capa (Home/Food/Alarm) y de colonia, con paleta por capa.
6. **Audio y accesibilidad** — sonidos de evento (unload, eclosión, alerta)
   y escala de UI; sin diseño urgente, es lo único sin contrato previo.

**Criterio de cierre**: una partida de 10 min contra el fixture 256 jugable
solo con ratón, sin texto plano excepto la tarjeta de inspección.

## 3. Realismo y especies (F5.2 — el contenido nuevo)

Lo que `arquitectura.md` §Fase 5 promete, en orden de dependencia:

1. **Especies diferenciadas**:
   - *Atta* (cortadora): cadena cortar→transportar→hongo. Necesita ítems
     compuestos (hoja = N carga) y un segundo objetivo de reserva (hongo);
     la economía ya soporta stock por colonia.
   - *Eciton* (legionaria): ciclos nómadas y predación. Necesita feromona
     de alarma ofensiva (la capa Alarm ya existe y el canal la puede emitir)
     y objetivos móviles (las otras colonias).
2. **Depredadores y agresividad inter-colonia**: agentes no-colonia con
   cerebro propio (el contrato `IBrain`/`MlpBrain` es agnóstico del dueño);
   los eventos de combate entran al canal B como kinds nuevos.
3. **Competencia con apuestas observables**: hoy 2 colonias compiten y el
   semáforo contrasta sembrada vs natural; con especies el picker gana una
   dimensión (¿qué pool resiste a una invasora Eciton?).
4. **NEAT en vivo** (especificaciones §4, `NEAT (F5)`): genes estructurales
   en `.antgenome` v2 (nodos/conexiones por innovation, orden canónico,
   topes 500/2000), inspector de grafos para topologías no fijas (el
   mini-grafo de F5.0 se generaliza), re-innovación determinista al
   importar. Es el hito más caro y el que más valor le da al modo evolución:
   topologías que EVOLUCIONAN delante del jugador.

**Criterio de cierre**: 3 especies con recetas distintas jugables; una
partida de invasión Eciton vs colonia Atta sembrada con pool propio, con el
grafo NEAT del mejor cortador visible.

## 4. Escala y rendimiento (F5.3 — sostenibilidad de todo lo anterior)

- **SoA/ECS del `WorldSim`**: el layout actual (objetos por hormiga) aguanta
  2 colonias a 60 fps; N colonias y depredadores exigen layout de datos.
  Migración con pin de hash: los benchmarks y los 3 pines de CI son el
  arnés de regresión más estricto posible (el mundo NO puede cambiar).
- **LOD de feromonas**: tiles sucios + presupuesto de subida (el canal E ya
  emite RLE; el costo real es la capa Core de difusión — LOD por distancia
  a cámara solo en render).
- **GPU instancing** en el presenter (`Graphics.DrawMeshInstanced`) y pool
  por `antId` (ya existe el slot estable del snapshot).
- **Exit de fase** (de arquitectura): N colonias estables a 60 fps en la
  escena de juego.

## 5. Decisiones abiertas (se resuelven aquí, no antes)

| Decisión | Opciones | Recomendación |
|---|---|---|
| Orden F5.0 vs F5.1 | grafo primero vs pulido primero | **F5.0 primero**: es barato (1 semana), vendible (el cerebro visible es LA feature del modo evolución) y desbloquea el inspector NEAT de F5.2 |
| `.antgenome` v2 (NEAT) | formato nuevo vs extensible v1 | v2 con version validation (el camino v1→v2 de `.antsave` ya probó el patrón) |
| Feromona ofensiva de Eciton | reusar capa Alarm vs capa nueva | reusar Alarm (el canal E ya la puede emitir; solo cambia la semántica de depósito) |
| Formato de gráficas del HUD | historia en el stream vs reconstruida en UI | historia EN el stream (regla §6.2: la UI no suaviza ni re-deriva; el canal C ya trae las series) |
| Alcance del 2D→3D | Fase 6, sin adelantar | mantenerlo en Fase 6: el adaptador cambia, los contratos no |

## 6. Riesgos heredados y su estado

| Riesgo | Estado tras Fase 4 |
|---|---|
| Determinismo roto | mitigado: 3 pines de hash en CI (fixture, replay 3000, replay 6000) + suite 190 |
| Fricción Unity↔netstandard | resuelto: modelos puros compilados en la suite desde F4.1 |
| Pre-entrenamiento no converge | resuelto: cadena de pools validada con transferencia 4/5 verdes |
| Feromonas costosas | abierto: RLE funciona para render; el LOD de difusión es trabajo F5.3 |
| NEAT estanca la evolución | abierto: empezará con topología MLP fija como red de seguridad (fallback documentado) |

## 7. Qué NO es Fase 5

- **2D→3D** (Fase 6): el adaptador cambia, los contratos no; no adelantar.
- **Multijugador / seed-sharing UI**: fuera de alcance hasta v1.0.
- **Localización**: los textos del contrato del HUD son definitivos en
  español; traducirlos es decisión de release, no de fase.
- **Balance fino de especies**: se calibra con tests de balance como en
  F1–F3, no a mano.

## 8. Orden propuesto (resumen ejecutable)

```
F5.0  mini-grafo MLP (canal de activaciones opt-in + render headless)   ~1 semana
F5.1  pulido uGUI (toasts-rect, drag&drop, gráficas, iconos)            ~2–3 semanas
F5.2a Atta: cortar→transportar→hongo (ítems compuestos + hongo)         ~2 semanas
F5.2b Eciton + depredadores (Alarm ofensiva, combate en canal B)        ~2 semanas
F5.2c NEAT v2 (.antgenome v2 + inspector de grafos generalizado)        ~3–4 semanas
F5.3  SoA/ECS + LOD feromonas + GPU instancing (pin de hash)            ~3 semanas
```

Cada hito sale con: tests de hash (si toca el mundo o la telemetría),
tarjeta de contrato actualizada, y entrada cronológica en `arquitectura.md`.
