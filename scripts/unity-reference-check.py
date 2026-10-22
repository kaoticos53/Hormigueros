"""Verificador de REFERENCIAS de los assets de Unity — la pérdida que deja un editor.

POR QUÉ EXISTE. Un editor de Unity al cerrarse (o al reescribir la escena, los
materiales o los ajustes) puede DESTRUIR una referencia sin dejar ni un error ni
un aviso: el `_BaseMap` de un material pasa de apuntar a su textura a
`{fileID: 0}`, y el objeto se dibuja con el blanco por defecto. Medido: al cerrar
el editor el 2026-10-08, `PheromoneMat.mat` perdió la guía de su textura de
reposo y el quad de feromonas quedó BLANCO OPACO tapando el tablero (el defecto
del Play pass de F5.1). El YAML seguía siendo válido y el fichero parecía un
material normal: por eso no lo veía ningún check de estado. Y por eso sobrevivía
en el árbol hasta que la siguiente suite de tests lo encontraba — a veces horas
después, y a veces ya commiteado.

QUÉ COMPRUEBA, y por qué así (dos clases, las dos imposibles de ver a ojo):

  · GUÍA MUERTA — una referencia a una guía que SÍ era de este repo (algún
    `.meta` la declaraba en HEAD) y ya no la declara ningún `.meta`. Es un
    puntero a un asset que desapareció: el editor lo borró y recreó, y las
    referencias se quedaron colgando. Un asset no-repo (shader de un paquete,
    builtin) no cuenta: su guía no está en el universo versionado y no es una
    pérdida de este tree.

  · REFERENCIA PERDIDA — un asset que CAMBIÓ frente a HEAD y que tenía una guía
    que ya NO aparece en el fichero. Es la firma exacta de `PheromoneMat.mat`:
    la guía existía en HEAD y el fichero se quedó sin ella. Se mira el CONJUNTO
    de guías del fichero, no las líneas del diff: el churn del editor (anclas
    renumeradas, documentos reordenados) toca cientos de líneas `guid:` sin
    perder ninguna —medido: 312 líneas de `guid:` en el diff y CERO guías
    perdidas— y una regla por líneas daría rojo todos los días.

  Y sólo cuenta como pérdida si el destino sigue siendo un asset de DATOS
  (textura, material, mesh, ScriptableObject…): una guía de `.cs` que desaparece
  es una referencia a un componente que se quitó, y eso un asset regenerado lo
  hace a propósito.

MODO `--range`: LA PÉRDIDA QUE YA VIAJA SE CAZA EN EL COMMIT QUE LA INTRODUJO.
El árbol no es lo que llega al remoto: llegan los COMMITS. Así que aquí no se
mira el disco —se compara cada commit del rango con SU PADRE— y el veredicto es
sobre lo que se va a empujar: un árbol limpio no salva a un commit que ya perdió
la referencia, y un arreglo sin commitear no acusa a un commit que no la perdió.
Es la forma de la puerta del push (`.githooks/pre-push`); `--range` sin valor
—«deduce tú el rango»— deja que el verificador lo resuelva (`CI_RANGE`, que es lo
que declara el evento, o el último commit).

MODO `--watch`: LA PÉRDIDA SE CAZA AL CERRARSE EL EDITOR. Unity marca la sesión
con `<proyecto>/Temp/UnityLockfile`, que BORRA al salir (por eso `Temp/` no
sobrevive a una sesión: es también por lo que los recortes del HUD se conservan
fuera, ver scripts/playpass-live.sh). Con `--watch` el verificador espera a que
ese fichero desaparezca —el instante en el que el editor deja el árbol escrito—
y ENTONCES comprueba, en segundos y sin abrir nada. Si no hay sesión abierta al
arrancar, lo dice y comprueba el árbol ya (no se queda esperando a un editor que
no viene); `--timeout SEG` acota la espera y sale 3 sin veredicto si se agota.

Uso:
  python unity-reference-check.py                     # el árbol, contra HEAD
  python unity-reference-check.py --rev v0.8.0        # contra otra revisión
  python unity-reference-check.py --range A..B        # cada commit contra su padre
  python unity-reference-check.py --range             # …sobre el rango que se deduzca
  python unity-reference-check.py --project RUTA      # otro proyecto de Unity
  python unity-reference-check.py --watch             # espera al cierre del editor
  python unity-reference-check.py --watch --timeout 600
  python unity-reference-check.py --selftest          # los dos detectores, sin repo
  python unity-reference-check.py --list              # ámbito y límites

Salida: 0 referencias intactas · 1 hay una pérdida (guía muerta o referencia
perdida) · 2 uso · 3 no medible (sin git, sin proyecto, o el editor no cerró
antes de --timeout).

Lo llama `scripts/check-unity-references.sh` (envoltorio) y el play pass justo
después de que el bootstrapper genere la escena y los materiales: es el momento
en el que el editor escribe y el instante en el que una referencia se pierde. Y
la puerta del pre-push, en modo `--range`, para que una pérdida que ya entró en un
commit no llegue al remoto.
"""
import argparse
import os
import re
import subprocess
import sys
import time
from collections import Counter

# El proyecto Unity del repo, y —dentro de él— las carpetas VERSIONADAS que
# declaran guías: `Packages/` (los paquetes locales) también, porque sus assets
# se referencian con guías como cualquier otro.
PROJECT = 'src/App/AntSim.Unity'
REPO_DIRS = ('Assets', 'Packages')

