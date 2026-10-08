"""Canonicalizador de assets YAML de Unity — anclas y fileIDs derivados del contenido.

POR QUÉ EXISTE. Unity no da API para fijar el `m_LocalIdentfierInFile` de los
objetos que crea: lo asigna de un contador cuyo origen es RANDOM por sesión.
Medido (2026-10-07): dos pases del MISMO montaje dieron bases 54570640 y 8954272
con los mismos 425 documentos en `Game.unity`, y el bloque `AssetVersion` que URP
añade a cada `.mat` lleva un id de 64 bits aleatorio y CAMBIA DE SITIO. La
consecuencia práctica: por cada pasada del generador, 17 assets (16 `.mat` y
`Game.unity`) salen con bytes distintos aunque la escena sea la misma — git ve un
diff que no significa nada, y para siempre.

QUÉ HACE. Reescribe cada asset con:

  · ANCLAS = hash del CONTENIDO del documento (huella recursiva por iteración
    hasta el punto fijo, la misma bisimulación que usa
    `scripts/unity-asset-equivalence.py` para decidir si dos assets son el
    mismo), no de su posición ni del contador de Unity;
  · REFERENCIAS LOCALES `{fileID: N}` remapeadas a las nuevas. Las que llevan
    `guid:` son EXTERNAS (otro asset, un paquete, un builtin) y NO se tocan; el
    null `{fileID: 0}` se queda en 0;
  · DOCUMENTOS ordenados de forma canónica (ascendente por id, que es como los
    escribe el serializador de Unity), porque el orden con el que salen de una
    pasada no es estable (`Material` y `AssetVersion` se alternan);
  · sin blancos de sobra al final de cada documento y el fichero terminado en
    exactamente un `\\n`, con finales LF (lo que pide `.gitattributes` para los
    assets en modo Force Text).

ANCLAS QUE NO SE PUEDEN TOCAR, y por qué (las dos MEDIDAS):

  · `SceneRoots` (clase 1660057539) lleva SIEMPRE `&9223372036854775807`
    (long.MaxValue) y va el último: Unity reescribe cualquier otro valor que se
    le ponga. Como es el mayor int64, el orden ascendente por id ya lo deja al
    final.
  · El objeto PRINCIPAL de un asset mono-objeto usa la convención `clase ×
    100000` (`&2100000` un Material, `&2800000` una Texture2D, `&4300000` una
    Mesh, `&4800000` un Shader, `&11400000` un ScriptableObject) y es una
    referencia ENTRE FICHEROS: las escenas y los prefabs lo citan como
    `{fileID: 2100000, guid: <guid del asset>, type: 2}`. Renumerarlo ROMPE esas
    referencias —medido: al renumerar `AntMat.mat` el presenter de la escena se
    quedó con `AntMaterial = null`, mientras el `AntMesh` (intacto) seguía
    resolviendo—.

QUÉ GARANTIZA EL PROPIO SCRIPT (`--self-test`, sin repo): canonicalizar dos veces
da lo mismo (idempotencia), reordenar los documentos no cambia la salida,
renumerar las anclas viejas tampoco, y los invariantes internos se sostienen
(anclas únicas, toda referencia local existe, un documento por ancla).

Uso:
  python unity-canonicalize-assets.py                # canonicaliza EN SITIO los assets del proyecto
  python unity-canonicalize-assets.py --check        # no escribe: rc 1 si alguno derivaría
  python unity-canonicalize-assets.py RUTA…          # sólo esos ficheros (en sitio, o con --check)
  python unity-canonicalize-assets.py --project RUTA # otro proyecto de Unity (por defecto el del repo)
  python unity-canonicalize-assets.py --selftest     # los invariantes, sin repo
  python unity-canonicalize-assets.py --list         # qué se reescribe y qué no

Salida: 0 hecho (o nada que hacer) · 1 con `--check`, algún asset derivaría · 2 uso.

Lo llama `scripts/playpass-live.sh` justo después de que el bootstrapper genere la
escena y los materiales, y antes de medir: así el árbol queda con el byte
canónico y dos pasadas seguidas dan el MISMO `git diff`.
"""
import argparse
import hashlib
import os
import re
import sys

