"""Equivalencia estructural de assets de Unity — ¿cambio o sólo churn?

POR QUÉ EXISTE. Abrir el proyecto, construir un player o dejar que el editor
guarde reescribe assets (escena, materiales, ajustes de URP) SIN cambiar nada de
lo que significan: reordena los documentos, renumera las anclas `&N` y mueve el
bloque `AssetVersion` de sitio. El diff resultante son miles de líneas que
esconden, si acaso, un cambio pequeño — o ninguno. Medido (2026-09-20): una pasada
del generador reescribió 64 assets del multi-visor; 60 eran idénticos y sólo 4
cambiaban de verdad. Revisar eso a ojo no se puede, y commitear el ruido contamina
la historia del repo para siempre.

CÓMO LO DECIDE. Cada asset YAML se parte en documentos (`--- !u!<clase> &<ancla>`)
y cada documento se canonicaliza sustituyendo sus REFERENCIAS LOCALES
(`{fileID: N}` sin `guid`) por la huella del documento al que apuntan, iterando
hasta el punto fijo: es una bisimulación sobre el grafo de referencias. Dos
versiones con los mismos objetos, los mismos campos y el MISMO CABLEADO dan las
mismas huellas aunque Unity renumere las anclas o reordene los documentos.

Lo que NO se normaliza, a propósito: nombres, guids externos, ni un solo escalar.
Un valor distinto —`m_Enabled: 1 → 0`, un `m_AnchorMin` que se mueve, una
referencia que apunta a otro objeto— cambia la huella y sale como DIFF. Lo ÚNICO
aparte de eso que se ignora son los finales de línea: este repo se desarrolla en
Windows (CRLF) y su CI corre en Linux (LF), y sin eso cada asset que tocara el
editor saldría «CAMBIA» por su `\r`. Si no se puede analizar (binario, sin
documentos), la respuesta es DIFF: fallar cerrado, nunca un verde por no haber
mirado.

LA EXCEPCIÓN: EL CHURN QUE DEJA EL ASSET CANÓNICO. Un cambio equivalente (mismo
contenido, otra forma) no siempre es ruido. Si el asset estaba commiteado en una
forma NO canónica —el caso real: `PheromoneMat.mat` con el bloque `AssetVersion`
delante y un ancla de sesión—, entonces el editor lo va a reescribir en su forma
canónica en CADA pasada: el «churn» se repite para siempre y descartarlo deja la
trampa puesta. Ese cambio —el que deja el asset en la forma que
documenta scripts/unity-canonicalize-assets.py— se acepta y se etiqueta
`CANONICALIZA`; el churn que sigue siendo ruido (re-anclar, reordenar) se rechaza
como siempre. Sin el canonicalizador a mano, la excepción no se aplica y todo
vuelve al criterio conservador (churn).

DESCARTAR LA FORMA (`--discard`). El veredicto también ACTÚA, y quita la forma
por los dos caminos que hacen falta para que el diff contra REV sea lo que
cambió:

  · los IDÉNTICO (churn puro: mismo contenido, otras anclas y otro orden) se
    restauran a REV con `git checkout REV -- RUTAS`: exacto y gratis.
  · los CAMBIA (cambio de contenido de verdad) NO se restauran —eso perdería el
    cambio—, así que se reescriben en su forma CANÓNICA conservando el contenido
    (`quitar_forma`, con el MISMO canonicalizador que usa el generador). Es una
    operación sin pérdida y funciona porque las anclas canónicas son el HASH DEL
    CONTENIDO: un documento que no cambió vuelve a ser el mismo byte que en REV
    aunque la pasada le hubiera puesto otra ancla, así que el diff se queda con
    los documentos que sí cambiaron (y los que los referencian, que cambian de
    ancla con ellos).
  · los CANONICALIZA (los que quitan la trampa) tampoco se tocan: descartarlos
    volvería a ponerla, y los que el canonicalizador no sabe leer —un `.meta` no
    tiene documentos— se avisan y se dejan como están en vez de escribirlos con
    cualquier cosa.

Antes de escribir, la forma canónica tiene que pasar el `compare` de este mismo
fichero contra lo que había en el árbol —el veredicto que decide el churn—: si no
significa lo mismo (o el asset no analiza), no se escribe y lo dice. Fallar
cerrado, nunca reescribir un contenido que no se entiende. Y después de actuar
vuelve a clasificar y a mirar el índice para comprobar lo que acaba de afirmar:
el churn ya no está tocado, cada CAMBIA sigue cambiando y quedó canónico, y el
árbol coincide con el índice; si algo falla, lo dice y sale con 4 (nada se da por
bueno sin volver a mirarlo). El diff contra REV se MIDE antes y después de cada
ruta, para que la reducción sea una cifra y no una promesa.

Uso:
  python unity-asset-equivalence.py                    # los assets que el árbol tiene tocados, contra HEAD
  python unity-asset-equivalence.py --rev REV RUTAS…   # esas rutas, contra REV
  python unity-asset-equivalence.py --discard          # quita la FORMA del árbol (churn y cambios de verdad)
  python unity-asset-equivalence.py --discard --dry-run # …pero sin tocar nada: sólo dice qué haría
  python unity-asset-equivalence.py --churn            # el churn de un rango de commits de git
  python unity-asset-equivalence.py --churn --range A..B
  python unity-asset-equivalence.py --selftest         # verifica el analizador (sin repo)
  python unity-asset-equivalence.py --list             # qué se comprueba y qué no

En el repo se llama por el envoltorio `scripts/check-unity-assets.sh`, que resuelve
el intérprete de Python; este fichero es el que tiene el algoritmo.

En modo `--churn` (el de CI) el rango por defecto lo resuelve él solo: la variable
`CI_RANGE` del workflow si trae dos extremos utilizables, y si no `HEAD~1..HEAD`.
Un commit dentro del rango que toque un asset de Unity y no cambie NADA de lo que
significa es un fallo: la salida dice exactamente qué ficheros descartar.

Salida: 0 sin nada que reprochar · 1 diferencias (o churn en el rango, o assets
pendientes: churn que descartar o cambios de verdad que revisar) · 2 uso · 3 sin
git para las partes que lo necesitan · 4 con `--discard`, la comprobación
posterior al descarte falló: el churn seguía tocado, un CAMBIA perdió su cambio,
o el índice se quedó con la forma vieja (no se da por bueno).
"""
import argparse
import collections
import contextlib
import hashlib
import io
import os
import re
import shutil
import subprocess
import sys
import tempfile

# Ámbito del escaneo de churn: el proyecto Unity. Un asset fuera de aquí no lo
# reescribe el editor al abrir el proyecto, así que no es este problema.
SCOPE = 'src/App/AntSim.Unity'

# Y sólo los ASSETS: bajo el proyecto Unity también viven el README y los .cs, y
# un .cs no es churn de re-serialización — lo que cuenta ahí es su contenido, que
# git ya enseña tal cual. Medido al probar el verificador: sin este filtro, el
# propio README de Unity salía como «CAMBIA … sin documentos que analizar».
ASSET_SUFFIXES = ('.mat', '.unity', '.asset', '.prefab', '.controller', '.anim', '.meta')


def es_asset(path):
    return path.lower().endswith(ASSET_SUFFIXES)


# ── ¿el texto YA es la forma canónica? ──────────────────────────────────────
# La excepción del churn necesita la MISMA definición de «canónico» que usa quien
# lo escribe (scripts/unity-canonicalize-assets.py), no una copia que se
# desincronice: se importa el script hermano por ruta (su nombre lleva guiones)
# una sola vez. Si no se puede importar, la excepción no se aplica —lado
# conservador— y el verificador sigue funcionando igual.
_CANON = None
_CANON_BUSCADO = False


def _canonicalizador():
    global _CANON, _CANON_BUSCADO
    if _CANON_BUSCADO:
        return _CANON
    _CANON_BUSCADO = True
    try:
        import importlib.util
        ruta = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                            'unity-canonicalize-assets.py')
        spec = importlib.util.spec_from_file_location('unity_canonicalize_assets', ruta)
        modulo = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(modulo)
        _CANON = modulo
    except Exception:
        _CANON = None
    return _CANON


def es_canonico(texto):
    """¿este texto YA es la forma canónica? `None` si no se puede decidir (sin
    canonicalizador, o un fichero que no analiza)."""
    canon = _canonicalizador()
    if canon is None or texto is None:
        return None
    try:
        return canon.canonical(texto)[0] == texto
    except Exception:
        return None