# Ficheros de Unity que pueden LLEVAR una referencia. Los `.meta` no: esos
# declaran guías, no las referencian.
ASSET_SUFFIXES = ('.mat', '.unity', '.asset', '.prefab', '.controller', '.anim')

# Guía referenciada: `guid: <32 hex>` — aparece en `{fileID: N, guid: …, type: N}`
# y también en el `m_Script`/`m_Texture` de un material.
REF_GUID = re.compile(r'guid: ([0-9a-f]{32})')
# Guía DECLARADA por un `.meta` (una por fichero, en su raíz).
META_GUID = re.compile(r'^guid: ([0-9a-f]{32})\s*$', re.M)
# El campo que introduce la línea y el ELEMENTO de lista que la contiene, para
# nombrar el slot perdido: en un material, la guía va en `m_Texture` pero el slot
# que al jugador le importa es el `_BaseMap` de encima (`m_TexEnvs: - _BaseMap:`).
CAMPO = re.compile(r'^\s*-?\s*([A-Za-z_][A-Za-z0-9_]*):')
SLOT = re.compile(r'^\s*-\s+([A-Za-z_][A-Za-z0-9_]*):')


def git(args, cwd):
    """(rc, stdout) del git; nunca lanza: «no medible» es un veredicto, no un
    traceback."""
    try:
        p = subprocess.run(['git'] + list(args), cwd=cwd, capture_output=True)
    except OSError:
        return 127, ''
    return p.returncode, p.stdout.decode('utf-8', 'replace')


def en_repo(path):
    rc, out = git(['rev-parse', '--show-toplevel'], path)
    return out.strip() if rc == 0 else None


def git_ok(args, cwd):
    """stdout del git, o None si falló: «no medible» es un veredicto, no un
    traceback."""
    rc, out = git(args, cwd)
    return out if rc == 0 else None


# ── Leer una REVISIÓN (para comparar dos commits sin tocar el disco) ─────────
# El modo del árbol lee el lado «ahora» del disco y el «antes» de git. El modo
# `--range` —el de la puerta del push— no puede: mide los commits que VIAJAN, así
# que los dos lados salen de git. Una pasada de `ls-tree` para las rutas y los
# blobs, y una de `cat-file --batch` para los textos.

def ls_tree(root, project, rev):
    """[(ruta, sha)] de los ficheros VERSIONADOS bajo las carpetas del proyecto en
    `rev`, en orden estable. `None` si la revisión no se puede leer. Las rutas
    salen relativas a la raíz del repo y con «/», como las da git (también en
    Windows: git no usa «\»)."""
    rel = [project.replace(os.sep, '/') + '/' + sub for sub in REPO_DIRS]
    out = git_ok(['ls-tree', '-r', '-z', rev, '--'] + rel, root)
    if out is None:
        return None
    entradas = []
    for trozo in out.split('\0'):
        if not trozo:
            continue
        meta, _, ruta = trozo.partition('\t')
        campos = meta.split()
        if len(campos) >= 3:
            entradas.append((ruta, campos[2]))
    return entradas


def blobs_texto(root, entradas):
    """{ruta: texto} de [(ruta, sha)] en UNA pasada de `cat-file --batch`: leer los
    assets de una revisión con un `git show` por fichero serían cientos de
    procesos (100+ assets por commit)."""
    if not entradas:
        return {}
    entrada = ''.join(sha + '\n' for _, sha in entradas).encode()
    try:
        p = subprocess.run(['git', 'cat-file', '--batch'], cwd=root, input=entrada,
                           capture_output=True)
    except OSError:
        return {}
    datos, pos, salida = p.stdout, 0, {}
    for ruta, _ in entradas:
        fin = datos.find(b'\n', pos)
        if fin < 0:
            break
        cabecera = datos[pos:fin].split()
        pos = fin + 1
        if len(cabecera) < 3 or cabecera[1] != b'blob':
            continue
        tam = int(cabecera[2])
        salida[ruta] = datos[pos:pos + tam].decode('utf-8', 'replace')
        pos += tam + 1
    return salida


def assets_en_rev(root, project, rev):
    """{ruta: texto} de los assets que pueden llevar referencias en `rev`; `None`
    si la revisión no se pudo leer."""
    entradas = ls_tree(root, project, rev)
    if entradas is None:
        return None
    return blobs_texto(root, [(r, s) for r, s in entradas
                              if r.lower().endswith(ASSET_SUFFIXES)])


# ── Índice de guías declaradas ──────────────────────────────────────────────

def metas_en_disco(project):
    """{guía: ruta del .meta} de los .meta que el ÁRBOL tiene ahora, en las
    carpetas versionadas del proyecto."""
    indice = {}
    for sub in REPO_DIRS:
        raiz = os.path.join(project, sub)
        if not os.path.isdir(raiz):
            continue
        for base, dirs, files in os.walk(raiz):
            dirs.sort()
            for nombre in sorted(files):
                if not nombre.endswith('.meta'):
                    continue
                ruta = os.path.join(base, nombre)
                try:
                    with open(ruta, encoding='utf-8', errors='replace') as h:
                        m = META_GUID.search(h.read())
                except OSError:
                    continue
                if m:
                    indice[m.group(1)] = ruta
    return indice


def metas_en_rev(root, project, rev):
    """{guía: ruta} de los .meta en la revisión `rev`, con la misma pasada única
    de git que `assets_en_rev`. `None` si git no responde."""
    entradas = ls_tree(root, project, rev)
    if entradas is None:
        return None
    textos = blobs_texto(root, [(r, s) for r, s in entradas if r.endswith('.meta')])
    indice = {}
    for ruta, contenido in textos.items():
        m = META_GUID.search(contenido)
        if m:
            indice[m.group(1)] = os.path.join(root, ruta)
    return indice


