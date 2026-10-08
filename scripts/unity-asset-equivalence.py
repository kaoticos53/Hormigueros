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

Uso:
  python unity-asset-equivalence.py                    # los assets que el árbol tiene tocados, contra HEAD
  python unity-asset-equivalence.py --rev REV RUTAS…   # esas rutas, contra REV
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

Salida: 0 sin nada que reprochar · 1 diferencias (o churn en el rango) · 2 uso ·
3 sin git para las partes que lo necesitan.
"""
import argparse
import collections
import hashlib
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


def touched_assets():
    """Las rutas de Unity que el árbol de trabajo tiene tocadas (modificadas,
    borradas o nuevas), con su código de estado."""
    out = git_ok('status', '--porcelain', '--', SCOPE)
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

def revisar(rev, paths):
    ruido, reales, saltados, canoniza = [], [], [], []
    for path in paths:
        old = show(rev, path)
        new = read_worktree(path)
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

    for path, detalle in rumbo_ordenado(ruido):
        print(f'IDÉNTICO  {path}  ({detalle})')
    for path, detalle in canoniza:
        print(f'CANONICALIZA  {path}  ({detalle}: queda en la forma canónica del editor)')
    for path, detalle in reales:
        print(f'CAMBIA    {path}  ({detalle})')
    for path, motivo in saltados:
        print(f'SALTADO   {path}  ({motivo})')

    total = len(ruido) + len(reales) + len(saltados) + len(canoniza)
    print()
    if total == 0:
        print(f'✓ ningún asset de Unity tocado en el árbol de trabajo ({SCOPE})')
        return 0
    print(f'{len(ruido)} idénticos (churn) · {len(canoniza)} que canonicalizan · '
          f'{len(reales)} con cambio real · {len(saltados)} saltados, de {total}')
    if ruido:
        print('✅ los idénticos se pueden descartar sin perder nada:')
        print('   git checkout -- ' + ' '.join(p for p, _ in rumbo_ordenado(ruido)))
    if canoniza:
        print('✅ los que canonicalizan SÍ se commitean (no los descartes): son los que')
        print('   dejan el árbol en la forma que el editor escribe, y el churn de esas')
        print('   rutas se acaba con el commit:')
        print('   git add -- ' + ' '.join(p for p, _ in rumbo_ordenado(canoniza)))
    if reales:
        print(f'✗ hay {len(reales)} asset(s) con cambio de verdad: revísalos antes de commitear')
        return 1
    if canoniza:
        print(f'→ {len(canoniza)} asset(s) quedan en forma canónica: commitéalos para cerrar el churn')
        return 1
    return 0


def rumbo_ordenado(pares):
    return sorted(pares, key=lambda p: p[0])


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

    if fails == 0:
        print('✓ selftest del verificador de assets ok (churn, cableado, escalares, '
              'documentos, rango y la excepción del canónico)')
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
    print('La excepción: un cambio equivalente que DEJA el asset en la forma canónica')
    print('  (la de scripts/unity-canonicalize-assets.py) no es churn, es el que quita la')
    print('  trampa de un asset commiteado mal — el editor lo reescribiría en cada pasada.')
    print('  Se reporta aparte (CANONICALIZA) y no bloquea el push.')
    print('Qué NO comprueba:')
    print(f'  · nada fuera de {SCOPE}, y dentro sólo estos tipos: ' + ', '.join(ASSET_SUFFIXES))
    print('    (los .cs y el README del proyecto Unity no son assets: su contenido lo')
    print('     enseña git tal cual, y ahí lo que cuenta no es la forma del YAML).')
    print('  · si un cambio es DESEABLE: sólo dice si cambia algo de lo que significa.')
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
        if args.paths:
            print('✗ --churn no toma rutas: escanea un rango de commits', file=sys.stderr)
            return 2
        if not have_git():
            print('✗ --churn necesita git', file=sys.stderr)
            return 3
        return churn(args.rango)
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