ANCHOR = re.compile(r'^--- !u!(\d+) &(-?\d+)( stripped)?\s*$')
REF = re.compile(r'\{fileID: (-?\d+)([^}]*)\}')
CEROS = '0' * 40


# ── El analizador (puro: texto entra, huellas salen) ─────────────────────────

def normaliza(text):
    """Finales de línea a \\n. NO es cosmética: el repo se desarrolla en Windows
    (que escribe CRLF al guardar) y CI corre en Linux con LF, así que sin esto
    cada asset que tocara el editor saldría como «CAMBIA» por su `\\r` — inútil.
    Un fichero cuyo ÚNICO cambio sean los finales de línea es churn, y así se
    reporta."""
    return text.replace('\r\n', '\n').replace('\r', '\n')


def parse(text):
    """Los documentos de un asset, con el cuerpo canonicalizado.

    El hueco en blanco del final del fichero cae en el ÚLTIMO documento, y ese
    documento cambia si Unity reordena: sin recortarlo, un reordenamiento daría un
    DIFF falso (medido: 36/64 «idénticos» sin recortar, 60/64 con el recorte)."""
    docs, cur = [], None
    for line in normaliza(text).split('\n'):
        match = ANCHOR.match(line)
        if match:
            cur = {'cls': match.group(1), 'anchor': match.group(2), 'body': []}
            docs.append(cur)
        elif cur is not None:
            cur['body'].append(line)
    for doc in docs:
        while doc['body'] and not doc['body'][-1].strip():
            doc['body'].pop()
    return docs


def _canon(line, resolver):
    return REF.sub(lambda m: resolver(m.group(1), m.group(2)), line)


def fingerprints(docs, rounds=20):
    """Huella por documento, iterada hasta el punto fijo: la referencia local se
    sustituye por la huella de su destino, así que el mismo cableado da la misma
    huella aunque las anclas se renumeren."""
    by_anchor = {}
    for i, doc in enumerate(docs):
        by_anchor.setdefault(doc['anchor'], i)
    sig = [None] * len(docs)
    for _ in range(rounds):
        new = []
        for doc in docs:
            def resolver(fid, rest):
                if 'guid:' in rest:
                    return '{fileID:@ext' + rest + '}'
                if fid == '0':
                    return '{fileID:@null' + rest + '}'
                target = by_anchor.get(fid)
                # Una referencia huérfana (a un ancla que no está) es una
                # diferencia: no se puede afirmar que el cableado sea el mismo.
                if target is None:
                    return '{fileID:@huerfano' + rest + '}'
                return '{fileID:@' + (sig[target] or '?') + '}'
            body = '\n'.join(_canon(line, resolver) for line in doc['body'])
            new.append(hashlib.sha256((doc['cls'] + '\n' + body).encode('utf-8')).hexdigest()[:16])
        if new == sig:
            break
        sig = new
    return sig


def multiset(docs):
    return collections.Counter(zip([d['cls'] for d in docs], fingerprints(docs)))


def compare(old_text, new_text):
    """(equivalente, detalle). `detalle` explica la diferencia cuando la hay."""
    old_docs, new_docs = parse(old_text), parse(new_text)
    if not old_docs and not new_docs:
        if normaliza(old_text) == normaliza(new_text):
            return True, 'sin documentos y texto igual (finales de línea aparte)'
        return False, 'sin documentos que analizar: no se puede afirmar equivalencia'
    old, new = multiset(old_docs), multiset(new_docs)
    if old == new:
        detalle = f'{len(new_docs)} documentos, mismo multiconjunto'
        # Mismo contenido pero texto distinto ⇒ sólo cambia la FORMA, y conviene
        # decir CUÁL, porque las dos cosas se ven muy distintas según quién mire:
        #  · finales de línea — el editor en Windows escribe CRLF y CI tiene LF. Con
        #    el git de Linux, un árbol con CRLF aparece LLENO de assets «modificados»
        #    que no cambian nada; sin decirlo, el veredicto parecería un invento.
        #  · blancos de más o de menos — lo que `parse` ya recorta en los bordes.
        if normaliza(old_text) != normaliza(new_text):
            detalle += ' · la diferencia son los blancos de los bordes'
        elif old_text != new_text:
            detalle += ' · la diferencia son sólo los finales de línea'
        return True, detalle
    extra = new - old
    falta = old - new
    trozos = []
    if extra:
        trozos.append('+' + ' '.join(f'{n}×clase {cls} {sig}' for (cls, sig), n in list(extra.items())[:4]))
    if falta:
        trozos.append('-' + ' '.join(f'{n}×clase {cls} {sig}' for (cls, sig), n in list(falta.items())[:4]))
    return False, f'{len(old_docs)} → {len(new_docs)} documentos · ' + ' · '.join(trozos)


# ── Git ─────────────────────────────────────────────────────────────────────

# El `cwd` se pasa SIEMPRE explícito en las partes que lo necesitan: el selftest
# levanta un repo de prueba, y sin él los comandos se ejecutarían en el repo del
# usuario — con el resultado de escanear el historial equivocado y salir verde
# (lo destapó el propio selftest: el caso del commit que sólo re-ancla).
def git(*args, cwd=None):
    return subprocess.run(['git'] + list(args), capture_output=True, check=False, cwd=cwd)


def git_ok(*args, cwd=None):
    proc = git(*args, cwd=cwd)
    if proc.returncode != 0:
        return None
    return proc.stdout.decode('utf-8', 'replace')


def have_git():
    return shutil.which('git') is not None


def touched_assets(cwd=None):
    """Las rutas de Unity que el árbol de trabajo tiene tocadas (modificadas,
    borradas o nuevas), con su código de estado."""
    out = git_ok('status', '--porcelain', '--', SCOPE, cwd=cwd)
    if out is None:
        return []
    pares = []
    for line in out.split('\n'):
        if len(line) < 4:
            continue
        estado, path = line[:2], line[3:].strip().strip('"')
        # Un renombrado llega como «viejo -> nuevo»: interesa el destino.
        if ' -> ' in path:
            path = path.split(' -> ', 1)[1].strip().strip('"')
        if path.startswith(SCOPE) and es_asset(path):
            pares.append((estado, path))
    return pares


def show(rev, path, cwd=None):
    return git_ok('show', f'{rev}:{path}', cwd=cwd)


def read_worktree(path):
    try:
        with open(path, 'rb') as handle:
            return handle.read().decode('utf-8', 'replace')
    except OSError:
        return None


# ── Modo 1: ¿los assets tocados del árbol son ruido? ────────────────────────

def clasificar(rev, paths, cwd=None):
    """(ruido, canoniza, reales, saltados): en qué cae cada ruta contra REV.

    No imprime nada: el informe lo pone el llamante. Es el MISMO veredicto que
    usa todo el fichero —`compare` decide y la excepción del canónico separa el
    churn que deja la trampa del que la quita—, y lo comparten el informe y el
    descarte para que no puedan discrepar.

    `cwd` es el repo sobre el que mirar (por defecto, el del proceso): lo usa el
    selftest para ejercitar esto sobre un repo de prueba, igual que `churn`.
    """
    ruido, canoniza, reales, saltados = [], [], [], []
    for path in paths:
        old = show(rev, path, cwd=cwd)
        new = read_worktree(path if cwd is None else os.path.join(cwd, path))
        if old is None:
            saltados.append((path, 'nuevo: no hay nada que comparar'))
            continue
        if new is None:
            saltados.append((path, 'borrado en el árbol de trabajo'))
            continue
        same, detalle = compare(old, new)
        if not same:
            reales.append((path, detalle))
        elif es_canonico(new) and es_canonico(old) is False:
            # El churn que deja el asset CANÓNICO no es ruido (ver el encabezado):
            # es el que quita la trampa de un asset commiteado en forma no canónica.
            canoniza.append((path, detalle))
        else:
            ruido.append((path, detalle))
    return ruido, canoniza, reales, saltados