# ── Referencias de un asset ─────────────────────────────────────────────────

def referencias(texto):
    """[(guía, slot)] de un asset. El nombre se arrastra desde la última línea
    `campo:`/`- elemento:` vista, para que una referencia anidada herede el de su
    contenedor en vez de salir sin contexto; el `- elemento:` de una lista gana
    al campo desnudo de dentro (`- _BaseMap:` sobre `m_Texture:`) y se suelta al
    desindentar, para no etiquetar con un slot viejo una referencia de más
    abajo."""
    salida, campo, slot, nivel_slot = [], '', '', -1
    for linea in texto.splitlines():
        sangria = len(linea) - len(linea.lstrip())
        m_slot = SLOT.match(linea)
        if m_slot:
            slot, nivel_slot = m_slot.group(1), sangria
        elif sangria <= nivel_slot:
            slot = ''
        m_campo = CAMPO.match(linea)
        if m_campo:
            campo = m_campo.group(1)
        for guia in REF_GUID.findall(linea):
            salida.append((guia, slot or campo))
    return salida


def assets_del_proyecto(project):
    """Los assets que pueden llevar referencias, en orden estable."""
    rutas = []
    for sub in REPO_DIRS:
        raiz = os.path.join(project, sub)
        if not os.path.isdir(raiz):
            continue
        for base, dirs, files in os.walk(raiz):
            dirs.sort()
            for nombre in sorted(files):
                ruta = os.path.join(base, nombre)
                if ruta.lower().endswith(ASSET_SUFFIXES):
                    rutas.append(ruta)
    return rutas


def textos_del_arbol(proyecto_abs, root):
    """{ruta relativa a la raíz: texto} de los assets con referencias del ÁRBOL de
    trabajo, en orden estable. Las claves van con «/» —igual que las de
    `assets_en_rev`— para que las dos fuentes sean intercambiables."""
    salida = {}
    for ruta in assets_del_proyecto(proyecto_abs):
        try:
            with open(ruta, encoding='utf-8', errors='replace') as h:
                salida[os.path.relpath(ruta, root).replace(os.sep, '/')] = h.read()
        except OSError:
            continue
    return salida


def valor_de_slot(texto, slot):
    """La línea que hoy ocupa el slot `- slot:` (su `m_Texture: {fileID: 0}`), para
    que el mensaje enseñe CON QUÉ se quedó el objeto. Vacío si no se encuentra."""
    lineas = texto.splitlines()
    for i, linea in enumerate(lineas):
        m = SLOT.match(linea)
        if not m or m.group(1) != slot:
            continue
        for siguiente in lineas[i + 1:i + 3]:
            if '{' in siguiente:
                return siguiente.strip()
        break
    return ''


def es_dato(meta_rel):
    """¿La guía apunta a un asset de DATOS? Una guía de script (`.cs.meta`) que
    desaparece es un componente que se quitó — regenerar la escena lo hace a
    propósito — y no es la pérdida que este verificador persigue."""
    return not meta_rel.endswith('.cs.meta')


# ── Pérdidas ACEPTADAS (borrados deliberados) ───────────────────────────────
#
# El detector cuenta referencias POR GUÍA dentro de cada asset: cuando un objeto
# se BORRA a propósito, la guía que sólo él usaba baja su cuenta y parece una
# pérdida. Así se perdió una referencia a la fuente del juego al eliminar el
# botón «↻ Sig. Gen» (F6): el objeto ya no existe, no es un editor que dejó un
# slot vacío. La lista de abajo es la única forma de decir «este borrado es
# intencionado» sin apagar el chequeo: cada línea lleva su MOTIVO y el alcance es
# exacto (ruta + guía), así que una pérdida distinta en el mismo fichero sigue
# fallando.
ACEPTADAS = os.path.join('scripts', 'unity-reference-allowlist.txt')


def cargar_aceptadas(root):
    """{(ruta, guía): motivo} de la lista versionada. Sin fichero, vacío."""
    ruta = os.path.join(root, ACEPTADAS)
    aceptadas = {}
    if not os.path.isfile(ruta):
        return aceptadas
    with open(ruta, encoding='utf-8') as f:
        for linea in f:
            linea = linea.strip()
            if not linea or linea.startswith('#'):
                continue
            partes = [p.strip() for p in linea.split('|')]
            if len(partes) < 3:
                continue
            aceptadas[(partes[0], partes[1])] = partes[2]
    return aceptadas


def aplicar_aceptadas(perdidas, aceptadas, quiet=False):
    """Quita las pérdidas que la lista declara intencionadas, diciéndolo."""
    if not aceptadas or not perdidas:
        return perdidas
    restantes = []
    for clase, rel, campo, guia, detalle in perdidas:
        motivo = aceptadas.get((rel, guia)) or aceptadas.get((os.path.basename(rel), guia))
        if motivo is None:
            restantes.append((clase, rel, campo, guia, detalle))
        elif not quiet:
            slot = f' [{campo}]' if campo else ''
            print(f'  · pérdida ACEPTADA: {rel}{slot} — {motivo}')
    return restantes


# ── La comprobación ─────────────────────────────────────────────────────────

