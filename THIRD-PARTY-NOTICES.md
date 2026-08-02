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

## ImageMagick

| | |
|---|---|
| Licence | **Apache 2.0** |
| Invocation | processus externe (`magick`), via le PATH ou `tools/imagemagick/` |
| RAW | libraw compilé en dur (confirmé : `magick -list format` liste CR2, CR3, NEF, ARW, DNG, ORF, RW2, RAF, PEF, SRW en lecture) |
| Source | <https://imagemagick.org/script/download.php> |

**Non encore embarqué dans l'installeur.** Le paquet `winget` utilisé en
développement (`ImageMagick.Q16-HDRI`) s'installe en MSIX, dont les binaires
sont virtualisés par Windows et ne peuvent pas être copiés tels quels dans
`dist/`. L'embarquement se fera avec le build ZIP portable officiel, au moment
de construire l'installeur — d'ici là, `magick` doit être installé
séparément sur la machine qui exécute ZiaConvert (l'application le détecte et
l'indique clairement si absent, sans planter).

## LibRaw

| | |
|---|---|
| Licence | LGPL 2.1 / CDDL (au choix) |
| Usage | compilé à l'intérieur du binaire ImageMagick ci-dessus, jamais lié directement par ZiaConvert |

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

## Real-ESRGAN (ncnn / Vulkan)

| | |
|---|---|
| Version | 0.2.5.0 (build ncnn-vulkan du 24/04/2022) |
| Licence | **MIT** |
| Invocation | processus externe (`realesrgan-ncnn-vulkan`), via le PATH ou `tools/realesrgan/` |
| Modèles embarqués | `realesrgan-x4plus`, `realesrgan-x4plus-anime`, `realesr-animevideov3` (x2/x3/x4) |
| GPU | Vulkan requis (Intel, AMD, NVIDIA) |
| Source | <https://github.com/xinntao/Real-ESRGAN/releases/tag/v0.2.5.0> |

**Non encore embarqué dans l'installeur**, pour la même raison qu'ImageMagick :
en développement l'outil est installé dans `%LOCALAPPDATA%\ZiaConvert\engines\`,
qui simule l'emplacement du téléchargement à la demande prévu pour la release
finale. D'ici là, il doit être placé manuellement au même endroit ou accessible
via le PATH.

## Moteurs prévus (non encore livrés)

| Composant | Licence | Distribution prévue |
|---|---|---|
| LibreOffice | MPL 2.0 | **non redistribué** — téléchargé à la demande ou détecté sur la machine |

LibreOffice n'étant jamais redistribué avec ZiaConvert, sa licence n'a aucune
incidence sur celle de l'application.