def informe(ruido, canoniza, reales, saltados):
    """La línea por fichero de los cuatro grupos, siempre en el mismo orden."""
    for path, detalle in rumbo_ordenado(ruido):
        print(f'IDÉNTICO  {path}  ({detalle})')
    for path, detalle in canoniza:
        print(f'CANONICALIZA  {path}  ({detalle}: queda en la forma canónica del editor)')
    for path, detalle in reales:
        print(f'CAMBIA    {path}  ({detalle})')
    for path, motivo in saltados:
        print(f'SALTADO   {path}  ({motivo})')


def revisar(rev, paths):
    ruido, canoniza, reales, saltados = clasificar(rev, paths)
    informe(ruido, canoniza, reales, saltados)

    total = len(ruido) + len(reales) + len(saltados) + len(canoniza)
    print()
    if total == 0:
        print(f'✓ ningún asset de Unity tocado en el árbol de trabajo ({SCOPE})')
        return 0
    print(f'{len(ruido)} idénticos (churn) · {len(canoniza)} que canonicalizan · '
          f'{len(reales)} con cambio real · {len(saltados)} saltados, de {total}')
    if ruido:
        print(f'✅ los {len(ruido)} idénticos son churn: `--discard` los descarta sin perder nada')
        print('   (a mano, equivalen a: git checkout ' + rev + ' -- ...)')
    if canoniza:
        print('✅ los que canonicalizan SÍ se commitean (no los descartes): son los que')
        print('   dejan el árbol en la forma que el editor escribe, y el churn de esas')
        print('   rutas se acaba con el commit:')
        print('   git add -- ' + ' '.join(p for p, _ in rumbo_ordenado(canoniza)))
    if reales:
        print(f'✗ hay {len(reales)} asset(s) con cambio de verdad: revísalos antes de commitear')
        print(f'   (`--discard` les quita la forma sin tocar el contenido: su diff contra {rev} se')
        print('   queda en lo que cambia de verdad)')
        return 1
    if canoniza:
        print(f'→ {len(canoniza)} asset(s) quedan en forma canónica: commitéalos para cerrar el churn')
        return 1
    return 0


def rumbo_ordenado(pares):
    return sorted(pares, key=lambda p: p[0])


# ── Cuánto diff hay, y cuánto quedaría (git, no una segunda implementación) ──

def _lineas_cambiadas(diff):
    """Las líneas que un diff añade o quita: sin cabeceras `+++`/`---` ni contexto.
    Es la cifra con la que se informa de la reducción."""
    if diff is None:
        return None
    return sum(1 for linea in diff.split('\n')
               if linea[:1] in '+-' and not linea.startswith(('+++', '---')))


def medir_diff(rev, path, cwd=None):
    """Cuántas líneas de diff hay AHORA entre REV y esa ruta del árbol."""
    return _lineas_cambiadas(git_ok('diff', '--no-color', rev, '--', path, cwd=cwd))


def medir_diff_si_quedara(rev, path, texto, cwd=None):
    """Cuántas líneas tendría ese diff si la ruta quedara con `texto`.

    Diffea el texto de REV contra él con `git diff --no-index` sobre dos ficheros
    temporales: el MISMO diff que luego se cuenta —y no una segunda
    implementación de «cuánto cambia esto», que podría discrepar—, sin tocar el
    árbol. `--no-index` sale con 1 cuando hay diferencias (que es el caso), así
    que se lee su salida sin exigir rc 0.
    """
    viejo = show(rev, path, cwd=cwd)
    if viejo is None:
        return None
    with tempfile.TemporaryDirectory() as tmp:
        for nombre, contenido in (('antes', viejo), ('despues', texto)):
            # Barras normales: en Windows git acepta `C:/…` sin ambigüedad, y con
            # `\` una ruta podía leerse como una secuencia de escape.
            destino = os.path.join(tmp, nombre).replace(os.sep, '/')
            with open(destino, 'w', encoding='utf-8', newline='') as fh:
                fh.write(contenido)
        salida = git('diff', '--no-color', '--no-index',
                     os.path.join(tmp, 'antes').replace(os.sep, '/'),
                     os.path.join(tmp, 'despues').replace(os.sep, '/'), cwd=cwd)
        return _lineas_cambiadas(salida.stdout.decode('utf-8', 'replace'))


def medida_del_diff(rev, antes, despues):
    """Cómo se informa de la reducción: «el diff contra REV: 412 → 3 líneas»."""
    if antes is None or despues is None:
        return f'el diff contra {rev} no lo pude medir'
    return f'el diff contra {rev}: {antes} → {despues} líneas'


def suma_al_indice(path, cwd=None):
    """¿Esta ruta tiene cambios AÑADIDOS al índice? `git diff --cached --quiet`
    lo dice en el código de salida (1 cuando hay diferencia) y no imprime nada."""
    return git('diff', '--cached', '--quiet', '--', path, cwd=cwd).returncode == 1


def quitar_forma(rev, path, dry_run=False, cwd=None):
    """Deja la ruta en su forma CANÓNICA sin tocar el contenido.

    Es el otro extremo del descarte: a un asset que cambia de VERDAD no se le
    puede devolver REV —eso perdería el cambio—, pero sí quitarle la forma que le
    puso la pasada del editor (anclas de sesión, orden, blancos de sobra) para
    que el diff contra REV quede reducido a lo que cambió. Se canonicaliza con el
    MISMO script que usa el generador: no hay dos definiciones de «canónico». Y
    funciona porque las anclas canónicas son el hash del contenido —un documento
    intacto vuelve a ser el mismo byte que en REV aunque la pasada le hubiera
    puesto otra ancla—, así que el diff se queda con los documentos que sí
    cambiaron y con los que los referencian.

    NO ESCRIBE si no puede demostrar que la forma canónica significa lo mismo: se
    canonicaliza el texto del árbol y se le pasa el `compare` de este mismo
    fichero —el veredicto que decide el churn— contra el original. Si no
    equivalen, se planta y lo dice; y si el fichero no es de los que el
    canonicalizador sabe leer (un `.meta`), tampoco escribe: lo avisa y lo deja.
    Fallar cerrado, nunca reescribir un contenido que no se entiende.

    Devuelve (estado, detalle, antes, despues): `estado` es 'hecho' cuando se
    reescribió (o se reescribiría, en `dry_run`), 'ya' cuando no había nada que
    quitarle, 'sin-canonicalizador' cuando no hay con qué decidir la forma,
    'ajeno' cuando el fichero no es de los que el canonicalizador sabe leer (un
    `.meta`, por ejemplo: no tiene documentos, y no hay «forma canónica» que
    ponerle) —esos dos no son culpa del asset ni un fallo del descarte— y 'no'
    cuando se negó a escribir por algo que sí lo es: la forma canónica no
    significa lo mismo que lo que hay. Y los dos recuentos de líneas de diff
    contra REV (el de ahora, y el de después —o la previsión, en `dry_run`—).
    """
    real = path if cwd is None else os.path.join(cwd, path)
    texto = read_worktree(real)
    if texto is None:
        return 'no', 'no se pudo leer el fichero', None, None
    canon = _canonicalizador()
    if canon is None:
        return 'sin-canonicalizador', 'sin el canonicalizador no sé cuál es la forma canónica', None, None
    try:
        nuevo, ndocs = canon.canonical(texto)
    except Exception as exc:
        # No es un fallo: hay ficheros con sufijo de asset que el canonicalizador no
        # toca por diseño (un `.meta` no tiene documentos `--- !u!`). No hay forma
        # canónica que ponerles, así que se dejan como están y se dice.
        return 'ajeno', f'el canonicalizador no lo analiza ({exc})', None, None
    if nuevo == texto:
        n = medir_diff(rev, path, cwd=cwd)
        return 'ya', 'ya estaba en forma canónica: no había forma que quitarle', n, n
    same, detalle = compare(texto, nuevo)
    if not same:
        # AQUÍ no se escribe: lo que sale del canonicalizador no significa lo
        # mismo que lo que había, así que reescribirlo se llevaría contenido por
        # delante. Se dice y se sigue; el llamante lo cuenta como fallo.
        return 'no', f'la forma canónica NO equivale a lo que hay ({detalle})', None, None
    antes = medir_diff(rev, path, cwd=cwd)
    if dry_run:
        return 'hecho', f'{ndocs} documentos, mismo contenido', antes, \
            medir_diff_si_quedara(rev, path, nuevo, cwd=cwd)
    with open(real, 'w', encoding='utf-8', newline='') as fh:
        fh.write(nuevo)
    # El índice, si la ruta estaba AÑADIDA: dejar el árbol sin forma y el índice
    # con ella volvería a meter el churn en el commit —el mismo motivo por el que
    # el descarte del churn también toca el índice—. Si no estaba añadida, no se
    # añade: no se le mete a git trabajo que nadie había preparado.
    if suma_al_indice(path, cwd=cwd):
        git('add', '--', path, cwd=cwd)
    return 'hecho', f'{ndocs} documentos, mismo contenido', antes, medir_diff(rev, path, cwd=cwd)