def comprobar(root, project, antes, ahora=None):
    """(pérdidas, desconocidas, cuentas). Cada pérdida es (clase, ruta, campo,
    guía, detalle).

    `antes` es la revisión de referencia —de donde se perdieron las guías— y
    `ahora` el lado con el que se compara: `None` (por omisión) es el ÁRBOL de
    trabajo, y una revisión es ese otro COMMIT. El modo de dos revisiones es el de
    la puerta del push: mide los commits que viajan, así que no le afecta lo que
    haya sin commitear en el disco.
    """
    perdidas = []

    proyecto_abs = os.path.join(root, project)
    if not os.path.isdir(os.path.join(proyecto_abs, 'Assets')):
        print(f'✗ no encuentro {project}/Assets (¿es un proyecto de Unity?)', file=sys.stderr)
        return None, None, None

    if ahora is None:
        metas_ahora = metas_en_disco(proyecto_abs)
        textos_ahora = textos_del_arbol(proyecto_abs, root)
    else:
        metas_ahora = metas_en_rev(root, project, ahora)
        textos_ahora = assets_en_rev(root, project, ahora)
        if metas_ahora is None or textos_ahora is None:
            print(f'✗ no puedo leer la revisión {ahora} con git', file=sys.stderr)
            return None, None, None

    metas_antes = metas_en_rev(root, project, antes)
    if metas_antes is None:
        print(f'✗ no puedo leer la revisión {antes} con git (¿repo git? ¿revisión válida?)',
              file=sys.stderr)
        return None, None, None

    # Guía → .meta antes y ahora: el universo REPO (paquete/builtin queda fuera a
    # propósito: su guía no está versionada y no es una pérdida de aquí).
    universo = set(metas_antes) | set(metas_ahora)

    # — 1. GUÍA MUERTA: referencia a una guía que era del repo y ya no existe —
    # Una sola pasada: de cada asset salen las pérdidas y el contador de guías
    # que ningún `.meta` declara (esperado para paquetes/builtin; en un proyecto
    # sin `Library/` suele ser casi todo el paquete, así que se CUENTA, no se lista).
    desconocidas = set()
    for rel, texto in sorted(textos_ahora.items()):
        for guia, campo in referencias(texto):
            if guia in metas_antes and guia not in metas_ahora:
                perdidas.append(('guía muerta', rel, campo, guia,
                                 f'el .meta que la declaraba ya no está: {metas_antes[guia]}'))
            elif guia not in universo:
                desconocidas.add(guia)

    # — 2. REFERENCIA PERDIDA: el fichero cambió y una guía suya se fue —
    dirs = [project.replace(os.sep, '/') + '/' + sub for sub in REPO_DIRS]
    args = ['diff', '--name-only', '-z', antes] + ([ahora] if ahora else []) + ['--'] + dirs
    rc, out = git(args, root)
    if rc != 0:
        print(f'✗ no puedo comparar {antes} con {ahora or "el árbol"} (git diff rc={rc})',
              file=sys.stderr)
        return None, None, None

    cambiados = [p for p in out.split('\0') if p and p.lower().endswith(ASSET_SUFFIXES)]
    # El lado «antes» de una pasada de git; el «ahora» ya está en `textos_ahora`
    # (del disco o de la otra revisión).
    entradas = ls_tree(root, project, antes)
    if entradas is None:
        print(f'✗ no puedo leer los blobs de {antes} con git', file=sys.stderr)
        return None, None, None
    shas = dict(entradas)
    contenidos_antes = blobs_texto(root, [(p, shas[p]) for p in cambiados if p in shas])

    for rel in cambiados:
        if rel not in contenidos_antes:
            continue                      # nuevo (no hay «antes») o borrado
        texto = textos_ahora.get(rel)
        if texto is None:
            continue                      # borrado (o ilegible)
        head = contenidos_antes[rel]
        refs_head, refs_ahora = referencias(head), referencias(texto)
        # Por SLOT, no por guía: medido —el accidente de PheromoneMat.mat dejó
        # `_BaseMap` sin textura pero `_MainTex` conservó la MISMA guía, así que
        # «la guía ya no está en el fichero» no lo ve. Lo que se pierde es una
        # REFERENCIA, y dos slots pueden compartir guía.
        cuenta_head = Counter(refs_head)
        cuenta_ahora = Counter(refs_ahora)
        total_head = Counter(g for g, _ in refs_head)
        total_ahora = Counter(g for g, _ in refs_ahora)
        for guia in sorted(total_head):
            if total_head[guia] <= total_ahora.get(guia, 0):
                continue                  # ninguna referencia a esta guía se fue
            meta_antes = metas_antes.get(guia, '')
            if guia not in universo or not es_dato(os.path.basename(meta_antes)):
                continue                  # guía de paquete o referencia a un script
            valor = next((l.strip() for l in head.splitlines() if guia in l), '')
            # Los slots que la perdieron (el total sólo baja si algún slot baja).
            for (g, slot) in sorted(cuenta_head):
                if g != guia or cuenta_head[(g, slot)] <= cuenta_ahora.get((g, slot), 0):
                    continue
                hoy = valor_de_slot(texto, slot) if slot else ''
                detalle = f'en {antes} apuntaba a {os.path.basename(meta_antes)}: «{valor}»'
                if hoy:
                    detalle += f' · ahora: «{hoy}»'
                perdidas.append(('referencia perdida', rel, slot, guia, detalle))

    perdidas = aplicar_aceptadas(perdidas, cargar_aceptadas(root))
    return perdidas, desconocidas, (len(metas_ahora), len(metas_antes), len(cambiados))