# Ámbito por defecto: el proyecto Unity del repo. Sólo se reescriben ficheros de
# TEXTO de Unity con documentos (`--- !u!`): un `.png` o un `.ttf` no son YAML y
# no se tocan, y un `.cs`/`.md` tampoco es un asset serializado.
PROJECT = 'src/App/AntSim.Unity'
# Y sólo los assets que el bootstrapper GENERA (escena y materiales, meshes y
# texturas incluidos): son los que una pasada reescribe con anclas nuevas. El
# `DefaultVolumeProfile.asset` de URP o los `Settings/*.asset` los escribe el
# paquete una vez, no el generador, y no es este ruido el que ensucia cada pase.
GENERATED_DIRS = ('Assets/Scenes', 'Assets/Materials')
ASSET_SUFFIXES = ('.mat', '.unity', '.asset', '.prefab', '.controller', '.anim')

# `stripped` es legítimo (Unity lo escribe en instancias de prefab recortadas):
# el sufijo el la cabecera se conserva tal cual.
ANCHOR = re.compile(r'^--- !u!(\d+) &(-?\d+)(.*)$')
REF = re.compile(r'\{fileID: (-?\d+)([^}]*)\}')

# 31 bits: ids no negativos y «pequeños» — el rango donde Unity se porta.
HASH_BITS = 31

RESERVED = {1660057539: 9223372036854775807}   # SceneRoots: el ancla de Unity
MAIN_OBJECT = 100000                           # clase × 100000 = objeto principal


def normaliza(text):
    """Finales de línea a \\n: los assets del repo van en LF por `.gitattributes`."""
    return text.replace('\r\n', '\n').replace('\r', '\n')


# ── El analizador (puro: texto entra, texto canónico sale) ──────────────────

def split_docs(text):
    """(cabecera, [documento…]); cada documento es su lista de líneas, sin
    blancos de sobra al final.

    El recorte de blancos finales NO es cosmética: Unity/URP dejan una línea
    vacía antes del bloque `AssetVersion` en unos `.mat` y no en otros —y el
    fichero acaba a veces sin `\\n` final—, así que la misma receta escrita dos
    veces daba ficheros distintos y una huella sensible al blanco producía DOS
    ids para el mismo contenido (`&1702511379` y `&127863662` en la misma
    pasada). El blanco es del escritor, no del contenido.
    """
    lines = normaliza(text).split('\n')
    header, i = [], 0
    while i < len(lines) and not lines[i].startswith('--- '):
        header.append(lines[i])
        i += 1
    docs, cur = [], None
    for line in lines[i:]:
        if line.startswith('--- '):
            if cur is not None:
                docs.append(_sin_blancos_finales(cur))
            cur = [line]
        elif cur is not None:
            cur.append(line)
    if cur is not None:
        docs.append(_sin_blancos_finales(cur))
    return header, docs


def _sin_blancos_finales(doc):
    while len(doc) > 1 and not doc[-1].strip():
        doc.pop()
    return doc


def parse(doc):
    """(clase, ancla vieja, sufijo, cuerpo). Falla si la cabecera no es un ancla."""
    m = ANCHOR.match(doc[0])
    if not m:
        raise ValueError('documento sin cabecera de ancla: %r' % doc[0][:60])
    return int(m.group(1)), int(m.group(2)), m.group(3), doc[1:]


def fingerprints(docs):
    """Huella por documento (clave = ancla vieja), invariante a las anclas viejas
    y al orden de los documentos.

    Se itera: la huella de un documento depende de la de sus destinos —así que un
    objeto y su transform tienen huellas distintas—, pero nunca de los ids que
    traía el fichero. Se para cuando el multiconjunto de huellas se repite (con
    ciclos puede oscilar; el conjunto se estabiliza igual).
    """
    def con(bodies, resolver):
        return '\n'.join(
            REF.sub(lambda m: m.group(0) if 'guid' in m.group(2) or int(m.group(1)) == 0
                    else '{fileID: %s}' % resolver(int(m.group(1))), line)
            for line in bodies)

    cur = {}
    for cls, old, _, body in docs:
        cur[old] = hashlib.sha1(('%d\n' % cls + con(body, lambda _: '?')).encode()).hexdigest()
    seen = set()
    for _ in range(64):
        nxt = {}
        for cls, old, _, body in docs:
            nxt[old] = hashlib.sha1(
                ('%d|%s' % (cls, con(body, lambda r: cur.get(r, '?')))).encode()).hexdigest()
        key = tuple(sorted(nxt.values()))
        if key == tuple(sorted(cur.values())) or key in seen:
            return nxt
        seen.add(key)
        cur = nxt
    return cur


