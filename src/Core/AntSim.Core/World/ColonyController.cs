using System;
using System.Collections.Generic;
using AntSim.Core.Sim;

namespace AntSim.Core.World;

/// <summary>
/// Demografía de la colonia (especificación cerrada):
/// - Puesta:  λ_eggs = clamp((A_target − A)·k_repl + A·λ_death, 0, λ_max)
///             · ρ_res(T_runway) · q_reina · G(t) · huecos(E)
/// - Vigor al nacer: v = clamp(g0·(0.55 + 0.45·n̄), 0.15, 1.15) con n̄ = nutr/KFull
/// - Alimentación priorizada: reina → adultas → larvas (si runway &gt; T_cann)
/// - Escasez escalonada: oofagia (T_ooph) → canibalismo larval débil primero
///   (T_cann) → pupas (T_crit), con acumulador fraccionario y recuperación η.
///
/// Etapas en orden fijo (parte del determinismo): 1) runway, 2) alimentación,
/// 3) canibalismo, 4) progresión de cría, 5) puesta, 6) EWMAs.
/// </summary>
public static class ColonyController
{
    public const int MaxAdultsPerColony = SimConstants.MaxAdultsPerColony;
    public const int EggCap = 30;
    public const float KRepl = 0.05f;     // k_repl: s⁻¹ de llenado de déficit
    public const float LambdaMax = 2.0f;  // huevos/s máximo
    public const float EtaEgg = 0.3f;     // recuperación por oofagia (η·ε_egg)
    public const float EtaLarva = 0.4f;   // recuperación por canibalismo larval
    public const float EtaPupa = 0.3f;    // recuperación de pupa (último recurso)
    public const float QueenUpkeep = 0.02f;
    public const float NurseShare = 0.15f; // fracción de adultas nodrizas
    // b_nurse (Fase 3ter): antes 0.04 ep/s — INSUFICIENTE incluso para una sola
    // larva: la ventana larval exige ≥ KFull/LarvaTimeMax = 2/25 = 0.08 ep/s
    // (la LarvaIdeal de la propia especificación, 0.088) para pupar fuerte. Con
    // 0.04 repartido a partes iguales sobre la oleada de puesta, ninguna larva
    // alcanzaba NI KMin: no había eclosión JAMÁS, con abundancia o sin ella —
    // el relevo intergeneracional no podía arrancar y el arranque en frío era
    // un bloqueo estructural. Ahora la constante DERIVA de la especificación
    // (LarvaIdeal): una nodriza sostiene la tasa ideal de una larva bien
    // alimentada; la escasez la recorta vía presupuesto y runway, como antes.
    public const float NurseRate = 0.088f; // ep/s por nodriza = LarvaIdeal(0.088)
    public const float EmuTau = 5f;        // constante de tiempo de EWMAs (s)