def informar(perdidas, desconocidas, cuentas, antes, ahora=None, quiet=False):
    n_ahora, n_antes, n_camb = cuentas
    if not quiet:
        print(f'  metas: {n_ahora} en {ahora or "el árbol"} · {n_antes} en {antes} · '
              f'assets que difieren: {n_camb}')
        if desconocidas:
            print(f'  ({len(desconocidas)} guía(s) referenciadas que ningún .meta versionado declara:')
            print('   paquetes o builtin sin Library/ — no es una pérdida de este árbol)')
    if not perdidas:
        print(f'✓ ninguna referencia de este repo se perdió frente a {antes}')
        return 0
    for clase, rel, campo, guia, detalle in perdidas:
        slot = f' [{campo}]' if campo else ''
        print(f'✗ {clase}: {rel}{slot} — guía {guia}')
        print(f'    {detalle}')
    print(f'✗ {len(perdidas)} referencia(s) perdida(s) frente a {antes}')
    print('  Es la firma de un editor que reescribió el asset: el objeto se queda SIN su')
    print('  dato y se pinta con el valor por defecto (una textura perdida = quad blanco')
    print('  opaco sobre el tablero). Repara abriendo el proyecto y regenerando la escena,')
    print('  o restaura el asset con «git checkout -- <ruta>» si el cambio no era tuyo.')
    return 1


# ── Modo del push: los commits, no el árbol ────────────────────────────────

CEROS = '0' * 40


def resolver_rango(explicito, cwd):
    """El rango a escanear, con el motivo cuando no se puede usar uno.

    Gemelo —a propósito— del de `scripts/unity-asset-equivalence.py`: las dos
    puertas del push tienen que entender lo mismo por «el rango del push», y su
    envoltorio llama a los dos con el MISMO `--range` (o con ninguno). `--range`
    es una orden: si no sirve, se falla. Sin `--range` manda `CI_RANGE` —lo que
    declare el evento— y, si no trae los dos extremos utilizables (un PR no los
    trae; el primer push de una rama manda 40 ceros como «antes»), se cae al
    último commit diciendo con qué se midió: nunca un verde por no haber mirado.
    """

    def usable(spec):
        if not spec or '..' not in spec:
            return None, f'«{spec}» no tiene forma A..B'
        izq, _, der = spec.partition('..')
        if not izq or not der or izq == CEROS or der == CEROS:
            return None, f'«{spec}» sin extremos utilizables'
        for rev in (izq, der):
            if git_ok(['rev-parse', '--verify', '--quiet', rev + '^{commit}'], cwd) is None:
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

    if git_ok(['rev-parse', '--verify', '--quiet', 'HEAD~1^{commit}'], cwd) is not None:
        return 'HEAD~1..HEAD', aviso
    return None, 'no hay historial que escanear (clone superficial sin HEAD~1)'


def referencias_de_rango(explicito, root, project, quiet=False):
    """La puerta del push: cada commit de `A..B` contra SU PADRE, sin leer el
    árbol. Lo que viaja al remoto son los commits, así que lo que se mide son
    ellos: un árbol limpio no salva a un commit que ya perdió la referencia, y un
    arreglo sin commitear no acusa a un commit que no la perdió.

    0 sin pérdidas · 1 se perdió alguna · 2 el rango pedido no sirve (o la mezcla
    de modos no tiene sentido) · 3 no medible (git no pudo leer un commit).
    """
    spec, aviso = resolver_rango(explicito, root)
    if spec is None:
        print(f'• sin rango que escanear: {aviso}')
        # Un rango PEDIDO a mano que no sirve es un error de uso, no un verde.
        return 2 if explicito is not None else 0
    if aviso:
        print(f'• {aviso}')

    commits = git_ok(['rev-list', '--reverse', spec], root)
    if commits is None:
        print(f'• «{spec}» no es un rango válido en este clon')
        return 0
    commits = [c for c in commits.split() if c]
    proyectado = project.replace(os.sep, '/')
    dirs = [proyectado + '/' + sub for sub in REPO_DIRS]
    if not quiet:
        print(f'▶ Referencias en {spec} ({len(commits)} commit(s), ámbito {proyectado})')

    fallos, comparados = [], 0
    for commit in commits:
        # Un commit que no toca el proyecto no puede perder una referencia suya:
        # saltarlo evita pagar dos lecturas completas por commit en un push largo.
        tocados = git_ok(['diff', '--name-only', '-z', commit + '^', commit, '--'] + dirs,
                         root)
        if tocados is None or not tocados.strip('\0'):
            continue                      # sin padre (primer commit) o sin cambios aquí
        padre = git_ok(['rev-parse', '--verify', '--quiet', commit + '^'], root)
        if padre is None:
            continue
        comparados += 1
        asunto = (git_ok(['log', '-1', '--format=%h %s', commit], root) or '').strip()
        perdidas, _desconocidas, _cuentas = comprobar(root, project, padre.strip(), commit)
        if perdidas is None:
            return 3
        for perdida in perdidas:
            fallos.append((asunto, perdida))

    if not fallos:
        if not quiet:
            print(f'✓ ningún commit de {spec} dejó un asset sin su referencia '
                  f'({comparados} de {len(commits)} commit(s) con cambios en el proyecto,'
                  f' cada uno contra su padre)')
        return 0
    if not quiet:
        print()
        for asunto, (clase, rel, campo, guia, detalle) in fallos:
            slot = f' [{campo}]' if campo else ''
            print(f'✗ {clase}: {rel}{slot} — guía {guia}\n    en {asunto}\n    {detalle}')
        print()
        print(f'✗ {len(fallos)} referencia(s) perdida(s) en {spec}')
        print('  El commit viaja al remoto con el asset SIN su dato (el `_BaseMap` en')
        print('  `{fileID: 0}`): se dibujará con el valor por defecto —una textura perdida,')
        print('  un quad blanco opaco sobre el tablero— y ningún check de estado lo verá.')
        print('  Repara ese commit (o revierte el asset) antes de empujar.')
    return 1