def canonical(text):
    """(texto canónico, nº de documentos). Lanza ValueError si algo no cuadra."""
    header, raw = split_docs(text)
    docs = [parse(d) for d in raw]
    if not docs:
        raise ValueError('el fichero no tiene ningún documento `--- !u!`')
    fp = fingerprints(docs)

    vistas = set()
    for doc in docs:
        if doc[1] in vistas:
            raise ValueError('dos documentos con la misma ancla vieja: &%d' % doc[1])
        vistas.add(doc[1])

    # primero los ids que NO se pueden tocar, así los derivados del contenido no
    # pueden chocar con ellos.
    used, new = set(), {}
    for cls, old, _, _ in docs:
        if cls in RESERVED:
            new[old] = RESERVED[cls]
            used.add(new[old])
        elif old == cls * MAIN_OBJECT:
            new[old] = old
            used.add(old)

    # ids por CONTENIDO, en orden canónico (huella, clase, ancla vieja) para que
    # la asignación no dependa del orden con el que llegaron los documentos.
    for cls, old, _, _ in sorted(docs, key=lambda d: (fp[d[1]], d[0], d[1])):
        if old in new:
            continue
        h = hashlib.sha1(('id|%d|%s' % (cls, fp[old])).encode()).digest()
        n = 1 + int.from_bytes(h[:8], 'big') % (2 ** HASH_BITS - 1)
        while n in used:
            n = 1 + (n % (2 ** HASH_BITS - 1)) + 1
        used.add(n)
        new[old] = n

    out = list(header)

    def remapea(line):
        return REF.sub(
            lambda m: m.group(0) if 'guid' in m.group(2) or int(m.group(1)) == 0
            else '{fileID: %d}' % new[int(m.group(1))], line)

    # orden ASCENDENTE POR id: es lo que hace el serializador de Unity (medido),
    # así un guardado del editor reproduce este orden en vez de cambiarlo.
    for cls, old, suffix, body in sorted(docs, key=lambda d: new[d[1]]):
        out.append('--- !u!%d &%d%s' % (cls, new[old], suffix))
        out.extend(remapea(line) for line in body)
    texto = '\n'.join(out)
    if not texto.endswith('\n'):
        texto += '\n'
    return texto, len(docs)


# ── Descubrimiento ──────────────────────────────────────────────────────────

def es_asset_yaml(path):
    """Un asset de texto de Unity: sufijo conocido y, dentro, algún documento."""
    if not path.lower().endswith(ASSET_SUFFIXES):
        return False
    try:
        with open(path, encoding='utf-8', errors='replace', newline='') as handle:
            for line in handle:
                if line.startswith('--- !u!'):
                    return True
    except OSError:
        return False
    return False


def assets_del_proyecto(project):
    """Los assets YAML GENERADOS del proyecto, en orden estable (para que la
    salida no dependa del sistema de ficheros). `None` si el proyecto no es un
    proyecto de Unity."""
    if not os.path.isdir(os.path.join(project, 'Assets')):
        return None
    rutas = []
    for sub in GENERATED_DIRS:
        raiz = os.path.join(project, sub)
        if not os.path.isdir(raiz):
            continue
        for base, dirs, files in os.walk(raiz):
            dirs.sort()
            for nombre in sorted(files):
                ruta = os.path.join(base, nombre)
                if es_asset_yaml(ruta):
                    rutas.append(ruta)
    return rutas


def leer(path):
    with open(path, encoding='utf-8', newline='') as handle:
        return handle.read()


def escribir(path, texto):
    with open(path, 'w', encoding='utf-8', newline='') as handle:
        handle.write(texto)