    public static void Step(Colony c, float dt, ulong tick, List<SimEvent> events, ref uint nextAntId)
    {
        SpeciesDescriptor sp = c.Species;

        // — 0. Digestión del hongo (F5.2a.2, ANTES de todo el paso) —
        // Solo especies con hongo. Tasa PROPORCIONAL al llenado: hongo vacío no
        // digiere (el cuello de botella real de una cortadora). El caudal
        // digerido entra por RecordInflow ⇒ alimenta Stock, InflowAccum y toda
        // la demografía calibrada (gate de puesta, runway) SIN cambios.
        if (c.FungusMax > 0f && c.Fungus > 0f)
        {
            float llenado = c.Fungus / c.FungusMax;
            float digerido = Math.Min(c.Fungus, sp.DigestionRate * llenado * dt);
            c.Fungus -= digerido;
            c.RecordInflow(digerido);
            c.InflowAccum += digerido;
            events.Add(new SimEvent(SimEventKind.FungusDigested, tick, c.Id, 0, c.NestX, c.NestY));
        }

        // — 1. Runway al inicio del paso (pre-alimentación) —
        float runway = Runway(c);

        // — 2. Alimentación priorizada (orden fijo) —
        float consumed = 0f;

        // 2a. Reina primero
        float queenBite = QueenUpkeep * dt;
        if (c.Stock >= queenBite)
        {
            c.Stock -= queenBite;
            c.QueenEnergy = Math.Min(1f, c.QueenEnergy + queenBite * 10f);
            consumed += queenBite;
        }

        // 2b. Adultas: presupuesto equitativo desde el stock
        int adultCount = c.AdultCountAlive;
        if (adultCount > 0)
        {
            // F8: en la RONDA el nido paga el hueco REAL de cada obrera, no la
            // ración teórica. Antes se descontaba `adultas · b_adult · dt` aunque
            // estuvieran llenas: ~0,3 ep/s de comida que salía del stock sin
            // alimentar a nadie, y la colonia se apagaba con el tablero lleno.
            // (El mundo clásico conserva la ración fija: sus pines no se mueven.)
            float allowance = adultCount * sp.AdultUpkeep * dt;
            float need = allowance;
            if (c.RecruitmentEconomy)
            {
                need = 0f;
                for (int i = 0; i < c.Adults.Count; i++)
                {
                    var a0 = c.Adults[i];
                    if (!a0.Alive) continue;
                    need += Math.Max(0f, 1f - a0.Energy) * a0.EnergyCapacity;
                }
            }
            float adultBudget = Math.Min(c.Stock, Math.Min(allowance, need));
            c.Stock -= adultBudget;
            consumed += adultBudget;
            float perAnt = adultBudget / adultCount;
            for (int i = 0; i < c.Adults.Count; i++)
            {
                var a = c.Adults[i];
                if (!a.Alive) continue;
                a.Energy = Math.Min(1f, a.Energy + perAnt / a.EnergyCapacity);
            }
        }

        // 2c. Larvas (solo si hay reserva para ello): alimentación SERIALIZADA y
        // PRIORIZADA — la larva MÁS INVERTIDA primero (ver orden abajo).
        // Repartir el presupuesto a partes iguales sobre toda la oleada
        // (≈ 0.004 ep/s por larva) no madura a NINGUNA — mejor una larva
        // pupando que treinta muriendo de inanición, y es exactamente cómo
        // nodrizan las colonias reales bajo escasez (prioridad a la cría más
        // cercana a pupar). Si hay margen (superávit), se extiende a las demás.
        int larvaCount = c.Larvae.Count;
        if (larvaCount > 0 && runway > sp.TCann && c.Stock > 0f)
        {
            float surplus = Math.Clamp((runway - sp.TSafe) / sp.TSafe, 0f, 0.3f);
            float target = larvaCount * sp.LarvaIdeal * (1f + surplus) * dt;
            float nurseBudget = (adultCount * NurseShare) * NurseRate * dt;
            float budget = Math.Min(c.Stock, Math.Min(target, nurseBudget));
            c.Stock -= budget;
            consumed += budget;

            // Orden MÁS INVESTIDA primero (mayor nutrición; empate: larva MÁS
            // JOVEN — mayor ventana larval restante), determinista. Dos trampas
            // empíricas detectadas con la sonda (Fase 3ter): (1) repartir a partes
            // iguales no madura a ninguna larva (maxNutr ≈ 0.1 eterno); (2) con
            // empate hacia la más VIEJA, en régimen de puesta continua el
            // "campeón" era siempre la larva a punto de cumplir LarvaTimeMax: se
            // alimentaba ~0.1 ep y moría de edad — rotación improductiva. Con el
            // empate hacia la más joven, cada campeón tiene ~25 s por delante:
            // alcanza KFull (0.088 ep/s ⇒ ~14 s) con margen y pupa.
            Span<int> order = larvaCount <= 64 ? stackalloc int[larvaCount] : new int[larvaCount];
            for (int i = 0; i < larvaCount; i++) order[i] = i;
            for (int i = 1; i < larvaCount; i++)
            {
                int key = order[i];
                int j = i - 1;
                while (j >= 0 &&
                       (c.Larvae[order[j]].Nutrition < c.Larvae[key].Nutrition ||
                        (c.Larvae[order[j]].Nutrition == c.Larvae[key].Nutrition &&
                         c.Larvae[order[j]].Insert < c.Larvae[key].Insert)))
                {
                    order[j + 1] = order[j];
                    j--;
                }
                order[j + 1] = key;
            }

            // Serial: satura la larva más invertida hasta KFull·(1+surplus) antes
            // de pasar a la siguiente — el excedente del presupuesto se concentra.
            float remaining = budget;
            for (int i = 0; i < larvaCount && remaining > 0f; i++)
            {
                var l = c.Larvae[order[i]];
                float cap = sp.KFull * (1f + surplus) - l.Nutrition;
                if (cap <= 0f) continue;
                float bite = Math.Min(remaining, cap);
                l.Nutrition += bite;
                remaining -= bite;
            }
            // (Si sobra presupuesto tras saturar todas, queda sin usar: no se
            // sobrealimenta, coherente con el tope de 'target'.)
        }

        // — 3. Canibalismo escalonado (recupera energía al stock) —
        if (runway < sp.TOoph)
        {
            float deficit = c.ConsumeEma * dt * Math.Max(0f, (sp.TOoph - runway) / sp.TOoph);
            c.CannibalAccumulator += deficit;
            // Comer solo lo que el acumulador puede pagar: evita sobregirar, respeta
            // el orden débil-primero y corta los bucles con recuperación 0.
            while (c.CannibalAccumulator > 0f)
            {
                if (runway < sp.TCrit && c.Eggs.Count == 0 && c.Larvae.Count == 0 && c.Pupae.Count > 0)
                {
                    float rec = EtaPupa * sp.KFull;
                    if (c.CannibalAccumulator < rec) break;
                    c.Pupae.RemoveAt(c.Pupae.Count - 1);
                    c.CannibalAccumulator -= rec;
                    c.Stock += rec;
                }
                else if (runway < sp.TCann && c.Larvae.Count > 0)
                {
                    int idx = WeakestLarvaIndex(c);
                    float rec = EtaLarva * c.Larvae[idx].Nutrition;
                    if (rec <= 0f || c.CannibalAccumulator < rec) break;
                    c.Larvae.RemoveAt(idx);
                    c.CannibalAccumulator -= rec;
                    c.Stock += rec;
                }
                else if (c.Eggs.Count > 0)
                {
                    float rec = EtaEgg * sp.EggCost;
                    if (c.CannibalAccumulator < rec) break;
                    c.Eggs.RemoveAt(0);
                    c.CannibalAccumulator -= rec;
                    c.Stock += rec;
                }
                else break;
            }
        }

        // — 4. Progresión de la cría —
        // Huevo → larva
        for (int i = c.Eggs.Count - 1; i >= 0; i--)
        {
            var e = c.Eggs[i];
            e.Age += dt;
            if (e.Age >= sp.EggTime)
            {
                c.Eggs.RemoveAt(i);
                c.Larvae.Add(new BroodMember { Kind = BroodKind.Larva, Insert = c.InsertCounter++, G0 = e.G0 });
            }
        }

        // Larva → pupa (fuerte si nutr ≥ KFull; minim si nutr ≥ KMin al vencer el plazo)
        for (int i = c.Larvae.Count - 1; i >= 0; i--)
        {
            var l = c.Larvae[i];
            l.Age += dt;
            if (l.Nutrition >= sp.KFull)
            {
                c.Larvae.RemoveAt(i);
                c.Pupae.Add(new BroodMember { Kind = BroodKind.Pupa, Insert = c.InsertCounter++, G0 = l.G0, Nutrition = l.Nutrition });
            }
            else if (l.Age >= sp.LarvaTimeMax)
            {
                c.Larvae.RemoveAt(i);
                if (l.Nutrition >= sp.KMin)
                    c.Pupae.Add(new BroodMember { Kind = BroodKind.Pupa, Insert = c.InsertCounter++, G0 = l.G0, Nutrition = l.Nutrition, Weak = true });
                // si nutr < KMin → muere de inanición (la cría no es adulta)
            }
        }

        // Pupa → adulta (espera una vacante: el máximo de 40 adultas es duro)
        for (int i = c.Pupae.Count - 1; i >= 0; i--)
        {
            var p = c.Pupae[i];
            p.Age += dt;
            if (p.Age < sp.PupaTime) continue;
            if (c.AdultCountAlive >= MaxAdultsPerColony) continue;

            c.Pupae.RemoveAt(i);
            float nRatio = Math.Min(1.3f, p.Nutrition / sp.KFull);
            float vigor = Math.Clamp(p.G0 * (0.55f + 0.45f * nRatio), 0.15f, 1.15f);

            var ant = new Ant
            {
                Id = nextAntId++,
                ColonyId = c.Id,
                X = c.NestX + (float)(c.Rng.NextDouble01() * 2.0 - 1.0) * 30f,
                Y = c.NestY + (float)(c.Rng.NextDouble01() * 2.0 - 1.0) * 30f,
                Heading = (float)(c.Rng.NextDouble01() * Math.PI * 2.0 - Math.PI)
            };
            ant.InitFromVigor(sp.EnergyCapacity, sp.BaseLifespan, vigor);
            c.Adults.Add(ant);
            events.Add(new SimEvent(SimEventKind.Eclosed, tick, c.Id, ant.Id, ant.X, ant.Y));
        }

        // — 5. Puesta —
        runway = Runway(c);
        float rho = Math.Clamp((runway - sp.TCrit) / (sp.TSafe - sp.TCrit), 0f, 1f);
        float qQueen = Math.Clamp(c.QueenEnergy, 0.3f, 1f);
        float huecos = c.Eggs.Count < EggCap ? (EggCap - c.Eggs.Count) / (float)EggCap : 0f;
        // (Fase 3ter) Arranque conservador: el término de DEFICIT (crecer hasta
        // MaxAdultsPerColony) se escala por la entrada REAL de comida
        // (InflowEma, 1 tras ~0.1 ep/s). Antes la reina fundadora inundaba la
        // colonia de huevos (~1.6/s = 0.8 ep/s ≈ 47 % de la reserva fundadora)
        // para "crecer a 40" SIN comida — el arranque en frío quemaba el stock
        // en ~120 s y la colonia moría antes de que el relevo de sueltas pudiera
        // completar la primera descarga (sonda: eclosed 6, unload 0, extinta a
        // los 240 s). Sin entrada solo se reponen bajas (A·λ_death); la
        // expansión espera al primer ciclo de comida — biología de fundación
        // real: primera puesta limitada, expansión ligada a la entrada.
        // F8: en la ronda la puesta se mide con la entrada NETA. El gate clásico
        // (`InflowEma · 10`, saturado a 0,1 ep/s) trataba cualquier goteo como
        // abundancia: la reina ponía ~0,9 huevos/s con 0,35 ep/s de entrada, la
        // cría se comía la reserva fundadora y la colonia no se reponía. Ahora
        // solo se expande el margen que la entrada paga por encima de lo que ya
        // cuestan las adultas.
        float upkeepDemand = Math.Max(1e-6f, c.AdultCountAlive * sp.AdultUpkeep);
        float inflowGate = c.RecruitmentEconomy
            ? Math.Clamp((c.InflowEma - upkeepDemand) / upkeepDemand, 0f, 1f)
            : Math.Clamp(c.InflowEma * 10f, 0f, 1f);
        // Feedback homeostático biológico: si el stock es abundante (>60%) acelera la puesta;
        // si el stock cae en zona crítica (<20%) frena la puesta para proteger la colonia.
        float stockRatio = sp.StockMax > 0f ? c.Stock / sp.StockMax : 0f;
        float stockFeedback = stockRatio > 0.60f
            ? (1.0f + 0.35f * ((stockRatio - 0.60f) / 0.40f))
            : Math.Clamp(stockRatio / 0.20f, 0.05f, 1.0f);

        // SOSTENIBILIDAD (ronda, 2026-10-30). Dos correcciones a la puesta que el
        // mundo clásico no puede adoptar sin mover sus pines de hash:
        //
        // 1) La REPOSICIÓN se mide con la mortalidad real de la especie, no con la
        //    constante `DeathRate`. `DeathRate` vale 1/240 s y una obrera vive
        //    `BaseLifespan · (0,7 + 0,6·vigor)` = 806 s de media: 3,4× menos. Con la
        //    constante la reina ponía un 60 % por encima de las bajas.
        // 2) El DÉFICIT descuenta la cría que ya va en camino, y la puesta se para
        //    cuando el embudo cubre lo que las nodrizas pueden criar.
        //
        // Sin las dos, la cola de pupas crecía sin techo —no mueren de viejas y no
        // eclosionan sin vacante—: la ronda de dos colonias cerraba con 479 y 943
        // pupas, 3 322 huevos puestos para 789 eclosiones y un 31 % de la cría
        // muriendo de inanación como larva. Medido: pico 6 pupas, 765 huevos y 738
        // eclosiones, con las 40 adultas igual.
        float reposicion = c.RecruitmentEconomy ? sp.RealDeathRate : sp.DeathRate;
        int enEmbudo = 0;
        bool puedePoner = true;
        if (c.RecruitmentEconomy)
        {
            enEmbudo = c.Eggs.Count + c.Larvae.Count + c.Pupae.Count;
            // Cada nodriza sostiene una larva a ritmo ideal (`NurseRate =
            // LarvaIdeal`), así que la capacidad del embudo es `adultas ·
            // NurseShare` — la misma cuenta con la que se paga la alimentación
            // larval, sin constante nueva. Sin techo, la cola crecía sin fin.
            int capacidad = Math.Max(1, (int)(c.AdultCountAlive * NurseShare));
            puedePoner = enEmbudo < capacidad;
        }
        // Contando el embudo, el término de déficit vale 0 en cuanto el cupo está
        // cubierto por adultos MÁS cría: sin descontarla, `(Max − A)·KRepl`
        // sobrellenaba en los 45 s de retardo de huevo→larva→pupa.
        int vacantes = Math.Max(0, MaxAdultsPerColony - c.AdultCountAlive - enEmbudo);
        float lambda = puedePoner
            ? Math.Clamp(vacantes * KRepl * inflowGate + c.AdultCountAlive * reposicion,
                         0f, LambdaMax) * rho * qQueen * huecos * stockFeedback
            : 0f;

        c.EggAccumulator += lambda * dt;
        while (c.EggAccumulator >= 1f && c.Stock >= sp.EggCost)
        {
            c.Stock -= sp.EggCost;
            c.QueenEnergy = Math.Max(0.3f, c.QueenEnergy - 0.01f);
            float g0 = 0.4f + 0.6f * c.Rng.NextFloat01();
            c.Eggs.Add(new BroodMember { Kind = BroodKind.Egg, Insert = c.InsertCounter++, G0 = g0 });
            c.EggAccumulator -= 1f;
            events.Add(new SimEvent(SimEventKind.EggLaid, tick, c.Id, 0, c.NestX, c.NestY));
        }

        // — 6. EWMAs (cierran el lazo) —
        float alpha = dt / EmuTau;
        float consumeRate = consumed / Math.Max(dt, 1e-6f);
        c.ConsumeEma += (consumeRate - c.ConsumeEma) * alpha;
        float inflowRate = c.InflowAccum / Math.Max(dt, 1e-6f);
        c.InflowEma += (inflowRate - c.InflowEma) * alpha;
        c.InflowAccum = 0f;
    }

    public static float Runway(Colony c)
        => c.Stock / Math.Max(c.ConsumeEma, 1e-4f);

    /// <summary>Larva más débil (menor nutrición; empate por orden de inserción).</summary>
    private static int WeakestLarvaIndex(Colony c)
    {
        int best = 0;
        for (int i = 1; i < c.Larvae.Count; i++)
        {
            var a = c.Larvae[i];
            var b = c.Larvae[best];
            if (a.Nutrition < b.Nutrition || (a.Nutrition == b.Nutrition && a.Insert < b.Insert))
                best = i;
        }
        return best;
    }
}