# ── El cierre del editor ────────────────────────────────────────────────────

def lockfile(project):
    """La marca de sesión del editor: Unity la crea al abrir el proyecto y la
    borra al salir."""
    return os.path.join(project, 'Temp', 'UnityLockfile')


def esperar_cierre(project, timeout, quiet=False):
    """'no-abierto' | 'cerrado' | 'agotado'. Espera (bucle corto) a que el
    lockfile del editor desaparezca."""
    lf = lockfile(project)
    if not os.path.exists(lf):
        return 'no-abierto'
    if not quiet:
        print(f'▶ vigilando el cierre del editor ({lf})…')
    t0 = time.time()
    while os.path.exists(lf):
        if timeout and time.time() - t0 >= timeout:
            return 'agotado'
        time.sleep(0.5)
    return 'cerrado'


# ── Selftest: los dos detectores y la espera, sin repo del juego ────────────

def selftest():
    """Un repo git de verdad, minúsculo, con el accidente REAL: un material que
    pierde la guía de su textura (referencia perdida) y una textura cuyo `.meta`
    desaparece con la guía todavía referenciada (guía muerta). Sin el repo del
    juego y sin editor."""
    import shutil
    import tempfile
    import threading

    rojo = 0

    def check(nombre, ok):
        nonlocal rojo
        print(('  ✓  ' if ok else '  ✗  ') + nombre)
        if not ok:
            rojo = 1

    tmp = tempfile.mkdtemp(prefix='antsim-refs-')
    try:
        proj = os.path.join(tmp, 'Game')
        mat_dir = os.path.join(proj, 'Assets', 'Materials')
        os.makedirs(mat_dir)
        os.makedirs(os.path.join(proj, 'Packages'))
        mat = os.path.join(mat_dir, 'FeroMat.mat')
        tex_meta = os.path.join(mat_dir, 'FeroEmpty.asset.meta')
        tex = os.path.join(mat_dir, 'FeroEmpty.asset')
        guia_tex = '853fa06e1234567890abcdef12345678'
        guia_mat = '11112222333344445555666677778888'

        def escribir_material(base=True, main=True):
            def ref(ok):
                return ('{fileID: 2800000, guid: %s, type: 3}' % guia_tex) if ok \
                    else '{fileID: 0}'
            with open(mat, 'w', encoding='utf-8') as h:
                h.write('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'
                        '--- !u!21 &2100000\nMaterial:\n  m_Name: FeroMat\n'
                        '  m_SavedProperties:\n    m_TexEnvs:\n    - _BaseMap:\n'
                        '        m_Texture: ' + ref(base) + '\n'
                        '        m_Scale: {x: 1, y: 1}\n'
                        '    - _MainTex:\n'
                        '        m_Texture: ' + ref(main) + '\n'
                        '        m_Scale: {x: 1, y: 1}\n')

        with open(os.path.join(mat_dir, 'FeroMat.mat.meta'), 'w', encoding='utf-8') as h:
            h.write('fileFormatVersion: 2\nguid: %s\n' % guia_mat)
        with open(tex_meta, 'w', encoding='utf-8') as h:
            h.write('fileFormatVersion: 2\nguid: %s\n' % guia_tex)
        escribir_material(True, True)

        for cmd in (['init', '-q', '.'], ['add', '-A'],
                    ['-c', 'user.email=t@t', '-c', 'user.name=t', 'commit', '-qm', 'base']):
            subprocess.run(['git'] + cmd, cwd=tmp, capture_output=True)
        # sin línea de `guid:` en el .asset: no es un asset con documentos, pero
        # el verificador sólo mira su .meta.
        with open(tex, 'w', encoding='utf-8') as h:
            h.write('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'
                    '--- !u!28 &2800000\nTexture2D:\n  m_Name: FeroEmpty\n')

        # (a) árbol intacto ⇒ sin pérdidas
        p, d, c = comprobar(tmp, 'Game', 'HEAD')
        check('un árbol intacto no tiene pérdidas', p == [])

        # (b) EL ACCIDENTE REAL (medido el 2026-10-08): el editor deja el
        #     `_BaseMap` sin textura y `_MainTex` CONSERVA la misma guía. El
        #     fichero sigue teniendo la guía: sólo se perdió una REFERENCIA, y
        #     por eso una regla «la guía ya no está en el fichero» no lo ve.
        escribir_material(False, True)
        p, d, c = comprobar(tmp, 'Game', 'HEAD')
        check('un slot que pierde su textura ⇒ referencia perdida (misma guía viva en otro slot)',
              any(x[0] == 'referencia perdida' and x[3] == guia_tex for x in p))
        check('la pérdida nombra el slot que la perdió (no el campo crudo)',
              any(x[2] == '_BaseMap' for x in p))
        check('y no acusa al slot que conservó su textura',
              all(x[2] != '_MainTex' for x in p))
        check('el mensaje enseña con qué se quedó el objeto',
              any('{fileID: 0}' in x[4] for x in p))
        check('el material no se reescribe para repararlo',
              '{fileID: 0}' in open(mat, encoding='utf-8').read())

        # (b2) los dos slots pierden su textura ⇒ dos referencias perdidas
        escribir_material(False, False)
        p, d, c = comprobar(tmp, 'Game', 'HEAD')
        check('si los dos slots la pierden ⇒ dos pérdidas',
              {x[2] for x in p if x[3] == guia_tex} == {'_BaseMap', '_MainTex'})

        # (b3) el editor AÑADE una referencia más a la MISMA guía (un slot de
        #      detalle) ⇒ no se perdió nada aunque el total suba
        escribir_material(True, True)
        with open(mat, 'a', encoding='utf-8') as h:
            h.write('    - _DetailAlbedoMap:\n'
                    '        m_Texture: {fileID: 2800000, guid: ' + guia_tex + ', type: 3}\n')
        p, d, c = comprobar(tmp, 'Game', 'HEAD')
        check('una referencia de más no cuenta como pérdida', p == [])

        # (c) el editor borra el .meta de la textura y la guía sigue referenciada
        escribir_material(True, True)
        os.remove(tex_meta)
        p, d, c = comprobar(tmp, 'Game', 'HEAD')
        check('un .meta que desaparece con su guía referenciada ⇒ guía muerta',
              any(x[0] == 'guía muerta' and x[3] == guia_tex for x in p))

        # (d) una guía que NO era del repo (shader de paquete) no es una pérdida
        with open(mat, 'w', encoding='utf-8') as h:
            h.write('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'
                    '--- !u!21 &2100000\nMaterial:\n  m_Name: FeroMat\n'
                    '  m_Shader: {fileID: 4800000, guid: '
                    'deadbeefdeadbeefdeadbeefdeadbeef, type: 3}\n')
        p, d, c = comprobar(tmp, 'Game', 'HEAD')
        check('una guía no versionada (paquete/builtin) no cuenta como pérdida',
              all(x[3] != 'deadbeefdeadbeefdeadbeefdeadbeef' for x in p))

        # (e) el churn PURO —ancla renumerada, un AssetVersion nuevo de un
        #     paquete y los documentos reordenados— no pierde ninguna
        #     referencia: el diff mueve líneas, el CONTENIDO sigue ahí.
        escribir_material(True, True)
        with open(mat, encoding='utf-8') as h:
            intacto = h.read()
        with open(mat, 'w', encoding='utf-8') as h:
            h.write('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'
                    '--- !u!114 &424242\nMonoBehaviour:\n  version: 10\n'
                    '  m_Script: {fileID: 11500000, guid: '
                    'aabbccddaabbccddaabbccddaabbccdd, type: 3}\n'
                    + intacto.replace('&2100000', '&515151', 1).split('\n', 2)[2])
        if os.path.exists(tex_meta):
            os.remove(tex_meta)
        with open(tex_meta, 'w', encoding='utf-8') as h:
            h.write('fileFormatVersion: 2\nguid: %s\n' % guia_tex)
        p, d, c = comprobar(tmp, 'Game', 'HEAD')
        check('renumerar anclas y reordenar documentos no es una pérdida', p == [])

        # (f) LA PUERTA DEL PUSH: la pérdida COMMITEADA se caza con dos
        #     revisiones, sin mirar el disco. El árbol se deja REPARADO a
        #     propósito: lo que viaja al remoto es el commit, no lo que haya sin
        #     commitear, así que un arreglo local no debe tapar la pérdida.
        def commit(asunto):
            subprocess.run(['git', 'add', '-A'], cwd=tmp, capture_output=True)
            subprocess.run(['git', '-c', 'user.email=t@t', '-c', 'user.name=t',
                            'commit', '-qm', asunto], cwd=tmp, capture_output=True)

        escribir_material(True, True)
        commit('sano')
        escribir_material(False, True)          # el editor pierde el _BaseMap
        commit('roto')
        escribir_material(True, True)           # …y el arreglo queda SIN commitear

        p, d, c = comprobar(tmp, 'Game', 'HEAD')
        check('un árbol ya reparado no acusa nada (modo del árbol)', p == [])
        p, d, c = comprobar(tmp, 'Game', 'HEAD~1', 'HEAD')
        check('la pérdida que SÍ se commiteó se caza comparando dos revisiones',
              any(x[0] == 'referencia perdida' and x[2] == '_BaseMap' and x[3] == guia_tex
                  for x in p))
        check('y el arreglo sin commitear no la tapa (no se lee el disco)', p != [])
        check('el modo del push sobre un rango real ⇒ bloquea (rc 1)',
              referencias_de_rango('HEAD~1..HEAD', tmp, 'Game', quiet=True) == 1)
        check('un rango sin commits que toquen el proyecto ⇒ verde',
              referencias_de_rango('HEAD..HEAD', tmp, 'Game', quiet=True) == 0)
        check('un --range que no sirve ⇒ error de uso (rc 2)',
              referencias_de_rango('no-existe-esta-rev..HEAD', tmp, 'Game', quiet=True) == 2)
        guardado = os.environ.pop('CI_RANGE', None)
        try:
            check('sin --range lo resuelve el verificador (el último commit) ⇒ bloquea',
                  referencias_de_rango(None, tmp, 'Game', quiet=True) == 1)
        finally:
            if guardado is not None:
                os.environ['CI_RANGE'] = guardado

        # — la espera del cierre, con un lockfile de verdad —
        tmp_proj = os.path.join(tmp, 'Wait')
        os.makedirs(os.path.join(tmp_proj, 'Temp'))
        os.makedirs(os.path.join(tmp_proj, 'Assets'))
        check('sin sesión abierta ⇒ no espera', esperar_cierre(tmp_proj, 5, quiet=True) == 'no-abierto')

        lf = lockfile(tmp_proj)
        open(lf, 'w').close()
        hilo = threading.Timer(0.6, lambda: os.remove(lf))
        hilo.start()
        t0 = time.time()
        r = esperar_cierre(tmp_proj, 10, quiet=True)
        hilo.join()
        check('con la sesión abierta ⇒ espera al cierre', r == 'cerrado')
        check('y lo detecta enseguida, no por agotamiento', time.time() - t0 < 8)

        open(lf, 'w').close()
        t0 = time.time()
        r = esperar_cierre(tmp_proj, 1, quiet=True)
        check('si la sesión no cierra ⇒ agota el plazo', r == 'agotado')
        check('y el plazo se respeta', 1 <= time.time() - t0 < 5)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    if rojo == 0:
        print('✓ verificador de referencias correcto (pérdida, guía muerta, churn, '
              'guía ajena, el modo del push —dos revisiones— y la espera del cierre)')
        return 0
    return 1