# ── Modo 1bis: descartar la FORMA del árbol ─────────────────────────────────

def descartar(rev, paths, dry_run=False, cwd=None):
    """Quita la forma de las rutas tocadas: la del churn entero, y la de los que
    cambian de verdad también.

    Dos casos, un mismo objetivo —que el diff contra REV sea lo que cambió, y no
    la forma con la que la pasada del editor lo escribió—:

      · los IDÉNTICO (churn puro: mismo contenido, anclas y orden aparte) se
        restauran a REV, que es exacto y gratis;
      · los CAMBIA (cambio de contenido de verdad) NO se pueden restaurar —se
        perdería el cambio—, así que se reescriben en su forma canónica
        conservando el contenido (`quitar_forma`). Sin esto habría que commitear
        el cambio con su churn dentro, o revisarlo a ojo entre miles de líneas.

    No se toca ninguno más: los CANONICALIZA porque descartarlos repondría la
    trampa que quitan, y los SALTADO (nuevos o borrados) porque no hay nada que
    comparar.

    Restaura con `git checkout REV -- RUTAS`: deja la copia de REV en el índice y
    en el árbol. En el índice también a propósito: si el churn estaba AÑADIDO a
    mano (`git add`), dejarlo staged sería descartar sólo la mitad.

    Después de actuar no se fía: vuelve a clasificar y mira el índice para
    comprobar lo que acaba de afirmar —el churn ya no está tocado, cada CAMBIA
    sigue cambiando y quedó canónico, y el árbol coincide con el índice—, y si
    algo falla lo dice y sale con 4.

    `--dry-run` no toca nada: dice lo que haría, con la reducción de diff medida
    de verdad (se canonicaliza en memoria y se diffea contra REV).
    """
    # Sólo se descarta lo que git ve CAMBIADO: una ruta ya igual a REV no es
    # churn pendiente, y pedirle a git que la restaure no haría nada. Cuando las
    # rutas salen de `touched_assets` esto no quita ninguna; importa para las
    # rutas que se pasan a mano.
    sucias = {p for _, p in touched_assets(cwd=cwd)}
    pendientes = [p for p in paths if p in sucias]

    ruido, canoniza, reales, saltados = clasificar(rev, pendientes, cwd=cwd)
    informe(ruido, canoniza, reales, saltados)
    print()

    rutas = [p for p, _ in rumbo_ordenado(ruido)]
    cambiar = [p for p, _ in rumbo_ordenado(reales)]

    if not rutas and not cambiar:
        print(f'✓ no hay churn que descartar: ningún asset tocado es sólo re-serialización')
        if canoniza:
            print(f'  ({len(canoniza)} quedan en forma canónica: ésos SÍ se commitean)')
        return 0

    if dry_run:
        if rutas:
            print(f'• {len(rutas)} asset(s) tocados SÓLO en la forma: se descartarían sin perder nada')
            print(f'    git checkout {rev} -- ' + ' '.join(rutas))
        if cambiar:
            print(f'• {len(cambiar)} con cambio de verdad: se les quitaría la forma (anclas y orden),')
            print(f'  conservando el contenido, así que su diff contra {rev} quedaría en lo que cambia:')
            for path in cambiar:
                estado, detalle, antes, despues = quitar_forma(rev, path, dry_run=True, cwd=cwd)
                if estado in ('hecho', 'ya'):
                    print(f'    {path}: {medida_del_diff(rev, antes, despues)} ({detalle})')
                elif estado in ('sin-canonicalizador', 'ajeno'):
                    print(f'    {path}: ni se tocaría ({detalle})')
                else:
                    print(f'    {path}: ⚠ NO se tocaría ({detalle})')
        print('  nada tocado todavía: repite sin --dry-run para hacerlo de verdad')
        return 1

    fallos = []
    if rutas:
        proc = git('checkout', rev, '--', *rutas, cwd=cwd)
        if proc.returncode != 0:
            motivo = proc.stderr.decode('utf-8', 'replace').strip() or 'sin detalle'
            print(f'✗ no se pudo descartar (git checkout {rev}, rc={proc.returncode}): {motivo}',
                  file=sys.stderr)
            return 4

        # Comprobación, no confianza: que git deje de verlos tocados. Si el descarte
        # no lo consiguió —un modo de fichero, un filtro de eol, lo que sea— se dice y
        # se falla; no se da por hecho que el `git checkout` hizo lo que dice.
        out = git_ok('status', '--porcelain', '--', *rutas, cwd=cwd)
        restos = [line for line in (out or '').split('\n') if line.strip()]
        if out is None or restos:
            print('✗ tras descartar, git sigue viendo tocado lo descartado:', file=sys.stderr)
            for line in restos:
                print('  ' + line, file=sys.stderr)
            return 4

        for path in rutas:
            print(f'DESCARTADO  {path}  (vuelve a {rev}: sólo se iba en anclas y orden)')

    # Los que cambian de verdad: fuera la forma, dentro el contenido. Cada uno se
    # informa con su propia medida, antes y después, para que la reducción sea un
    # número y no una promesa.
    medidos = []
    for path in cambiar:
        estado, detalle, antes, despues = quitar_forma(rev, path, cwd=cwd)
        if estado in ('sin-canonicalizador', 'ajeno'):
            # O no hay con qué decidir la forma, o el fichero no es de los que se
            # canonicalizan (un `.meta`): se avisa, se deja como está en vez de
            # reescribirlo con algo que no se entiende, y NO es un fallo del
            # descarte —es del entorno, o del tipo de fichero—.
            print(f'⚠ sin quitarle la forma a {path}: {detalle}', file=sys.stderr)
            continue
        if estado not in ('hecho', 'ya'):
            fallos.append((path, detalle))
            continue
        medidos.append((path, antes, despues, estado == 'hecho'))
        etiqueta = 'SIN FORMA  ' if estado == 'hecho' else 'YA CANÓNICO'
        print(f'{etiqueta}  {path}  ({detalle}; {medida_del_diff(rev, antes, despues)})')

    # Y la segunda mirada, ahora sobre lo que se acaba de afirmar. Un CAMBIA que
    # después de quitarle la forma ya no cambie nada significa que la forma
    # canónica se llevó el contenido por delante; y un árbol que no coincide con
    # el índice significa que el commit se llevaría la forma vieja.
    nuevas = [path for path, _, _, cambio in medidos if cambio]
    despues_de = clasificar(rev, nuevas, cwd=cwd)
    aun_reales = {p for p, _ in despues_de[2]}
    for path in nuevas:
        real = path if cwd is None else os.path.join(cwd, path)
        if path not in aun_reales:
            fallos.append((path, 'ya no cambia nada contra REV: la forma se llevó el cambio'))
        elif es_canonico(read_worktree(real)) is not True:
            fallos.append((path, 'sigue sin estar en forma canónica'))
        estado = git_ok('status', '--porcelain', '--', path, cwd=cwd) or ''
        linea = next((l for l in estado.split('\n') if l.strip()), '')
        # `XY`: X = índice contra HEAD, Y = árbol contra índice. Un cambio sin
        # añadir es «·M» y es lo normal; «MM» significa que la forma vieja se quedó
        # AÑADIDA y el commit se llevaría justo el churn que se acaba de quitar.
        if len(linea) > 1 and linea[0] not in (' ', '?') and linea[1] != ' ':
            fallos.append((path, 'el índice se quedó con la forma vieja: hay una versión'
                                 ' antigua AÑADIDA y el árbol ya no es ésa'))

    if fallos:
        print('✗ la comprobación del descarte falló:', file=sys.stderr)
        for path, motivo in fallos:
            print(f'  {path}: {motivo}', file=sys.stderr)
        return 4

    # La reducción, medida. Cuando no la hay es porque REV no está en forma
    # canónica (el asset commiteado mal): se dice, en vez de dar por hecho que
    # quitar la forma siempre acorta el diff.
    sin_reducir = [(p, a, d) for p, a, d, cambio in medidos
                   if cambio and a is not None and d is not None and d >= a]
    print()
    if rutas or medidos:
        if rutas:
            print(f'✓ {len(rutas)} asset(s) de re-serialización descartados de {len(pendientes)} tocados')
        else:
            print(f'✓ ningún asset tocado era sólo re-serialización: no había churn que descartar')
    if medidos:
        ya = len(medidos) - len(nuevas)
        como = f'{len(nuevas)} sin forma' + (f' y {ya} ya canónico(s)' if ya else '')
        print(f'  · {len(medidos)} con cambio de verdad ({como}): su diff contra {rev} es ya lo')
        print('    que cambia — revísalo y committéalo')
    if canoniza:
        print(f'  · {len(canoniza)} quedan en forma canónica: ésos SÍ se commitean (cierra el churn)')
    if sin_reducir:
        print(f'  · en {len(sin_reducir)} asset(s) el diff contra {rev} no se reduce todavía:')
        for path, antes, despues in sin_reducir:
            print(f'      {path}: {antes} → {despues} líneas')
        print(f'    {rev} no está en forma canónica (el caso del asset commiteado mal): el asset queda')
        print('    en la forma que escribe el editor, y commitear eso es lo que cierra su churn')
    if reales:
        return 1
    return 0


