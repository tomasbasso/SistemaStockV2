# Guía de Uso: Engram + Agent Teams Lite

SistemaDeStockV3 ahora tiene memoria persistente y desarrollo guiado por specs.

---

## Parte 1: ENGRAM (Memoria Persistente)

### Qué es
Engram es un sistema de memoria que sobrevive entre sesiones de chat. Guarda decisiones, fixes, patrones y aprendizajes en SQLite con búsqueda full-text.

### Inicio Rápido

```bash
# 1. Iniciar el servidor de memoria (antes de OpenCode)
engram serve &

# 2. Abrir OpenCode
opencode .
```

### Comandos CLI

```bash
engram serve           # Iniciar servidor HTTP (puerto 7437)
engram serve 8080     # Usar puerto diferente
engram tui            # UI interactiva para explorar memorias
engram search "auth"  # Buscar memorias
engram stats          # Ver estadísticas
engram export backup.json  # Exportar a JSON
```

### Cómo Funciona (Auto-Guardado)

El agente guarda memorias automáticamente cuando:
- Completa un fix importante
- Toma una decisión de arquitectura
- Descubre un patrón nuevo
- Aprende algo sobre el codebase

**No necesitás hacerlo manualmente** - el plugin de Engram le indica al agente cuándo guardar.

### Estructura de una Memoria

```json
{
  "title": "N+1 query fix en Producto list",
  "type": "bugfix",
  "topic_key": "bug-product-list-n-plus-one",
  "content": {
    "what": "Corrigí N+1 queries en DataService.GetProductosAsync",
    "why": "El Include() faltaba en la query EF Core",
    "where": "Services/DataService.cs:245",
    "learned": "Siempre usar Include() para navegación properties"
  },
  "project": "SistemaDeStockV3",
  "scope": "project"
}
```

### Tema Keys (para topics que evolucionan)

```bash
# Sugerir un topic key estable
mem_suggest_topic_key(type="architecture", title="Auth architecture")

# Guardar con ese topic (próximos cambios lo actualizan, no crean nuevos)
mem_save(topic_key="architecture-auth-architecture", ...)

# Cambios futuros sobre auth usan el mismo key → se actualiza, no duplica
```

### Patrón de 3 Capas (Búsqueda Progresiva)

```bash
# 1. Búsqueda broad → IDs
mem_search("auth middleware")
# → resultados compactos con IDs

# 2. Timeline → contexto temporal
mem_timeline(observation_id=42)
# → qué pasó antes/después en esa sesión

# 3. Contenido completo
mem_get_observation(id=42)
# → texto completo sin truncar
```

---

## Parte 2: AGENT TEAMS LITE (SDD)

### Qué es
Sistema de orquestación con 9 sub-agentes especializados. Cada uno tiene contexto fresco, hace una tarea específica, y devuelve resultados estructurados.

### Flujo de Trabajo SDD

```
explore → propose → spec + design → tasks → apply → verify → archive
              ↑______________|
                      (design depende de proposal)
```

### Comandos Disponibles

| Comando | Qué hace |
|---------|----------|
| `/sdd-init` | Inicializar contexto del proyecto |
| `/sdd-explore <topic>` | Investigar una idea (sin crear archivos) |
| `/sdd-new <name>` | Iniciar feature nuevo (explore + propose) |
| `/sdd-continue` | Continuar siguiente fase del change activo |
| `/sdd-ff <name>` | Fast-forward: propose → spec → design → tasks |
| `/sdd-apply` | Implementar tasks en batches |
| `/sdd-verify` | Validar contra specs |
| `/sdd-archive` | Cerrar change y persistir estado |

### Fases Detalladas

#### 1. Explore (`/sdd-explore <topic>`)
- Lee el codebase
- Investiga approaches
- Compara trade-offs
- **Output:** artifact de exploración

#### 2. Propose (`/sdd-propose`)
- Resume exploración
- Define WHY + SCOPE + APPROACH
- **Output:** `proposal.md`