def listar():
    print('Qué comprueba:')
    print('  · GUÍA MUERTA: una guía que algún `.meta` VERSIONADO declaraba en la')
    print('    revisión pedida y ya no declara ninguno — el asset desapareció y las')
    print('    referencias quedaron colgando.')
    print('  · REFERENCIA PERDIDA: un asset que difiere de la revisión tenía una guía')
    print('    que ya no aparece en el fichero (el `_BaseMap` que quedó en `fileID: 0`).')
    print('    Se compara el CONJUNTO de guías por fichero, no las líneas del diff: el')
    print('    churn del editor mueve cientos de líneas `guid:` sin perder ninguna.')
    print('  · Sólo cuentan las guías de assets de DATOS: perder la guía de un `.cs` es')
    print('    quitar un componente, y un asset regenerado lo hace a propósito.')
    print(f'  · Un BORRADO deliberado se declara con su motivo en {ACEPTADAS}')
    print('    (ruta | guía | motivo): todo lo demás sigue fallando.')
    print('Modos:')
    print('  · el ÁRBOL (por omisión): el árbol de trabajo contra una revisión (HEAD).')
    print('  · `--range A..B`: cada commit del rango contra SU PADRE, sin mirar el disco')
    print('    —es lo que empuja un push, y lo que mide la puerta 2 del pre-push—.')
    print('  · `--watch`: espera al cierre del editor y comprueba entonces el árbol.')
    print('Qué NO hace:')
    print('  · no toca ni repara ningún fichero (informa y sale ≠ 0);')
    print('  · no decide si un cambio es deseable, sólo si una referencia se perdió;')
    print('  · no abre Unity: lee YAML y las guías de los `.meta`.')
    return 0