# ── Modo 2: el churn de un rango de commits ─────────────────────────────────

def resolver_rango(explicito, cwd=None):
    """El rango a escanear, con el motivo cuando no se puede usar uno.

    `--range` es una orden: si no sirve, se falla. `CI_RANGE` es lo que el evento
    declare, y no siempre trae los dos extremos (un PR no lo trae; el primer push
    de una rama manda 40 ceros como «antes»), así que ahí se cae al último commit
    y se dice con qué se midió — nunca un verde por no haber mirado."""

    def usable(spec):
        if not spec or '..' not in spec:
            return None, f'«{spec}» no tiene forma A..B'
        izq, _, der = spec.partition('..')
        if not izq or not der or izq == CEROS or der == CEROS:
            return None, f'«{spec}» sin extremos utilizables'
        for rev in (izq, der):
            if git_ok('rev-parse', '--verify', '--quiet', rev + '^{commit}', cwd=cwd) is None:
                return None, f'la revisión «{rev}» no está en este clon'
        return f'{izq}..{der}', None

    if explicito is not None:
        spec, motivo = usable(explicito)
        return (spec, None) if spec else (None, f'--range {motivo}')

    aviso = None
    if os.environ.get('CI_RANGE'):
        spec, motivo = usable(os.environ['CI_RANGE'])
        if spec:
            return spec, None
        aviso = f'CI_RANGE {motivo}: se escanea el último commit'

    if git_ok('rev-parse', '--verify', '--quiet', 'HEAD~1^{commit}', cwd=cwd) is not None:
        return 'HEAD~1..HEAD', aviso
    return None, 'no hay historial que escanear (clone superficial sin HEAD~1)'


def churn(rango, cwd=None):
    spec, aviso = resolver_rango(rango, cwd=cwd)
    if spec is None:
        print(f'• sin rango que escanear: {aviso}')
        # Un rango PEDIDO a mano que no sirve es un error de uso, no un verde.
        return 2 if rango is not None else 0
    if aviso:
        print(f'• {aviso}')

    commits = git_ok('rev-list', '--reverse', spec, cwd=cwd)
    if commits is None:
        print(f'• «{spec}» no es un rango válido en este clon')
        return 0
    commits = [c for c in commits.split() if c]
    print(f'▶ Churn de re-serialización en {spec} ({len(commits)} commit(s), ámbito {SCOPE})')

    sucios, canonizados = [], []
    for commit in commits:
        asunto = (git_ok('log', '-1', '--format=%h %s', commit, cwd=cwd) or '').strip()
        tocados = git_ok('diff-tree', '--no-commit-id', '--name-only', '--diff-filter=M', '-r',
                         f'{commit}^', commit, '--', SCOPE, cwd=cwd)
        if tocados is None:
            print(f'• {asunto}: sin padre que comparar (primer commit)')
            continue
        for path in [p for p in tocados.split('\n') if p.strip() and es_asset(p.strip())]:
            old = show(f'{commit}^', path, cwd=cwd)
            new = show(commit, path, cwd=cwd)
            if old is None or new is None:
                continue
            same, _ = compare(old, new)
            if not same:
                continue
            # La EXCEPCIÓN (ver el encabezado): el cambio equivalente que DEJA el
            # asset en su forma canónica no es ruido, es el que cierra el churn
            # recurrente de un asset commiteado mal. Un re-anclado cualquiera, no.
            if es_canonico(new) and es_canonico(old) is False:
                canonizados.append((asunto, path))
            else:
                sucios.append((asunto, path))

    if canonizados:
        print()
        for asunto, path in canonizados:
            print(f'CANONICALIZA  {path}\n    en {asunto}  (queda en la forma que el editor escribe: no es ruido)')
    if not sucios:
        extra = f' · {len(canonizados)} asset(s) canonicalizado(s), que sí entran' if canonizados else ''
        print(f'✓ ningún commit de {spec} mete ruido de re-serialización{extra}')
        return 0
    print()
    for asunto, path in sucios:
        print(f'✗ {path}\n    en {asunto}')
    print()
    print(f'✗ {len(sucios)} asset(s) de Unity entraron en el rango sin cambiar nada de lo que')
    print('  significan (sólo anclas, orden de documentos o el bloque AssetVersion).')
    print('  Descártalos del commit: el editor los reescribirá igual la próxima vez.')
    print('  (Uno que quedara en forma canónica no se cuenta aquí: eso es CANONICALIZA.)')
    return 1


# ── Modo 3: el selftest (sin repo) ──────────────────────────────────────────

DOC_114 = """--- !u!114 &{ancla}
MonoBehaviour:
  m_ObjectHideFlags: {flags}
  m_Script: {{fileID: 11500000, guid: aaaa, type: 3}}
  m_Name: Pinta
  m_GameObject: {{fileID: {dueno}}}
"""

DOC_1 = """--- !u!1 &{ancla}
GameObject:
  m_Name: {nombre}
  m_Component:
  - component: {{fileID: {hijo}}}
"""


def asset(*docs):
    return '%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n' + ''.join(docs)