#### 3. Spec (`/sdd-spec`)
- Especificación técnica detallada
- Requisitos funcionales
- **Output:** `specs/<feature>/spec.md`

#### 4. Design (`/sdd-design`)
- Decisiones de arquitectura
- Patrones a usar
- **Output:** `design.md`

#### 5. Tasks (`/sdd-tasks`)
- Lista de tareas implementables
- Checkboxes para tracking
- **Output:** `tasks.md`

#### 6. Apply (`/sdd-apply`)
- Implementa código
- Marca tasks como completadas
- **Output:** progreso de implementación

#### 7. Verify (`/sdd-verify`)
- Valida contra specs
- Reporta: CRITICAL / WARNING / SUGGESTION

#### 8. Archive (`/sdd-archive`)
- Persiste estado final
- Limpia change activo

### Ejemplo de Uso

```bash
# Escenario: querés agregar export CSV

# 1. Inicializar contexto del proyecto (primera vez)
/sdd-init

# 2. Explorar la idea
/sdd-explore csv export options

# 3. Iniciar feature completo (explore + propose + spec + design + tasks)
/sdd-ff add-csv-export

# 4. Implementar tasks en batches
/sdd-apply

# 5. Verificar
/sdd-verify

# 6. Archivar cuando está completo
/sdd-archive
```

### Persistencia Híbrida (Modo Configurado)

Tu proyecto usa modo `hybrid`:

```
┌─────────────────────────────────────┐
│           HYBRID MODE               │
├─────────────────────────────────────┤
│  ENGRAM          │  OPENSPEC        │
│  (memoria)       │  (archivos)      │
├─────────────────────────────────────┤
│  • Cross-session  │  • specs/        │
│  •搜索 rápida     │  • changes/      │
│  • TUI           │  • Versionable   │
│  • Se reconstruye │  • Compartido    │
└─────────────────────────────────────┘
```

---

## Parte 3: COMBINANDO AMBOS

### Sesión Típica

```bash
# 1. Iniciar Engram
engram serve &

# 2. Abrir OpenCode
opencode .

# 3. Trabajá normalmente
#    El agente guarda memorias automáticamente en Engram

# 4. Para features grandes, usar SDD
/sdd-new add-barcode-scanning
```

### Recuperación de Contexto

```bash
# En nueva sesión, Engram inyecta contexto automáticamente:
# → Decisiones previas
# → Bugs fijos
# → Patrones descubiertos

# También podés buscar manualmente:
engram tui
# → Explora memorias visualmente

# O desde OpenCode:
mem_search("barcode")
mem_context("SistemaDeStockV3")
```

---

## Parte 4: COMANDOS ÚTILES

### Engram
```bash
engram serve           # Servidor en background
engram tui             # UI interactiva
engram search "query"  # Búsqueda CLI
engram stats           # Estadísticas
engram sync            # Exportar para git
```

### Verificación de Instalación
```bash
engram --version       # v1.10.1+
engram setup opencode  # Re-instalar plugin si needed
```

---

## Parte 5: TIPS Y BEST PRACTICES

### Cuándo usar SDD
- ✅ Features nuevas sustanciales
- ✅ Refactors que requieren planificación
- ✅ Cambios de arquitectura
- ❌ Quick fixes (hacelo directo)
- ❌ Preguntas simples (respondé directo)

### Cuándo guardar memorias
El plugin te lo indica, pero guardá cuando:
- Arreglás un bug tricky
- Tomás decisión de diseño
- Descubriste un patrón útil
- Configuraste algo complejo

### Topic Keys
- Usá para topics que evolucionan (`architecture/*`, `bug/*`)
- No duplicar keys para temas diferentes
- `mem_suggest_topic_key` sugiere automáticamente

---

## Referencia Rápida

| Necesidad | Solución |
|-----------|----------|
| Memoria entre sesiones | Engram (automático) |
| Explorar idea | `/sdd-explore` |
| Planificar feature | `/sdd-ff <name>` |
| Implementar | `/sdd-apply` |
| Ver progreso | `engram tui` |
| Buscar decisiones | `mem_search` |
