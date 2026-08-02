# ZiaConvert

Convertisseur video, audio et image, avec interface graphique et ligne de commande. ZiaConvert n'implemente aucun algorithme de conversion lui-meme : c'est un orchestrateur au-dessus de [FFmpeg](https://ffmpeg.org/), d'[ImageMagick](https://imagemagick.org/) et de [Real-ESRGAN](https://github.com/xinntao/Real-ESRGAN), qui gere le routage, la progression, l'annulation propre et le parallelisme.

## Telecharger

La derniere version est disponible dans les [releases](https://github.com/Aymezia/ZiaConvert/releases) : `ZiaConvert-Setup-x.y.z.exe`, un installateur classique (raccourcis, desinstalleur, ajout optionnel de `zia` au PATH). L'application verifie elle-meme au demarrage si une version plus recente existe et propose de l'installer en un clic.

## Fonctionnalites

- **Copie de flux sans reencodage** quand le conteneur cible accepte les codecs source (`mp4` vers `mkv` en quelques secondes plutot qu'en plusieurs minutes), avec detection automatique et repli sur le reencodage sinon. Un mode strict (`--remux-only`) echoue clairement plutot que de basculer sans prevenir sur un reencodage complet — utile pour re-emballer un rip DVD (MPEG-2/AC3, format `.vob` reconnu) sans jamais toucher a l'image ni au son.
- **Acceleration materielle** (NVENC, QuickSync, AMF) detectee par un veritable encodage de test au demarrage, pas par simple lecture de la liste des encodeurs compiles dans FFmpeg — cette liste annonce souvent des encodeurs que le materiel ne sait pas executer.
- **Changement de cadence** en deux modes distincts : duplication d'images (instantane, mais n'ajoute aucune fluidite reelle) ou interpolation par analyse de mouvement (`minterpolate`, plus lent mais produit de vraies images intermediaires).
- **GIF anime** avec palette de couleurs calculee sur le contenu, en une seule passe.
- **Decoupe** par debut/fin, extraction audio, normalisation de volume (EBU R128).
- **File de conversions** avec parallelisme borne par famille de format (une video sature deja tous les coeurs, inutile d'en lancer dix en parallele), annulation individuelle ou globale, sans jamais laisser de fichier de sortie partiel.
- **Prereglages** (Rapide, Web, Qualite, Archivage) qui remplissent les champs plutot que de les masquer derriere un mode « personnalise » — et **prereglages personnalises** enregistrables et supprimables depuis l'application, a cote de ceux fournis.
- **Choix de piste** audio et sous-titres par index (celui affiche par `zia probe`), pour garder la bonne langue ou le bon commentaire sur un rip multipiste.
- **Sous-titres externes** (.srt, .ass, .ssa, .vtt) integres a une sortie mkv sans reencoder l'image ni le son, avec langue et titre par piste — pratique pour ajouter un VOSTFR trouve a part a un film ou une serie.
- **Verification post-conversion** : la duree de la sortie est recomparee a celle de la source ; un ecart anormal est signale sans jamais transformer une conversion reussie en echec.
- **Estimation de taille finale** avant de lancer, par un veritable echantillon encode plutot qu'une formule — exacte pour un remux, approchee par extrapolation pour un reencodage a qualite constante.
- **Developpement RAW** (CR2, CR3, NEF, ARW, DNG, ORF, RW2, RAF, PEF, SRW) via libraw, avec balance des blancs, orientation automatique et conversion d'espace colorimetrique.
- **Images classiques** (jpeg, png, webp, avif, heic, tiff, bmp) avec redimensionnement, choix qualite/sans-perte selon le format, et suppression optionnelle des metadonnees EXIF.
- **Agrandissement par IA** (Real-ESRGAN) qui reconstruit du detail plutot que d'etirer les pixels — a ne pas confondre avec un redimensionnement, qui reste instantane mais n'ajoute rien. Une estimation de duree, calibree sur la machine par une mesure reelle plutot que devinee, s'affiche avant de lancer.
- **Mise a jour automatique** : l'application interroge la derniere release GitHub au demarrage et, si une version plus recente existe, propose de la telecharger et de l'installer en un clic (silencieux, l'application se ferme et se relance d'elle-meme).

## Formats

| Entree | Sortie |
|---|---|
| mp4, mkv, webm, mov, avi, m4v, wmv, flv, mpg, ts, ogv, 3gp, vob (lecture seule) | mp4, mkv, webm, mov, avi, m4v, wmv, flv, mpg, ts, ogv, 3gp, gif |
| — | mp3, aac, m4a, flac, wav, opus, ogg, wma |
| jpeg, png, webp, avif, heic, tiff, bmp, ico | jpeg, png, webp, avif, heic, tiff, bmp, ico |
| cr2, cr3, nef, arw, dng, orf, rw2, raf, pef, srw (RAW, lecture seule) | — (developpe vers un format d'image classique) |

L'extraction audio fonctionne depuis n'importe quel fichier video ou audio pris en charge.

## Compiler depuis les sources

Necessite le [SDK .NET 10](https://dotnet.microsoft.com/download), [FFmpeg](https://www.gyan.dev/ffmpeg/builds/) (build « full »), [ImageMagick](https://imagemagick.org/script/download.php) (`magick`) et, pour l'agrandissement par IA, [Real-ESRGAN ncnn-vulkan](https://github.com/xinntao/Real-ESRGAN/releases/tag/v0.2.5.0) — tous accessibles dans le `PATH` ou places dans `tools/<outil>/` a la racine du projet (Real-ESRGAN a besoin d'un GPU compatible Vulkan).

```bash
dotnet build ZiaConvert.slnx
dotnet test ZiaConvert.slnx
dotnet run --project src/ZiaConvert.App
```

### Publier un executable autonome

```bash
dotnet publish src/ZiaConvert.App -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
dotnet publish src/ZiaConvert.Cli -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

Copier `ffmpeg.exe` et `ffprobe.exe` dans `dist/tools/ffmpeg/` pour obtenir un dossier autonome.

### Construire l'installateur

Necessite [Inno Setup 6](https://jrsoftware.org/isinfo.php). `installer/build.ps1` publie l'App et la CLI, signe les executables (si un certificat `.pfx` est present dans `signing/`, jamais versionne), puis compile et signe `installer/output/ZiaConvert-Setup-x.y.z.exe`. Sans certificat, le build reste possible : tout ressort simplement non signe.

```powershell
.\installer\build.ps1
```

## Ligne de commande

```bash
zia film.mp4 -o film.mkv                                   # remux, aucune perte
zia film.mkv -o film.mp4 --codec h264 -q 20                 # reencodage
zia clip.mp4 -o clip.gif --fps 15 -w 480 -ss 5 -to 10        # extrait en GIF
zia concert.mp4 -o concert.mp3 -b 192k                       # extraction audio
zia video.mp4 -o video-60fps.mp4 --fps 60 --interpolate      # vraie interpolation
zia photo.cr2 -o photo.jpg                                    # developpement RAW
zia photo.png -o photo.webp -q 85 -w 1200                    # image, redimensionnee
zia vieille-photo.jpg -o hd.jpg --upscale --factor 4          # agrandissement IA, duree estimee affichee
zia rip_dvd.vob -o film.mkv --remux-only                      # re-emballage strict, echec clair sinon
zia rip_dvd.mkv -o film.mkv --audio-track 2 --subtitle-track 4  # pistes precises (voir "zia probe")
zia film.mkv -o film.mkv --add-subtitle vostfr.srt --subtitle-lang fre --subtitle-title VOSTFR  # sous-titre externe
zia engines                                                   # moteurs et materiel detecte
zia --help                                                    # toutes les options
```

## Architecture

```
src/
├── ZiaConvert.Core/       modeles, routage, file d'attente — aucune dependance a un moteur
├── ZiaConvert.Engines/    moteurs FFmpeg, ImageMagick et Real-ESRGAN (arguments, progression, detection materielle)
├── ZiaConvert.App/        interface Avalonia
└── ZiaConvert.Cli/        ligne de commande
tests/
├── ZiaConvert.Core.Tests/
└── ZiaConvert.Engines.Tests/   dont des tests d'integration qui pilotent reellement ffmpeg
```

Tout moteur de conversion implemente une seule interface, `IConversionEngine` :

```csharp
public interface IConversionEngine
{
    string Name { get; }
    EngineAvailability CheckAvailability();
    bool CanHandle(ConversionRequest request);
    IAsyncEnumerable<ConversionProgress> ExecuteAsync(
        ConversionRequest request, CancellationToken cancellationToken = default);
}
```

Le reste de l'application (file d'attente, interface, ligne de commande) ne connait que cette interface : ajouter un moteur ne demande de modification nulle part ailleurs.

## Licence

[GPL v3](LICENSE.txt). ZiaConvert est livre avec un binaire FFmpeg compile en GPL v3 (`libx264`/`libx265` inclus), ce qui impose cette licence a l'ensemble — voir [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) pour le detail des composants tiers et les alternatives possibles.
