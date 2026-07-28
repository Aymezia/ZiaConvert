# Composants tiers

ZiaConvert est distribué sous **GPL v3** (voir `LICENSE.txt`).

Ce n'est pas un choix esthétique : l'application est livrée avec un binaire FFmpeg
compilé avec `--enable-gpl --enable-version3`, qui embarque notamment `libx264` et
`libx265`. Redistribuer ce binaire avec l'application impose de placer l'ensemble
sous GPL v3.

## FFmpeg

| | |
|---|---|
| Version | 8.1 (`full_build`, gyan.dev) |
| Licence | **GPL v3 ou ultérieure** |
| Configuration | `--enable-gpl --enable-version3` |
| Emplacement | `tools/ffmpeg/` |
| Texte | `tools/ffmpeg/LICENSE-ffmpeg.txt` |
| Source | <https://www.gyan.dev/ffmpeg/builds/> · <https://git.ffmpeg.org/ffmpeg.git> |

FFmpeg est invoqué comme processus externe et n'est pas modifié.

### Revenir à une licence permissive

Deux voies si le code doit rester fermé :

1. **Build LGPL de FFmpeg** — perte de `libx264` et `libx265`, donc de l'encodage
   H.264/HEVC **logiciel**. L'encodage matériel NVENC reste disponible (il est
   compatible LGPL), mais une machine sans GPU compatible ne pourrait plus produire
   que du VP9, de l'AV1 ou du MPEG-4.
2. **Licence commerciale x264/x265** auprès de leurs ayants droit.

## Bibliothèques .NET

| Composant | Version | Licence |
|---|---|---|
| .NET Runtime | 10.0 | MIT |
| Avalonia | 11.3.18 | MIT |
| Avalonia.Themes.Fluent | 11.3.18 | MIT |
| Avalonia.Fonts.Inter | 11.3.18 | MIT — police Inter sous SIL Open Font License 1.1 |
| CommunityToolkit.Mvvm | 8.4.2 | MIT |
| Microsoft.Extensions.Logging.Abstractions | 10.0.0 | MIT |

## Moteurs prévus (non encore livrés)

| Composant | Licence | Distribution prévue |
|---|---|---|
| ImageMagick / Magick.NET | Apache 2.0 | embarqué |
| LibRaw | LGPL 2.1 / CDDL | embarqué (via ImageMagick) |
| Real-ESRGAN ncnn Vulkan | MIT | embarqué |
| LibreOffice | MPL 2.0 | **non redistribué** — téléchargé à la demande ou détecté sur la machine |

LibreOffice n'étant jamais redistribué avec ZiaConvert, sa licence n'a aucune
incidence sur celle de l'application.