def proceso(rutas, check):
    """(rc, reescritos, derivarían). Imprime una línea por asset tocado."""
    reescritos, derivarían, fallos = 0, 0, 0
    for ruta in rutas:
        try:
            original = leer(ruta)
            canonico, ndocs = canonical(original)
        except (ValueError, OSError) as exc:
            print(f'✗ NO SE PUDO CANONICALIZAR {ruta}: {exc}', file=sys.stderr)
            fallos += 1
            continue
        if canonico == original:
            continue
        if check:
            print(f'DERIVARÍA  {ruta}  ({ndocs} documentos)')
            derivarían += 1
        else:
            escribir(ruta, canonico)
            print(f'REESCRITO  {ruta}  ({ndocs} documentos)')
            reescritos += 1
    return (1 if (derivarían or fallos) else 0), reescritos, derivarían, fallos


def listar():
    print('Qué canonicaliza:')
    print(f'  · los assets YAML que el generador escribe en {PROJECT}/' +
          ' y '.join(GENERATED_DIRS) + ', con sufijo ' + ', '.join(ASSET_SUFFIXES) +
          ' y al menos un documento `--- !u!`.')
    print('  · las anclas y fileIDs se derivan del CONTENIDO (no del contador de Unity):')
    print('    anclas = huella recursiva del documento; las referencias locales se')
    print('    remapean; las externas (`guid:`) y el null `{fileID: 0}` no se tocan;')
    print('    el objeto principal (`clase × 100000`) y `SceneRoots` quedan intactos.')
    print('  · los documentos se ordenan ascendente por id (lo que hace Unity) y el')
    print('    fichero queda en LF, con un solo `\\n` final.')
    print('Qué NO toca:')
    print('  · binarios (`.png`, `.ttf`, `.antgenome`): no son YAML.')
    print('  · los `.meta`, el `ProjectSettings/` y el `Library/`: fuera del ámbito.')
    print('  · el `DefaultVolumeProfile.asset` de URP y los `Settings/*.asset`: los')
    print('    escribe el paquete una vez, no el generador en cada pase.')
    print('  · un asset sin documentos, o con una cabecera que no se entiende: no se')
    print('    reescribe (y en --check se reporta, no se da por bueno en silencio).')
    print('  · NO decide si un cambio es deseable: sólo quita el ruido de las anclas.')
    return 0


# ── Selftest (sin repo) ─────────────────────────────────────────────────────

