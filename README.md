# Jumpfall JSM Compilator

Herramienta oficial de escritorio para preparar mapas compilados de Jumpfall y publicarlos en Steam Workshop.

## Compatibilidad actual

- Lee mapas compilados `.jfue` con `LevelData` hasta la version 19.
- No publica `.jmap`: ese formato sigue siendo editable y local.
- Conserva el formato de paquete `.jsm` version 1 utilizado por Jumpfall.
- Reconoce las piezas actuales: `box_ground`, `checkpoint`, `apple`, `orb_jump`, `plane_jump` y `elevator`.
- Reconoce los triggers actuales, incluidos Lua, camara estatica, visibilidad, eventos, temporizador y wall jump.
- Incluye fondos PNG y video MP4/WebM, audio WAV/OGG y scripts Lua.
- Valida limites de seguridad antes de reemplazar un paquete existente.

> Estado temporal de Linux: Jumpfall rechaza mapas que tengan fondos MP4 o WebM habilitados y muestra `Map not compatible`. Los mapas sin fondos de video siguen siendo compatibles. El compilador permite crearlos para Windows y macOS, pero muestra una advertencia.

## Estructura generada

```text
Documents/jumpfall/levels/workshop/{workshop_id}/
|-- map_name.jfue
|-- map_name.jsm
|-- preview.png            (opcional)
`-- assetlocal/
    |-- backgroundimg/
    |   |-- background.png
    |   |-- background.mp4
    |   `-- background.webm
    |-- sound/
    |   |-- music.wav
    |   `-- music.ogg
    `-- lua/
        `-- main.lua
```

El archivo `.jsm` contiene solamente:

- un mapa `.jfue` en la raiz;
- `manifest.json`;
- `preview.png`, cuando fue seleccionada;
- `assetlocal/` y sus archivos compatibles.

## Uso

1. Abre un mapa ya compilado como `.jfue`.
2. Selecciona `assetlocal` si la deteccion automatica no encuentra la carpeta correcta.
3. Selecciona una preview PNG opcional.
4. Completa titulo, descripcion y etiquetas de Workshop.
5. Usa la compilacion local para revisar advertencias y crear el `.jsm`.
6. Prueba el mapa dentro de Jumpfall antes de publicarlo.
7. Publica o actualiza el item de Steam Workshop.

Los fondos, pistas de audio y el script Lua principal referenciados por el `.jfue` se buscan en este orden:

1. La carpeta `assetlocal` seleccionada.
2. `assetlocal` junto al mapa.
3. `Documents/jumpfall/levels/assetlocal`.
4. La ruta heredada `Documents/jumpfall/levels/assetslocal`.

Si un archivo referenciado no aparece, el compilador crea el paquete con una advertencia. Jumpfall no podra reproducir ese recurso faltante.

## Limites validados

| Recurso | Limite |
|---|---:|
| Paquete `.jsm` comprimido | 256 MiB |
| Contenido extraido | 512 MiB |
| Archivo individual | 128 MiB |
| Imagen PNG | 32 MiB |
| Fondo de video | 128 MiB |
| Script Lua | 512 KiB |
| Archivos por paquete | 1500 |
| Textura PNG | 4096 x 4096 |
| Bosses por mapa | 8 |
| Nodos por boss | 128 |

Las extensiones aceptadas dentro de `assetlocal` son `.json`, `.png`, `.mp4`, `.webm`, `.wav`, `.ogg`, `.lua`, `.txt` y `.md`. Otras extensiones se omiten y se registran como advertencia.

## Compilar la herramienta

El proyecto actual usa Windows Forms y .NET 9 para Windows x64.

```powershell
dotnet build .\JumpfallJsmCompilator.csproj -c Release
```

Publicacion autocontenida en un solo ejecutable:

```powershell
dotnet publish .\JumpfallJsmCompilator.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true
```

El AppID de Jumpfall Playtest permanece integrado en el codigo. No se solicita al usuario y no debe sustituirse por un campo editable.