def selftest():
    fails = 0

    def check(nombre, ok, detalle=''):
        nonlocal fails
        if ok:
            print(f'  ✓ {nombre}')
        else:
            print(f'  ✗ {nombre} {detalle}')
            fails = 1

    # 1. El caso que motivó la herramienta: mismos documentos, anclas renumeradas,
    #    bloques en otro orden y el hueco final cayendo en otro documento.
    viejo = asset(DOC_114.format(ancla=111, flags=11, dueno=222), DOC_1.format(ancla=222, nombre='Go', hijo=111))
    nuevo = asset(DOC_1.format(ancla=70, nombre='Go', hijo=90), DOC_114.format(ancla=90, flags=11, dueno=70))
    same, _ = compare(viejo + '\n', nuevo)
    check('reordenado y re-anclado ⇒ idéntico', same)
    check('el mismo texto ⇒ idéntico', compare(nuevo, nuevo)[0])

    # 2. Un escalar cambiado es un cambio.
    check('escalar cambiado ⇒ DIFF',
          not compare(viejo, asset(DOC_114.format(ancla=111, flags=99, dueno=222),
                                   DOC_1.format(ancla=222, nombre='Go', hijo=111)))[0])

    # 3. El caso que separa esto de normalizar texto: los MISMOS documentos (mismo
    #    multiconjunto de cuerpos) con el cableado CRUZADO tienen que salir DIFF.
    cruzado = asset(DOC_114.format(ancla=111, flags=11, dueno=222), DOC_1.format(ancla=222, nombre='Go', hijo=222))
    check('cableado retargeteado ⇒ DIFF (bisimulación, no normalización)', not compare(viejo, cruzado)[0])

    # 4. Un documento de más o de menos.
    check('documento añadido ⇒ DIFF',
          not compare(viejo, asset(DOC_114.format(ancla=111, flags=11, dueno=222),
                                   DOC_1.format(ancla=222, nombre='Go', hijo=111),
                                   DOC_1.format(ancla=333, nombre='Otro', hijo=111)))[0])
    check('un texto sin documentos pero distinto ⇒ DIFF', not compare('basura\n', 'basura2\n')[0])
    check('binario/ilegible ⇒ DIFF (falla cerrado)', not compare('PK\x03\x04\x00\x01\n', 'PK\x03\x04\x00\x02\n')[0])
    # El filtro de tipos: el README y los .cs del proyecto Unity NO son assets.
    check('los assets se reconocen por sufijo', all(es_asset(p) for p in
          ('a/b/X.mat', 'Assets/S.unity', 'Settings/U.asset', 'P.prefab', 'X.cs.meta')))
    check('los .cs, .md y .asmdef del proyecto NO son assets',
          not any(es_asset(p) for p in ('README.md', 'Assets/Scripts/A.cs', 'X.asmdef', 'a.json')))
    # Los finales de línea no cuentan: Windows guarda CRLF y CI tiene LF.
    check('mismo asset con CRLF y con LF ⇒ idéntico',
          compare(viejo.replace('\n', '\r\n'), nuevo)[0])
    same, detalle = compare(nuevo, nuevo.replace('\n', '\r\n'))
    check('un asset que SÓLO cambia de finales de línea ⇒ idéntico (churn)', same)
    check('…y el informe dice que son los finales de línea',
          'finales de línea' in detalle, f'({detalle})')

    # 5. El rango del churn: los endpoints inutilizables se rechazan y se cae al
    #    último commit en vez de reventar.
    check('rango sin forma A..B ⇒ se rechaza',
          resolver_rango('HEAD')[0] is None)
    check('rango con 40 ceros (primer push) ⇒ se rechaza',
          resolver_rango(CEROS + '..HEAD')[0] is None)
    check('rango con revisión inexistente ⇒ se rechaza',
          resolver_rango('no-existe-esta-rev..HEAD')[0] is None)

    # 6. Con un repo de verdad: un commit que sólo re-ancla es churn (falla) y otro
    #    con un valor cambiado es un cambio de verdad (pasa).
    if not have_git():
        print('  • git no está: se salta el caso de --churn sobre un repo real')
    else:
        with tempfile.TemporaryDirectory() as tmp:
            env = dict(os.environ, GIT_AUTHOR_NAME='t', GIT_AUTHOR_EMAIL='t@t',
                       GIT_COMMITTER_NAME='t', GIT_COMMITTER_EMAIL='t@t')
            def run(*args):
                return subprocess.run(['git'] + list(args), cwd=tmp, capture_output=True, env=env)
            run('init', '-q')
            sub = os.path.join('sub')
            os.makedirs(os.path.join(tmp, sub), exist_ok=True)
            # El ámbito del escaneo es fijo (el proyecto Unity): el repo de prueba
            # lo imita con la misma ruta.
            destino = os.path.join(tmp, SCOPE, 'Assets', 'Materials')
            os.makedirs(destino, exist_ok=True)
            mat = os.path.join(destino, 'X.mat')
            with open(mat, 'w', encoding='utf-8') as fh:
                fh.write(viejo)
            run('add', '-A'); run('commit', '-q', '-m', 'inicial')
            with open(mat, 'w', encoding='utf-8') as fh:
                fh.write(nuevo)          # sólo anclas y orden
            run('add', '-A'); run('commit', '-q', '-m', 'churn')
            rc = churn('HEAD~1..HEAD', cwd=tmp)
            check('un commit que sólo re-ancla ⇒ churn detectado (rc 1)', rc == 1, f'(rc={rc})')
            with open(mat, 'w', encoding='utf-8') as fh:
                fh.write(asset(DOC_114.format(ancla=90, flags=99, dueno=70),
                               DOC_1.format(ancla=70, nombre='Go', hijo=90)))
            run('add', '-A'); run('commit', '-q', '-m', 'cambio real')
            rc = churn('HEAD~1..HEAD', cwd=tmp)
            check('un commit con un valor cambiado ⇒ verde (rc 0)', rc == 0, f'(rc={rc})')
            os.environ['CI_RANGE'] = '..' + CEROS
            try:
                spec, aviso = resolver_rango(None, cwd=tmp)
            finally:
                os.environ.pop('CI_RANGE', None)
            check('CI_RANGE inutilizable ⇒ cae al último commit y lo dice',
                  spec == 'HEAD~1..HEAD' and bool(aviso), f'(spec={spec}, aviso={aviso})')

    # 7. LA EXCEPCIÓN: un cambio equivalente que DEJA el asset en su forma canónica
    #    no es ruido — es el que cierra el churn recurrente de un asset commiteado
    #    mal (el caso real: PheromoneMat.mat). Sin el canonicalizador a mano, la
    #    excepción no se aplica y el criterio se queda en el conservador.
    canon = _canonicalizador()
    if canon is None:
        print('  • sin el canonicalizador a mano: se salta el caso de la excepción')
    else:
        check('un re-anclado a mano NO es la forma canónica', not es_canonico(nuevo))
        check('la salida del canonicalizador SÍ es canónica',
              es_canonico(canon.canonical(viejo)[0]) is True)
        check('un asset no canónico se reconoce como tal', es_canonico(viejo) is False)
        check('un texto que no analiza no se decide (None, no False)',
              es_canonico('PK\x03\x04\x00\x01\n') is None)
        if not have_git():
            print('  • git no está: se salta la excepción sobre un repo real')
        else:
            with tempfile.TemporaryDirectory() as tmp:
                env = dict(os.environ, GIT_AUTHOR_NAME='t', GIT_AUTHOR_EMAIL='t@t',
                           GIT_COMMITTER_NAME='t', GIT_COMMITTER_EMAIL='t@t')

                def correr(*args):
                    return subprocess.run(['git'] + list(args), cwd=tmp,
                                          capture_output=True, env=env)
                destino = os.path.join(tmp, SCOPE, 'Assets', 'Materials')
                os.makedirs(destino, exist_ok=True)
                ruta = os.path.join(destino, 'X.mat')
                correr('init', '-q')
                with open(ruta, 'w', encoding='utf-8') as fh:
                    fh.write(viejo)                       # commiteado mal: no canónico
                correr('add', '-A'); correr('commit', '-q', '-m', 'mal')
                with open(ruta, 'w', encoding='utf-8') as fh:
                    fh.write(canon.canonical(viejo)[0])   # sólo la forma canónica
                correr('add', '-A'); correr('commit', '-q', '-m', 'canonicaliza')
                rc = churn('HEAD~1..HEAD', cwd=tmp)
                check('un commit que canonicaliza ⇒ NO es churn (rc 0)', rc == 0, f'(rc={rc})')
                # Y el ruido de verdad sigue bloqueando: mismo contenido, otra forma
                # que NO es la canónica.
                with open(ruta, 'w', encoding='utf-8') as fh:
                    fh.write(nuevo)
                correr('add', '-A'); correr('commit', '-q', '-m', 'churn')
                rc = churn('HEAD~1..HEAD', cwd=tmp)
                check('…y el re-anclado que NO canonicaliza sigue siendo churn (rc 1)',
                      rc == 1, f'(rc={rc})')

    # 8. DESCARTAR: el churn certificado se va, al cambio real se le quita la forma
    #    y lo demás NO se toca. Es la parte que actúa (escribe ficheros de verdad),
    #    así que se prueba sobre un repo con los cinco casos a la vez: churn puro,
    #    cambio real, canonicalización, asset nuevo sin seguimiento y un `.meta`
    #    con cambio de verdad —que no tiene documentos, así que no se reescribe:
    #    se avisa—. La salida del descarte se captura: el selftest informa de su
    #    veredicto, no del de cada caso.
    if not have_git():
        print('  • git no está: se salta el caso de --discard sobre un repo real')
    else:
        with tempfile.TemporaryDirectory() as tmp:
            env = dict(os.environ, GIT_AUTHOR_NAME='t', GIT_AUTHOR_EMAIL='t@t',
                       GIT_COMMITTER_NAME='t', GIT_COMMITTER_EMAIL='t@t')

            def descorrer(*args):
                return subprocess.run(['git'] + list(args), cwd=tmp,
                                      capture_output=True, env=env)

            dir_destino = os.path.join(tmp, SCOPE, 'Assets', 'Materials')
            os.makedirs(dir_destino, exist_ok=True)
            os.makedirs(os.path.join(tmp, SCOPE, 'Assets', 'Scenes'), exist_ok=True)

            # newline='\n' no es adorno: sin él, en Windows Python traduce a CRLF y
            # un asset "canónico" deja de reconocerse como tal (la forma canónica
            # es LF, lo pide `.gitattributes`).
            def escribe(nombre, texto):
                with open(os.path.join(dir_destino, nombre), 'w', encoding='utf-8',
                          newline='\n') as fh:
                    fh.write(texto)

            def lee(nombre):
                with open(os.path.join(dir_destino, nombre), encoding='utf-8') as fh:
                    return fh.read()

            def ruta(nombre):
                return SCOPE + '/Assets/Materials/' + nombre

            def editor_b(flags_por_doc):
                """Como deja el editor un asset de ocho documentos INDEPENDIENTES
                —sin referencias entre ellos: `{fileID: 0}` es el null—: un ancla de
                sesión por documento y en su orden, no en el orden canónico."""
                return asset(*[DOC_114.format(ancla=1000 + i, flags=f, dueno=0)
                               for i, f in enumerate(flags_por_doc)])

            # En HEAD: A commiteado en la forma NO canónica (`viejo`); B en la
            # CANÓNICA —ocho documentos independientes—, que es el estado normal
            # del repo (el generador canonicaliza lo que escribe); y C, el que
            # queda para el caso que canonicaliza.
            base_b = None
            if canon is not None:
                base_b = canon.canonical(editor_b(list(range(8))))[0]
            else:
                print('  • sin el canonicalizador a mano: se salta el caso del cambio real,'
                      ' que necesita saber cuál es la forma canónica')
            escribe('A.mat', viejo)
            escribe('E.mat.meta', 'fileFormatVersion: 2\nguid: 1111aaaa2222bbbb3333cccc4444dddd\n')
            casos = ['A.mat', 'E.mat.meta']
            if base_b is not None:
                escribe('B.mat', base_b)
                casos.append('B.mat')
            if canon is not None:
                escribe('C.mat', viejo)
                casos.append('C.mat')
            descorrer('init', '-q')
            descorrer('add', '-A'); descorrer('commit', '-q', '-m', 'base')

            # En el ÁRBOL: A sólo re-anclado y reordenado (churn), B como lo deja
            # una pasada del editor —anclas de sesión y su orden— CON UN documento
            # cambiado de verdad, C en forma canónica (canonicaliza) y D nuevo.
            escribe('A.mat', nuevo)
            # Un `.meta` con un cambio de verdad: no tiene documentos `--- !u!`, así
            # que no hay forma canónica que ponerle. Se avisa y se deja quieto —y eso
            # NO puede tumbar el descarte de los demás—.
            escribe('E.mat.meta', 'fileFormatVersion: 2\nguid: 9999eeee8888ffff7777aaaa6666bbbb\n')
            if base_b is not None:
                escribe('B.mat', editor_b([99 if i == 3 else i for i in range(8)]))
            if canon is not None:
                escribe('C.mat', canon.canonical(viejo)[0])
            escribe('D.mat', nuevo)
            casos.append('D.mat')

            antes = {n: lee(n) for n in casos}
            rutas = [ruta(n) for n in casos]
            diff_b_antes = medir_diff('HEAD', ruta('B.mat'), cwd=tmp) if base_b is not None else None

            seco = io.StringIO()
            with contextlib.redirect_stdout(seco):
                rc_seco = descartar('HEAD', rutas, dry_run=True, cwd=tmp)
                tocados = [n for n in casos if lee(n) != antes[n]]
            check('--dry-run dice que descartaría y NO toca nada',
                  rc_seco == 1 and not tocados, f'(rc={rc_seco}, tocados={tocados})')
            check('…y anuncia el churn que descartaría con la marca que lee commit-layers',
                  'tocados SÓLO en la forma' in seco.getvalue())
            if base_b is not None:
                check('…y el cambio real también sale en el aviso, con su reducción medida',
                      'se les quitaría la forma' in seco.getvalue() and ruta('B.mat') in seco.getvalue())

            with contextlib.redirect_stdout(io.StringIO()):
                rc = descartar('HEAD', rutas, cwd=tmp)
            check('el churn puro vuelve al byte exacto de HEAD', lee('A.mat') == viejo)
            check('el asset nuevo tampoco se toca', lee('D.mat') == antes['D.mat'])
            check('un .meta que no se puede canonicalizar se deja quieto (no se reescribe)',
                  lee('E.mat.meta') == antes['E.mat.meta'])
            if canon is not None:
                check('el que canonicaliza tampoco (descartarlo repondría la trampa)',
                      lee('C.mat') == antes['C.mat'])
            check('y avisa de que queda un cambio real (rc 1, y el `.meta` no lo tumba)',
                  rc == 1, f'(rc={rc})')

            if base_b is not None:
                # EL CASO DEL ENCARGO: B tenía churn Y un cambio real a la vez. Se
                # le quitó la forma y conservó el contenido.
                diff_b_despues = medir_diff('HEAD', ruta('B.mat'), cwd=tmp)
                check('al cambio real se le quita la forma: queda canónico',
                      es_canonico(lee('B.mat')) is True)
                check('…sin tocar el contenido: sigue significando lo mismo',
                      compare(antes['B.mat'], lee('B.mat'))[0])
                # Lo que tiene que quedar del diff: el documento que cambió —su
                # ancla es el hash de su contenido, así que la del documento
                # cambiado se mueve— y, como mucho, ese bloque recolocado.
                check('…y el diff contra HEAD se queda en el documento que cambió',
                      diff_b_despues is not None and diff_b_despues <= 8 < diff_b_antes,
                      f'({diff_b_antes} → {diff_b_despues} líneas)')
                check('la reducción que anuncia --dry-run es la que luego se mide',
                      medir_diff_si_quedara('HEAD', ruta('B.mat'),
                                            canon.canonical(antes['B.mat'])[0], cwd=tmp)
                      == diff_b_despues)
                check('…y el fichero queda exactamente en la forma canónica del contenido nuevo',
                      lee('B.mat') == canon.canonical(editor_b([0, 1, 2, 99, 4, 5, 6, 7]))[0])
                intactos = [d for d in base_b.split('\n--- ')[1:]      # el [0] es la cabecera
                            if d.strip() and 'm_ObjectHideFlags: 3' not in d]
                check('…con los siete documentos intactos byte a byte como en HEAD (ancla incluida)',
                      len(intactos) == 7 and all(d in lee('B.mat') for d in intactos),
                      f'({len(intactos)} documentos)')
                check('…y el cambiado sí cambió de ancla (es el hash de su contenido)',
                      'm_ObjectHideFlags: 99' in lee('B.mat'))

            # Lo que git ve tocado AHORA (no la lista de antes): el churn tiene que
            # haber desaparecido de ahí.
            restos = clasificar('HEAD', sorted({p for _, p in touched_assets(cwd=tmp)}), cwd=tmp)
            sigue_tocado = {p for grupo in restos for p, _ in grupo}
            check('tras descartar, el churn ya no aparece tocado',
                  ruta('A.mat') not in sigue_tocado)
            check('…y el cambio real sigue clasificado como cambio real',
                  any(p == ruta('B.mat') for p, _ in restos[2]))

            # El índice también: lo que estaba AÑADIDO no puede quedarse con la
            # forma vieja (sería commitear el churn que se acaba de quitar).
            if base_b is not None:
                descorrer('add', '--', ruta('B.mat'))
                descorrer('checkout', 'HEAD', '--', ruta('B.mat'))
                escribe('B.mat', editor_b([99 if i == 3 else i for i in range(8)]))
                descorrer('add', '--', ruta('B.mat'))
                with contextlib.redirect_stdout(io.StringIO()):
                    rc_ind = descartar('HEAD', [ruta('B.mat')], cwd=tmp)
                estado_ind = descorrer('status', '--porcelain', '--', ruta('B.mat')).stdout.decode()
                check('con la ruta AÑADIDA, el índice queda sin la forma vieja (nada de MM)',
                      rc_ind == 1 and estado_ind.strip()[:2] == 'M ',
                      f'(rc={rc_ind}, status={estado_ind.strip()!r})')

            with contextlib.redirect_stdout(io.StringIO()):
                rc2 = descartar('HEAD', rutas, cwd=tmp)
            check('repetirlo no descarta nada más y no rompe', rc2 == 1, f'(rc={rc2})')
            check('el byte de A sigue siendo el de HEAD', lee('A.mat') == viejo)

            # Sin nada real: el descarte deja el árbol limpio y sale verde.
            descorrer('checkout', 'HEAD', '--', ruta('B.mat'))
            descorrer('checkout', 'HEAD', '--', ruta('E.mat.meta'))
            with contextlib.redirect_stdout(io.StringIO()):
                rc3 = descartar('HEAD', rutas, cwd=tmp)
            check('sin cambios reales, el descarte sale verde (rc 0)', rc3 == 0, f'(rc={rc3})')

    # 9. LA GUARDA del que quita la forma, con un canonicalizador que MIENTE: si
    #    lo que sale de él no significa lo mismo que lo que hay en el árbol, no se
    #    escribe nada. Se prueba así —y no con el de verdad, que no puede fallar
    #    de esa manera— porque es la garantía de que «quitar la forma» no puede
    #    llevarse contenido por delante aunque el canonicalizador derive algún día.
    #    No hace falta ni repo: la guarda va antes de mirar a git.
    with tempfile.TemporaryDirectory() as tmp:
        destino = os.path.join(tmp, SCOPE, 'Assets', 'Materials')
        os.makedirs(destino, exist_ok=True)
        fichero = os.path.join(destino, 'Mentira.mat')
        with open(fichero, 'w', encoding='utf-8', newline='\n') as fh:
            fh.write(viejo)

        class Mentiroso:
            @staticmethod
            def canonical(texto):
                return texto.replace('m_Name: Pinta', 'm_Name: MENTIRA'), 1

        de_verdad = (globals()['_CANON'], globals()['_CANON_BUSCADO'])
        globals()['_CANON'], globals()['_CANON_BUSCADO'] = Mentiroso(), True
        try:
            estado, detalle, _, _ = quitar_forma(
                'HEAD', SCOPE + '/Assets/Materials/Mentira.mat', cwd=tmp)
        finally:
            globals()['_CANON'], globals()['_CANON_BUSCADO'] = de_verdad
        check('una forma que NO equivale ⇒ no se escribe (falla cerrado)',
              estado == 'no' and 'NO equivale' in detalle, f'({estado}, {detalle})')
        check('…y el fichero sigue byte a byte como estaba', read_worktree(fichero) == viejo)

        # Y lo mismo con un fichero que el canonicalizador no sabe leer: se niega
        # en vez de escribir cualquier cosa. Con el canonicalizador de verdad.
        if canon is not None:
            ilegible = SCOPE + '/Assets/Materials/NoEsYaml.mat'
            sin_documentos = os.path.join(destino, 'NoEsYaml.mat')
            with open(sin_documentos, 'w', encoding='utf-8', newline='\n') as fh:
                fh.write('PK\x03\x04\x00\x01\n')
            estado2, detalle2, _, _ = quitar_forma('HEAD', ilegible, cwd=tmp)
            check('un asset que no se puede analizar tampoco se reescribe (y se avisa)',
                  estado2 == 'ajeno', f'({estado2}, {detalle2})')
            check('…y también sigue byte a byte como estaba',
                  read_worktree(sin_documentos) == 'PK\x03\x04\x00\x01\n')

    if fails == 0:
        print('✓ selftest del verificador de assets ok (churn, cableado, escalares, '
              'documentos, rango, la excepción del canónico, el descarte de la forma '
              'y su guarda de equivalencia)')
        return 0
    return 1