def selftest():
    rojo = 0

    def check(nombre, ok):
        nonlocal rojo
        print(('  ✓  ' if ok else '  ✗  ') + nombre)
        if not ok:
            rojo = 1

    base = ('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'
            '--- !u!1 &11\nGameObject:\n  m_Component:\n  - component: {fileID: 12}\n'
            '  m_Name: A\n'
            '--- !u!4 &12\nTransform:\n  m_GameObject: {fileID: 11}\n  m_Father: {fileID: 0}\n'
            '--- !u!114 &13\nMonoBehaviour:\n  m_Script: {fileID: 11500000, guid: abc, type: 3}\n'
            '--- !u!114 &99\nMonoBehaviour:\n  version: 10\n')

    c1, _ = canonical(base)
    c2, _ = canonical(c1)
    check('idempotente: canonical(canonical(x)) == canonical(x)', c1 == c2)

    header, docs = split_docs(base)
    shuffled = '\n'.join(header + [line for doc in reversed(docs) for line in doc])
    check('invariante al orden de los documentos', canonical(shuffled)[0] == c1)

    ren = canonical(base.replace('&11', '&5001').replace('&12', '&5002').replace('&13', '&5003')
                    .replace('{fileID: 12}', '{fileID: 5002}').replace('{fileID: 11}', '{fileID: 5001}'))
    check('invariante a renumerar las anclas viejas', ren[0] == c1)

    check('respeta las referencias externas (guid)', 'guid: abc, type: 3' in c1)
    check('respeta el null {fileID: 0}', '{fileID: 0}' in c1)

    anclas = [int(m) for m in re.findall(r'^--- !u!\d+ &(-?\d+)', c1, re.M)]
    locales = [int(m.group(1)) for m in REF.finditer(c1)
               if 'guid' not in m.group(2) and int(m.group(1)) != 0]
    check('anclas únicas', len(anclas) == len(set(anclas)))
    check('toda referencia local existe', set(locales) <= set(anclas))
    check('un documento por ancla', len(re.findall(r'^--- ', c1, re.M)) == len(docs))
    check('el orden de salida es ascendente por id', anclas == sorted(anclas))

    # El objeto principal de un asset mono-objeto NO se renumera: la escena lo
    # cita por su convención `clase × 100000`.
    mat = ('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'
           '--- !u!21 &2100000\nMaterial:\n  m_Name: X\n'
           '--- !u!114 &4056156942334081302\nMonoBehaviour:\n  version: 10\n')
    cmat, _ = canonical(mat)
    check('el objeto principal (clase × 100000) conserva su ancla', '&2100000' in cmat)

    # SceneRoots es intocable y cae al final por ser long.MaxValue.
    esc = ('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'
           '--- !u!1660057539 &9223372036854775807\nSceneRoots:\n  m_Roots:\n  - {fileID: 77}\n'
           '--- !u!4 &77\nTransform:\n  m_Father: {fileID: 0}\n')
    cesc, _ = canonical(esc)
    check('SceneRoots conserva long.MaxValue', '&9223372036854775807' in cesc)
    ultimo_ancla = [l for l in cesc.splitlines() if l.startswith('--- ')][-1]
    check('SceneRoots queda el último', ultimo_ancla.endswith('&9223372036854775807'))

    # Un sufijo `stripped` (instancia de prefab recortada) no se pierde.
    stripped = ('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'
                '--- !u!1001 &5 stripped\nPrefabInstance:\n  m_Name: P\n')
    check('conserva el sufijo `stripped`', canonical(stripped)[0].splitlines()[2].endswith(' stripped'))

    # Un documento con dos anclas iguales es un asset corrupto: falla, no adivina.
    dup = ('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'
           '--- !u!1 &7\nGameObject:\n  m_Name: A\n--- !u!4 &7\nTransform:\n  m_Father: {fileID: 0}\n')
    try:
        canonical(dup)
        check('dos documentos con la misma ancla ⇒ falla', False)
    except ValueError:
        check('dos documentos con la misma ancla ⇒ falla', True)

    # Y un fichero sin documentos no es un asset que canonicalizar.
    try:
        canonical('hola\n')
        check('un fichero sin documentos ⇒ falla', False)
    except ValueError:
        check('un fichero sin documentos ⇒ falla', True)

    if rojo == 0:
        print('✓ canonicalizador correcto (idempotente, invariante al orden y a las anclas viejas, anclas reservadas intactas)')
        return 0
    return 1


def main(argv=None):
    # La consola de Windows viene en cp1252 y estos mensajes llevan ✓/✗/·: sin
    # forzar UTF-8, el script se cae al imprimir su primer veredicto.
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass

    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument('--project', default=PROJECT)
    parser.add_argument('--check', action='store_true')
    parser.add_argument('--selftest', action='store_true')
    parser.add_argument('--list', action='store_true')
    parser.add_argument('--help', '-h', action='store_true')
    parser.add_argument('paths', nargs='*')
    args = parser.parse_args(argv)

    if args.help:
        print(__doc__.strip())
        return 0
    if args.list:
        return listar()
    if args.selftest:
        return selftest()

    if args.paths:
        rutas = list(args.paths)
        faltan = [p for p in rutas if not os.path.isfile(p)]
        if faltan:
            print('✗ no existe: ' + ', '.join(faltan), file=sys.stderr)
            return 2
    else:
        rutas = assets_del_proyecto(args.project)
        if rutas is None:
            print(f'✗ no encuentro {args.project}/Assets (¿es un proyecto de Unity?)', file=sys.stderr)
            return 2

    rc, reescritos, derivarían, fallos = proceso(rutas, args.check)
    if args.check:
        if rc == 0:
            print(f'✓ los {len(rutas)} assets del proyecto ya están en forma canónica')
        else:
            print(f'✗ {derivarían} de {len(rutas)} assets derivarían del canónico' +
                  (f' · {fallos} no se pudieron analizar' if fallos else ''))
        return rc
    if rc != 0:
        return rc
    print(f'✓ {len(rutas)} assets revisados · {reescritos} reescritos al canónico')
    return 0


if __name__ == '__main__':
    sys.exit(main())