def main(argv=None):
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass

    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument('--rev', default='HEAD')
    # `--range` SIN valor (const='') es «deduce tú el rango»: el envoltorio lo
    # llama así desde el pre-push cuando no hay refs que leer, igual que hace el
    # verificador de assets con `--churn`.
    parser.add_argument('--range', dest='rango', nargs='?', const='', default=None)
    parser.add_argument('--project', default=PROJECT)
    parser.add_argument('--watch', action='store_true')
    parser.add_argument('--timeout', type=float, default=0.0)
    parser.add_argument('--quiet', action='store_true')
    parser.add_argument('--selftest', action='store_true')
    parser.add_argument('--list', action='store_true')
    parser.add_argument('--help', '-h', action='store_true')
    args = parser.parse_args(argv)

    if args.help:
        print(__doc__.strip())
        return 0
    # Mezclas que no significan nada: mejor un rc 2 dicho que una medida a medias.
    if args.rango is not None:
        if args.watch:
            print('✗ --range no combina con --watch: el rango son commits ya escritos y'
                  ' el cierre del editor es del árbol de trabajo', file=sys.stderr)
            return 2
        if args.rev != 'HEAD':
            print('✗ --range no combina con --rev: en modo de rango cada commit se'
                  ' compara con SU PADRE', file=sys.stderr)
            return 2
    if args.list:
        return listar()
    if args.selftest:
        return selftest()

    root = en_repo(os.getcwd())
    if root is None:
        print('✗ no estoy dentro de un repositorio git', file=sys.stderr)
        return 3

    # `normpath`: la ruta puede venir con separadores mezclados (Windows) y el
    # mensaje del --watch la imprime; normalizarla es lo que hace legible eso.
    project = os.path.normpath(os.path.join(root, args.project))

    if args.rango is not None:
        # Sin valor ⇒ que lo resuelva el verificador (CI_RANGE o el último commit).
        return referencias_de_rango(args.rango or None, root,
                                    os.path.relpath(project, root), args.quiet)

    if args.watch:
        estado = esperar_cierre(project, args.timeout, args.quiet)
        if estado == 'agotado':
            print(f'✗ el editor no cerró en {args.timeout:g}s — no hay veredicto '
                  '(relanza con más --timeout o sin --watch)', file=sys.stderr)
            return 3
        if estado == 'no-abierto':
            print('  (no hay sesión del editor abierta: se comprueba el árbol ya)')

    perdidas, desconocidas, cuentas = comprobar(root, os.path.relpath(project, root),
                                                args.rev)
    if perdidas is None:
        return 3
    return informar(perdidas, desconocidas, cuentas, args.rev, None, args.quiet)


if __name__ == '__main__':
    sys.exit(main())