def listar():
    print('Qué comprueba:')
    print('  · que dos versiones de un asset YAML de Unity tengan los mismos documentos,')
    print('    los mismos campos y el MISMO cableado, ignorando el orden de los')
    print('    documentos y la numeración de las anclas `&N`.')
    print('  · los finales de línea (el editor en Windows escribe CRLF y CI tiene LF):')
    print('    un asset que sólo cambia de finales de línea es churn, no un cambio.')
    print('  · en --churn, qué commits de un rango tocaron un asset sin cambiar nada.')
    print('  · con --discard, ADEMÁS actúa, y por los dos caminos que hacen falta para que')
    print('    el diff contra REV sea lo que cambió: devuelve a REV las rutas que certifica')
    print('    como idénticas (churn puro), y a las que CAMBIAN de verdad les quita la FORMA')
    print('    (anclas de sesión, orden, blancos) reescribiéndolas en canónico sin tocar el')
    print('    contenido. Funciona porque las anclas canónicas son el hash del contenido: un')
    print('    documento intacto vuelve a ser el mismo byte que en REV. Antes de escribir')
    print('    exige que la forma canónica pase el `compare` (si no, no escribe y lo dice),')
    print('    y enseña la reducción medida: cuántas líneas de diff había y cuántas hay.')
    print('    Con --dry-run sólo dice lo que haría —midiendo igual, sin tocar nada—; y al')
    print('    terminar vuelve a clasificar y a mirar el índice para comprobar que quedó')
    print('    como dice (el churn ya no tocado, cada CAMBIA aún cambiando y canónico).')
    print('La excepción: un cambio equivalente que DEJA el asset en la forma canónica')
    print('  (la de scripts/unity-canonicalize-assets.py) no es churn, es el que quita la')
    print('  trampa de un asset commiteado mal — el editor lo reescribiría en cada pasada.')
    print('  Se reporta aparte (CANONICALIZA) y no bloquea el push.')
    print('Qué NO comprueba:')
    print(f'  · nada fuera de {SCOPE}, y dentro sólo estos tipos: ' + ', '.join(ASSET_SUFFIXES))
    print('    (los .cs y el README del proyecto Unity no son assets: su contenido lo')
    print('     enseña git tal cual, y ahí lo que cuenta no es la forma del YAML).')
    print('  · si un cambio es DESEABLE: sólo dice si cambia algo de lo que significa.')
    print('  · con --discard, un fichero que el canonicalizador no sabe leer (un `.meta`, sin')
    print('    documentos) no se reescribe: se avisa y sigue siendo un cambio a revisar.')
    print('  · binarios ni assets sin documentos: ésos salen DIFF (falla cerrado).')
    print('  · un renombrado de fichero (git lo ve como borrado + nuevo: se salta).')
    return 0


