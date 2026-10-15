# Plan: <tema> (<issue o pedido>)

<!--
Plantilla de planes de este repo (regla general del CLAUDE.md). Copiar como
docs/planes/AAAA-MM-DD-tema.md ANTES de tocar código y seguirla al pie de la letra.
Todo cambio de rumbo se anota aquí primero (Decisiones / Hallazgos) y después se programa.
Debe poder reutilizarse: quien no estuvo en la conversación tiene que poder retomar o repetir
el trabajo solo con este archivo. Borrar los comentarios al llenarla.
-->

## Contexto

- Qué se pide y por qué (quién lo pidió, reunión, issue con enlace).
- Qué queda fuera de alcance.
- Entorno: versión de Revit / .NET, modelo de prueba, rama de trabajo.

## Criterios de aceptación

<!-- Copiados de la issue, o acordados con el usuario. Se evalúan uno por uno en "Estado". -->

- [ ] …
- [ ] …

## Hallazgos del código y de Revit real

<!-- Lo que se encontró leyendo el código y PROBANDO en Revit (no supuestos), con archivo:línea,
     comandos usados y valores medidos. Regla del CLAUDE.md: verificar contra Revit real. -->

## Decisiones

<!-- Numeradas, con fecha, quién decidió y por qué. Si una decisión cambia otra, se tacha la vieja
     y se anota la nueva; no se borra. -->

1. **…** (AAAA-MM-DD, <quién>): …

## Plan paso a paso

<!-- Cada paso: qué se hace, en qué archivos, y cómo se comprueba antes de pasar al siguiente.
     Se implementa en este orden. -->

1. **…** — archivos: `…` — cómo se prueba: …
2. **…**

## Cómo probarlo

<!-- Comandos exactos (build, deploy) y lista de pruebas en Revit, para repetirla después. -->

```
dotnet build practicacq.csproj -c Debug -p:RevitVersion=2027
powershell -ExecutionPolicy Bypass -File tools/deploy.ps1 -Versions 2027 -TargetFramework net10.0-windows
```

1. …

## Estado

<!-- Se actualiza al avanzar. Al cerrar: criterio por criterio (✅ / ⚠️ / ⏳) con su evidencia,
     qué quedó pendiente y en qué commit quedó cada cosa. -->

- [ ] Plan revisado con el usuario.
- [ ] Pasos implementados.
- [ ] Probado en Revit.