def main(argv=None):
    # La consola de Windows viene en cp1252 y estos mensajes llevan ✓/✗/·: sin
    # forzar UTF-8, el verificador se caería al imprimir su primer veredicto.
    # Aquí y no al importar: un `errors='replace'` global taparía un fallo real en
    # cualquier otra salida del proceso.
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass

    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument('--rev', default='HEAD')
    parser.add_argument('--range', dest='rango')
    parser.add_argument('--churn', action='store_true')
    parser.add_argument('--discard', action='store_true')
    parser.add_argument('--dry-run', dest='dry_run', action='store_true')
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
    if args.churn:
        if args.paths or args.discard or args.dry_run:
            print('✗ --churn no toma rutas ni se combina con --discard/--dry-run:'
                  ' escanea un rango de commits', file=sys.stderr)
            return 2
        if not have_git():
            print('✗ --churn necesita git', file=sys.stderr)
            return 3
        return churn(args.rango)
    if args.discard:
        if args.rango:
            print('✗ --discard no combina con --range: eso es --churn', file=sys.stderr)
            return 2
        if not have_git():
            print('✗ --discard necesita git (restaura desde REV con git checkout)', file=sys.stderr)
            return 3
        paths = args.paths or [p for _, p in touched_assets()]
        if not paths:
            print(f'✓ ningún asset de Unity tocado en el árbol de trabajo ({SCOPE})')
            return 0
        return descartar(args.rev, paths, dry_run=args.dry_run)
    if args.dry_run:
        print('✗ --dry-run sólo vale con --discard', file=sys.stderr)
        return 2
    if args.rango:
        print('✗ --range sólo vale con --churn', file=sys.stderr)
        return 2

    paths = args.paths
    if not paths:
        if not have_git():
            print('✗ sin rutas y sin git no hay nada que comparar', file=sys.stderr)
            return 3
        paths = [p for _, p in touched_assets()]
    if not paths:
        print(f'✓ ningún asset de Unity tocado en el árbol de trabajo ({SCOPE})')
        return 0
    return revisar(args.rev, paths)


if __name__ == '__main__':
    sys.exit(main())
